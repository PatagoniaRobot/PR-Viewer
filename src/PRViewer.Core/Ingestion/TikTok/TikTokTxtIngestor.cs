using System.Globalization;
using System.Text.RegularExpressions;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Ingestion.TikTok;

/// <summary>
/// Ingestor del export de TikTok en formato TXT («Descargar tus datos», opción TXT).
/// Los mensajes directos viven en un .txt con bloques por conversación:
///
///   &gt;&gt;&gt; Chat History with &lt;usuario&gt;::
///   2026-07-03 23:00:11 UTC rominacofre860: hola
///
/// Cada bloque es un hilo. El marcador «&gt;&gt;&gt; Chat History with» aparece en inglés
/// aun en exports en español, así que sirve para detectar por contenido. Se recorren
/// todos los .txt del paquete con ese marcador (los mensajes directos y, si tuvieran
/// datos, el chat de grupo); las secciones vacías traen «No hay datos…» y se ignoran.
///
/// TikTok no incluye multimedia física: los videos compartidos figuran como URLs de
/// tiktokv.com, que se registran como adjuntos «referenciados, no presentes».
/// </summary>
public sealed partial class TikTokTxtIngestor : IExportIngestor
{
    public Platform Platform => Platform.TikTok;

    // Encabezado de bloque: «>>> Chat History with <nombre>::» (los «:» finales varían).
    [GeneratedRegex(@"^>>> Chat History with (?<name>.*?):*\s*$")]
    private static partial Regex BlockHeaderPattern();

    // Línea de mensaje: «YYYY-MM-DD HH:MM:SS UTC <usuario>: <texto>».
    [GeneratedRegex(@"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) UTC (?<sender>[^:]+): (?<text>.*)$")]
    private static partial Regex MessagePattern();

    // Video/foto compartido: URL de tiktokv.com/share/<tipo>/<id>.
    [GeneratedRegex(@"tiktokv\.com/share/(?<kind>[a-z]+)/(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SharePattern();

    public bool CanIngest(IInspectionSource source)
        => source.Entries.Any(e => IsTxt(e) && HasChatMarker(source, e));

    public IngestedPackage Ingest(IInspectionSource source)
    {
        var threads = new List<ConversationThread>();

        foreach (var entry in source.Entries)
        {
            if (!IsTxt(entry) || !HasChatMarker(source, entry))
                continue;

            threads.AddRange(ParseThreads(source, entry));
        }

        if (threads.Count == 0)
            throw new InvalidDataException($"«{source.DisplayName}» no contiene chats de TikTok reconocibles.");

        return new IngestedPackage
        {
            Platform = Platform.TikTok,
            Threads = threads,
        };
    }

    private static bool IsTxt(SourceEntry entry)
        => entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lee las primeras líneas y verifica que aparezca un encabezado de conversación.</summary>
    private static bool HasChatMarker(IInspectionSource source, SourceEntry entry)
    {
        using var stream = source.OpenRead(entry);
        using var reader = new StreamReader(stream);

        for (var i = 0; i < 40; i++)
        {
            var line = reader.ReadLine();
            if (line is null)
                break;
            if (BlockHeaderPattern().IsMatch(line))
                return true;
        }

        return false;
    }

    private static List<ConversationThread> ParseThreads(IInspectionSource source, SourceEntry entry)
    {
        var threads = new List<ConversationThread>();

        using var stream = source.OpenRead(entry);
        using var reader = new StreamReader(stream);

        string? title = null;
        var messages = new List<ChatMessage>();

        void FlushThread()
        {
            if (title is not null)
                threads.Add(BuildThread(title, messages));
            messages = new List<ChatMessage>();
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var header = BlockHeaderPattern().Match(line);
            if (header.Success)
            {
                FlushThread();
                title = header.Groups["name"].Value.Trim();
                continue;
            }

            if (title is null)
                continue; // líneas antes del primer encabezado (p. ej. «No hay datos…»)

            var message = MessagePattern().Match(line);
            if (message.Success)
            {
                messages.Add(CreateMessage(message));
            }
            else if (line.Length > 0 && messages.Count > 0)
            {
                // Continuación de un mensaje multilínea: se anexa tal como llegó.
                var previous = messages[^1];
                messages[^1] = previous with { Text = previous.Text + "\n" + line };
            }
        }

        FlushThread();
        return threads;
    }

    private static ChatMessage CreateMessage(Match message)
    {
        var timestamp = ParseTimestamp(message.Groups["ts"].Value);
        var sender = message.Groups["sender"].Value.Trim();
        var text = message.Groups["text"].Value;

        // Video/foto compartido: se registra como adjunto referenciado (no presente).
        var share = SharePattern().Match(text);
        var attachmentName = share.Success
            ? $"TikTok {share.Groups["kind"].Value} {share.Groups["id"].Value}"
            : null;

        return new ChatMessage(timestamp, sender, text, attachmentName, IsSystemMessage: false);
    }

    private static ConversationThread BuildThread(string title, List<ChatMessage> messages)
    {
        var participants = messages
            .Where(m => m.Sender is not null)
            .Select(m => m.Sender!)
            .Distinct()
            .ToList();

        var timestamps = messages.Where(m => m.Timestamp.HasValue).Select(m => m.Timestamp!.Value).ToList();
        var dateRange = timestamps.Count > 0
            ? new DateRange(timestamps.Min(), timestamps.Max())
            : new DateRange(null, null);

        // Adjuntos referenciados: los videos/fotos compartidos son URLs, nunca están
        // en el paquete (TikTok no incluye multimedia física) → siempre ausentes.
        var attachments = new List<AttachmentInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var message in messages)
        {
            if (message.AttachmentName is null || !seen.Add(message.AttachmentName))
                continue;

            attachments.Add(new AttachmentInfo(
                message.AttachmentName, "referencia externa (TikTok)",
                Size: null, Sha256: null, IsPresent: false));
        }

        return new ConversationThread
        {
            Title = $"Conversación con {title}",
            Participants = participants,
            DateRange = dateRange,
            Attachments = attachments,
            Messages = messages,
        };
    }

    private static DateTime? ParseTimestamp(string raw)
        => DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;
}
