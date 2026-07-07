using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Ingestion.Meta;

/// <summary>
/// Base compartida de los ingestores de Meta en formato HTML («Descargar tu
/// información», opción HTML — el default de Meta). Instagram y Facebook usan
/// plantillas HTML distintas pero comparten los anclajes que este parser explota,
/// deliberadamente sin depender de las clases CSS ofuscadas (que Meta cambia entre
/// versiones):
///
/// - el remitente es un &lt;h2&gt; (que se muestra solo al cambiar de emisor: es «pegajoso»);
/// - cada mensaje termina en un &lt;div&gt; cuyo texto es la fecha en formato es-AR
///   («jun 11, 2026 9:59 am», con segundos en Facebook);
/// - el título del hilo es el &lt;h1&gt; de la cabecera;
/// - la multimedia son &lt;img&gt;/&lt;video&gt;/&lt;audio&gt;/&lt;source&gt; con src relativo al paquete.
///
/// Cada carpeta <c>inbox/&lt;hilo&gt;/</c> es un hilo (con uno o varios message_N.html).
/// Los mensajes se ordenan cronológicamente (el HTML de Meta viene del más nuevo al
/// más viejo); las horas no traen zona horaria, se toman tal como figuran.
/// </summary>
public abstract class MetaHtmlIngestor : IExportIngestor
{
    public abstract Platform Platform { get; }

    /// <summary>Prefijo de ruta del inbox de esta plataforma, p. ej. «your_instagram_activity/messages/inbox/».</summary>
    protected abstract string InboxPrefix { get; }

    private static readonly Regex TitlePattern =
        new(@"<h1[^>]*>(?<t>.*?)</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex H2Pattern =
        new(@"<h2[^>]*>(?<s>.*?)</h2>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    // Div de fecha en es-AR: delimita el fin de cada mensaje. La whitelist de meses
    // evita matchear texto arbitrario del usuario.
    private static readonly Regex TimestampPattern = new(
        @"<div[^>]*>\s*(?<date>(?:ene|feb|mar|abr|may|jun|jul|ago|sept?|oct|nov|dic)\s+\d{1,2},\s*\d{4}\s+\d{1,2}:\d{2}(?::\d{2})?\s*[ap]\.?\s?m\.?)\s*</div>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex DatePartsPattern = new(
        @"(?<mon>[a-zñ]{3,4})\s+(?<day>\d{1,2}),\s*(?<year>\d{4})\s+(?<hour>\d{1,2}):(?<min>\d{2})(?::(?<sec>\d{2}))?\s*(?<ap>[ap])\.?\s?m\.?",
        RegexOptions.IgnoreCase);

    private static readonly Regex MediaPattern = new(
        @"<(?:img|video|audio|source)\b[^>]*?\ssrc=""(?<u>[^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex TagPattern = new(@"<[^>]+>", RegexOptions.Singleline);
    private static readonly Regex BreakPattern = new(@"<br\s*/?>|</div>|</p>", RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ene"] = 1, ["feb"] = 2, ["mar"] = 3, ["abr"] = 4, ["may"] = 5, ["jun"] = 6,
        ["jul"] = 7, ["ago"] = 8, ["sep"] = 9, ["sept"] = 9, ["oct"] = 10, ["nov"] = 11, ["dic"] = 12,
    };

    public bool CanIngest(IInspectionSource source) => ThreadFolders(source).Count > 0;

    public IngestedPackage Ingest(IInspectionSource source)
    {
        var folders = ThreadFolders(source);
        if (folders.Count == 0)
            throw new InvalidDataException($"«{source.DisplayName}» no contiene mensajes de Meta en HTML.");

        var entriesByPath = new Dictionary<string, SourceEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source.Entries)
            entriesByPath.TryAdd(entry.Path, entry);

        var threads = folders
            .OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .Select(f => ParseThread(source, f.Value, entriesByPath))
            .ToList();

        return new IngestedPackage
        {
            Platform = Platform,
            Threads = threads,
        };
    }

    /// <summary>Agrupa los message_N.html por carpeta de hilo (clave = ruta del hilo).</summary>
    private Dictionary<string, List<SourceEntry>> ThreadFolders(IInspectionSource source)
    {
        var folders = new Dictionary<string, List<SourceEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in source.Entries)
        {
            if (!entry.Path.StartsWith(InboxPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsMessageFile(entry.Name))
                continue;

            var threadKey = entry.Path[..entry.Path.LastIndexOf('/')];
            if (!folders.TryGetValue(threadKey, out var list))
                folders[threadKey] = list = new List<SourceEntry>();
            list.Add(entry);
        }

        return folders;
    }

    private static bool IsMessageFile(string name)
        => name.StartsWith("message_", StringComparison.OrdinalIgnoreCase)
           && name.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

    private ConversationThread ParseThread(IInspectionSource source, List<SourceEntry> files,
        IReadOnlyDictionary<string, SourceEntry> entriesByPath)
    {
        // message_1, message_2, … en orden; dentro de cada archivo, del más nuevo al más viejo.
        files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        string? title = null;
        var messages = new List<ChatMessage>();
        var attachments = new List<AttachmentInfo>();
        var seenAttachments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var html = ReadAll(source, file);
            title ??= ExtractTitle(html);
            ParseMessagesInFile(html, source, entriesByPath, messages, attachments, seenAttachments);
        }

        // Orden cronológico ascendente y estable (Meta exporta del más nuevo al más viejo).
        var ordered = messages
            .Select((m, i) => (m, i))
            .OrderBy(x => x.m.Timestamp ?? DateTime.MaxValue)
            .ThenBy(x => x.i)
            .Select(x => x.m)
            .ToList();

        var participants = ordered
            .Where(m => m.Sender is not null)
            .Select(m => m.Sender!)
            .Distinct()
            .ToList();

        var timestamps = ordered.Where(m => m.Timestamp.HasValue).Select(m => m.Timestamp!.Value).ToList();
        var dateRange = timestamps.Count > 0
            ? new DateRange(timestamps.Min(), timestamps.Max())
            : new DateRange(null, null);

        return new ConversationThread
        {
            Title = title ?? "Conversación",
            Participants = participants,
            DateRange = dateRange,
            Attachments = attachments,
            Messages = ordered,
        };
    }

    private void ParseMessagesInFile(string html, IInspectionSource source,
        IReadOnlyDictionary<string, SourceEntry> entriesByPath,
        List<ChatMessage> messages, List<AttachmentInfo> attachments, HashSet<string> seenAttachments)
    {
        // Se descarta la cabecera (todo antes de </header>): tiene el título y el «Generado por…».
        var bodyStart = IndexAfter(html, "</header>");

        string? currentSender = null;
        var segmentStart = bodyStart;

        foreach (Match ts in TimestampPattern.Matches(html))
        {
            if (ts.Index < bodyStart)
                continue;

            var segment = html[segmentStart..ts.Index];
            segmentStart = ts.Index + ts.Length;

            // Remitente: el último <h2> del segmento actualiza el emisor «pegajoso».
            var h2s = H2Pattern.Matches(segment);
            if (h2s.Count > 0)
            {
                var sender = CleanText(h2s[^1].Groups["s"].Value);
                if (!string.IsNullOrEmpty(sender))
                    currentSender = sender;
            }

            var contentHtml = H2Pattern.Replace(segment, string.Empty);
            var text = CleanText(BreakPattern.Replace(contentHtml, "\n"));

            var attachmentName = ResolveMedia(contentHtml, source, entriesByPath, attachments, seenAttachments);
            var timestamp = ParseMetaDate(ts.Groups["date"].Value);

            // Cada div de fecha es un mensaje real; los que no tienen texto ni adjunto
            // local (stickers, GIFs o contenido externo) se conservan igual, con su
            // remitente y fecha, para no alterar el conteo.
            messages.Add(new ChatMessage(timestamp, currentSender, text, attachmentName, IsSystemMessage: false));
        }
    }

    /// <summary>
    /// Resuelve la multimedia local del segmento: cada src relativo se cruza con las
    /// entradas del paquete (presente + hash, o ausente si el HTML lo referencia pero
    /// el archivo no está). Devuelve el nombre del primer adjunto (para el indicador 📎).
    /// </summary>
    private static string? ResolveMedia(string contentHtml, IInspectionSource source,
        IReadOnlyDictionary<string, SourceEntry> entriesByPath,
        List<AttachmentInfo> attachments, HashSet<string> seenAttachments)
    {
        string? first = null;

        foreach (Match m in MediaPattern.Matches(contentHtml))
        {
            var url = WebUtility.HtmlDecode(m.Groups["u"].Value);
            // Solo archivos del paquete: se ignoran enlaces externos e incrustados.
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = url[(url.LastIndexOf('/') + 1)..];
            first ??= name;

            if (!seenAttachments.Add(url))
                continue;

            var contentType = ContentTypes.FromFileName(name);
            if (entriesByPath.TryGetValue(url, out var entry))
            {
                attachments.Add(new AttachmentInfo(
                    name, contentType, entry.Size, ComputeSha256(source, entry), IsPresent: true));
            }
            else
            {
                attachments.Add(new AttachmentInfo(name, contentType, Size: null, Sha256: null, IsPresent: false));
            }
        }

        return first;
    }

    private static string? ExtractTitle(string html)
    {
        var match = TitlePattern.Match(html);
        return match.Success ? CleanText(match.Groups["t"].Value) : null;
    }

    private static DateTime? ParseMetaDate(string raw)
    {
        var m = DatePartsPattern.Match(raw);
        if (!m.Success || !Months.TryGetValue(m.Groups["mon"].Value, out var month))
            return null;

        var day = int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture);
        var year = int.Parse(m.Groups["year"].Value, CultureInfo.InvariantCulture);
        var hour = int.Parse(m.Groups["hour"].Value, CultureInfo.InvariantCulture) % 12;
        if (m.Groups["ap"].Value.Equals("p", StringComparison.OrdinalIgnoreCase))
            hour += 12;
        var min = int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture);
        var sec = m.Groups["sec"].Success ? int.Parse(m.Groups["sec"].Value, CultureInfo.InvariantCulture) : 0;

        try
        {
            return new DateTime(year, month, day, hour, min, sec, DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Quita etiquetas, decodifica entidades y normaliza espacios.</summary>
    private static string CleanText(string html)
    {
        var stripped = TagPattern.Replace(html, string.Empty);
        var decoded = WebUtility.HtmlDecode(stripped);
        decoded = Regex.Replace(decoded, @"[ \t]+", " ");
        decoded = Regex.Replace(decoded, @" *\n *", "\n");
        decoded = Regex.Replace(decoded, @"\n{3,}", "\n\n");
        return decoded.Trim();
    }

    private static int IndexAfter(string html, string marker)
    {
        var idx = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? 0 : idx + marker.Length;
    }

    private static string ReadAll(IInspectionSource source, SourceEntry entry)
    {
        using var stream = source.OpenRead(entry);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string ComputeSha256(IInspectionSource source, SourceEntry entry)
    {
        using var stream = source.OpenRead(entry);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
