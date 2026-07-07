using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Ingestion.WhatsApp;

/// <summary>
/// Ingestor del export nativo de WhatsApp («Exportar chat»): un _chat.txt de texto
/// plano, una línea por mensaje, más los archivos de media adjuntos.
/// Soporta los dos formatos de línea conocidos (iOS con corchetes, Android con guion)
/// y los marcadores de adjunto en inglés y español.
/// </summary>
public sealed partial class WhatsAppTxtIngestor : IExportIngestor
{
    public Platform Platform => Platform.WhatsApp;

    // Formato iOS:      [25/6/2024, 14:03:12] Juan Pérez: Hola
    [GeneratedRegex(@"^\[(?<ts>[^\]]+)\]\s(?<rest>.*)$")]
    private static partial Regex IosLinePattern();

    // Formato Android:  25/6/24 14:03 - Juan Pérez: Hola   (o «25/6/2024, 2:03 p. m. - ...»)
    [GeneratedRegex(@"^(?<ts>\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4},?\s+\d{1,2}:\d{2}(?::\d{2})?(?:\s?[ap]\.?\s?m\.?)?)\s-\s(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex AndroidLinePattern();

    // Cuerpo «Remitente: texto». El remitente no contiene «:» (WhatsApp lo garantiza en el export).
    [GeneratedRegex(@"^(?<sender>[^:]+?):\s(?<text>.*)$", RegexOptions.Singleline)]
    private static partial Regex SenderPattern();

    // Adjuntos, variantes iOS (<attached: f> / <adjunto: f>) y Android (f (file attached) / f (archivo adjunto)).
    [GeneratedRegex(@"<(?:attached|adjunto):\s*(?<file>[^>]+)>|(?<file>\S[^(]*?)\s*\((?:file attached|archivo adjunto)\)", RegexOptions.IgnoreCase)]
    private static partial Regex AttachmentPattern();

    // Formatos de fecha aceptados, día-primero (locale argentino) antes que mes-primero.
    private static readonly string[] TimestampFormats =
    {
        "d/M/yyyy H:mm:ss", "d/M/yyyy H:mm", "d/M/yy H:mm:ss", "d/M/yy H:mm",
        "d/M/yyyy h:mm:ss tt", "d/M/yyyy h:mm tt", "d/M/yy h:mm:ss tt", "d/M/yy h:mm tt",
        "M/d/yyyy H:mm:ss", "M/d/yyyy H:mm", "M/d/yy H:mm:ss", "M/d/yy H:mm",
        "M/d/yyyy h:mm:ss tt", "M/d/yyyy h:mm tt", "M/d/yy h:mm:ss tt", "M/d/yy h:mm tt",
    };

    public bool CanIngest(IInspectionSource source)
        => FindChatEntry(source) is not null;

    public IngestedPackage Ingest(IInspectionSource source)
    {
        var chatEntry = FindChatEntry(source)
            ?? throw new InvalidDataException($"«{source.DisplayName}» no contiene un chat de WhatsApp reconocible.");

        var messages = ParseMessages(source, chatEntry);

        // Participantes: remitentes distintos, en orden de aparición.
        var participants = messages
            .Where(m => m.Sender is not null)
            .Select(m => m.Sender!)
            .Distinct()
            .ToList();

        // Rango de fechas sobre los timestamps parseables.
        var timestamps = messages.Where(m => m.Timestamp.HasValue).Select(m => m.Timestamp!.Value).ToList();
        var dateRange = timestamps.Count > 0
            ? new DateRange(timestamps.Min(), timestamps.Max())
            : new DateRange(null, null);

        var attachments = ResolveAttachments(source, messages);

        // WhatsApp exporta un único hilo por paquete: título por participantes,
        // con el nombre del archivo del chat como respaldo.
        var title = participants.Count > 0
            ? string.Join(", ", participants)
            : chatEntry.Name;

        var thread = new ConversationThread
        {
            Title = title,
            Participants = participants,
            DateRange = dateRange,
            Attachments = attachments,
            Messages = messages,
        };

        return new IngestedPackage
        {
            Platform = Platform.WhatsApp,
            Threads = new[] { thread },
        };
    }

    /// <summary>
    /// Busca la entrada del chat por contenido: prioriza «_chat.txt» (nombre nativo del
    /// export) pero acepta cualquier .txt cuyas primeras líneas tengan formato WhatsApp.
    /// </summary>
    private static SourceEntry? FindChatEntry(IInspectionSource source)
    {
        var candidates = source.Entries
            .Where(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Name.Equals("_chat.txt", StringComparison.OrdinalIgnoreCase));

        foreach (var candidate in candidates)
        {
            if (LooksLikeWhatsAppChat(source, candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>Lee las primeras líneas y verifica que al menos una tenga formato de mensaje.</summary>
    private static bool LooksLikeWhatsAppChat(IInspectionSource source, SourceEntry entry)
    {
        using var stream = source.OpenRead(entry);
        using var reader = new StreamReader(stream);

        for (var i = 0; i < 20; i++)
        {
            var line = reader.ReadLine();
            if (line is null)
                break;

            line = StripDirectionalMarks(line);
            if (IosLinePattern().IsMatch(line) || AndroidLinePattern().IsMatch(line))
                return true;
        }

        return false;
    }

    private static List<ChatMessage> ParseMessages(IInspectionSource source, SourceEntry chatEntry)
    {
        var messages = new List<ChatMessage>();

        using var stream = source.OpenRead(chatEntry);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = StripDirectionalMarks(line);

            var match = IosLinePattern().Match(line);
            if (!match.Success)
                match = AndroidLinePattern().Match(line);

            if (match.Success)
            {
                messages.Add(CreateMessage(match.Groups["ts"].Value, match.Groups["rest"].Value));
            }
            else if (messages.Count > 0)
            {
                // Línea de continuación de un mensaje multilínea: se anexa tal como llegó.
                var previous = messages[^1];
                messages[^1] = previous with { Text = previous.Text + "\n" + line };
            }
            // Líneas sueltas antes del primer mensaje se ignoran (no hay a qué anexarlas).
        }

        return messages;
    }

    private static ChatMessage CreateMessage(string rawTimestamp, string body)
    {
        var timestamp = ParseTimestamp(rawTimestamp);

        var senderMatch = SenderPattern().Match(body);
        if (!senderMatch.Success)
        {
            // Sin «Remitente: »: mensaje de sistema (cifrado, alta de grupo, etc.).
            return new ChatMessage(timestamp, null, body, null, IsSystemMessage: true);
        }

        var sender = senderMatch.Groups["sender"].Value.Trim();
        var text = senderMatch.Groups["text"].Value;

        var attachmentMatch = AttachmentPattern().Match(text);
        var attachmentName = attachmentMatch.Success
            ? attachmentMatch.Groups["file"].Value.Trim()
            : null;

        return new ChatMessage(timestamp, sender, text, attachmentName, IsSystemMessage: false);
    }

    private static DateTime? ParseTimestamp(string raw)
    {
        // Normalización: «, » entre fecha y hora → espacio; «p. m.» / «a.m.» → PM / AM.
        var normalized = StripDirectionalMarks(raw)
            .Replace(",", " ")
            .Trim();
        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = Regex.Replace(normalized, @"(?i)a\.?\s?m\.?", "AM");
        normalized = Regex.Replace(normalized, @"(?i)p\.?\s?m\.?", "PM");
        normalized = normalized.Replace('.', '/').Replace('-', '/');

        if (DateTime.TryParseExact(normalized, TimestampFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var result))
        {
            return result;
        }

        return null;
    }

    /// <summary>
    /// Cruza los adjuntos referenciados en el chat con las entradas reales del paquete.
    /// Los presentes se hashean (SHA-256, en streaming); los ausentes quedan marcados,
    /// que es justamente la señal de export incompleto que el perito necesita ver.
    /// </summary>
    private static List<AttachmentInfo> ResolveAttachments(IInspectionSource source, List<ChatMessage> messages)
    {
        var entriesByName = new Dictionary<string, SourceEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source.Entries)
            entriesByName.TryAdd(entry.Name, entry);

        var attachments = new List<AttachmentInfo>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in messages)
        {
            if (message.AttachmentName is null || !seenNames.Add(message.AttachmentName))
                continue;

            var contentType = ContentTypes.FromFileName(message.AttachmentName);

            if (entriesByName.TryGetValue(message.AttachmentName, out var entry))
            {
                attachments.Add(new AttachmentInfo(
                    message.AttachmentName, contentType, entry.Size,
                    ComputeSha256(source, entry), IsPresent: true));
            }
            else
            {
                attachments.Add(new AttachmentInfo(
                    message.AttachmentName, contentType, Size: null,
                    Sha256: null, IsPresent: false));
            }
        }

        return attachments;
    }

    private static string ComputeSha256(IInspectionSource source, SourceEntry entry)
    {
        using var stream = source.OpenRead(entry);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Quita las marcas direccionales Unicode (U+200E/U+200F) que WhatsApp intercala.</summary>
    private static string StripDirectionalMarks(string value)
        => value.Replace("‎", string.Empty).Replace("‏", string.Empty);
}
