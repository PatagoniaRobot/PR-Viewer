using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Ingestion.Telegram;

/// <summary>
/// Ingestor del export de Telegram Desktop en HTML («Exportar datos de Telegram»,
/// opción HTML). A diferencia de Meta, Telegram usa clases CSS estables:
///
/// - cada carpeta <c>chats/chat_NNN/</c> es un hilo, con uno o varios messages[N].html;
/// - el nombre del chat está en el &lt;div class="text bold"&gt; de la cabecera;
/// - cada mensaje es &lt;div class="message default clearfix[ joined]"&gt;; los «joined»
///   omiten el remitente (mismo emisor que el anterior: se arrastra el último visto);
/// - el remitente está en &lt;div class="from_name"&gt;, el texto en &lt;div class="text"&gt;,
///   y la fecha completa CON zona horaria en el atributo title del div de fecha
///   («17.02.2021 12:57:04 UTC-03:00»);
/// - la multimedia son &lt;a href="…"&gt; relativos a la carpeta del chat (photos/,
///   video_files/, voice_messages/, etc.), que se cruzan con el paquete.
///
/// Los mensajes de servicio (separadores de fecha, altas/bajas) se omiten: no forman
/// parte de la conversación y no traen timestamp propio.
/// </summary>
public sealed class TelegramHtmlIngestor : IExportIngestor
{
    public Platform Platform => Platform.Telegram;

    private static readonly Regex MessageEntryPattern =
        new(@"(?:^|/)chats/chat_[^/]+/messages\d*\.html$", RegexOptions.IgnoreCase);

    private static readonly Regex TitlePattern =
        new(@"<div class=""text bold"">(?<t>.*?)</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    // Inicio de cada mensaje; «kind» distingue default/service y joined.
    private static readonly Regex MessageStartPattern =
        new(@"<div class=""message (?<kind>[^""]*)""[^>]*>", RegexOptions.IgnoreCase);

    private static readonly Regex FromNamePattern =
        new(@"<div class=""from_name"">(?<n>.*?)</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex TextPattern =
        new(@"<div class=""text"">(?<x>.*?)</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex DatePattern =
        new(@"class=""[^""]*date details""[^>]*title=""(?<dt>[^""]+)""", RegexOptions.IgnoreCase);

    private static readonly Regex DatePartsPattern =
        new(@"(?<d>\d{1,2})\.(?<mo>\d{1,2})\.(?<y>\d{4})\s+(?<h>\d{1,2}):(?<mi>\d{2}):(?<s>\d{2})(?:\s*UTC(?<off>[+-]\d{2}:\d{2}))?");

    // Ancla de multimedia: Telegram envuelve cada adjunto en un <a> cuya clase termina
    // en «_wrap» (photo_wrap, video_file_wrap, voice_message_wrap…) o contiene «media_».
    // Exigirla descarta la navegación y los enlaces a css/js.
    private static readonly Regex MediaAnchorPattern =
        new(@"<a class=""[^""]*(?:_wrap|media_)[^""]*""[^>]*\shref=""(?<h>[^""]+)""", RegexOptions.IgnoreCase);

    private static readonly Regex TagPattern = new(@"<[^>]+>", RegexOptions.Singleline);
    private static readonly Regex BreakPattern = new(@"<br\s*/?>", RegexOptions.IgnoreCase);
    private static readonly Regex TrailingNumberPattern = new(@"(\d+)\.html$", RegexOptions.IgnoreCase);

    public bool CanIngest(IInspectionSource source)
    {
        var entry = source.Entries.FirstOrDefault(e => MessageEntryPattern.IsMatch(e.Path));
        if (entry is null)
            return false;

        // Confirmación por contenido: el archivo trae mensajes de Telegram.
        using var stream = source.OpenRead(entry);
        using var reader = new StreamReader(stream);
        var head = new char[4096];
        var read = reader.ReadBlock(head, 0, head.Length);
        return new string(head, 0, read).Contains("class=\"message ", StringComparison.OrdinalIgnoreCase);
    }

    public IngestedPackage Ingest(IInspectionSource source)
    {
        var entriesByPath = new Dictionary<string, SourceEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source.Entries)
            entriesByPath.TryAdd(entry.Path, entry);

        // Agrupa los messages[N].html por carpeta de chat.
        var byChat = new Dictionary<string, List<SourceEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source.Entries)
        {
            if (!MessageEntryPattern.IsMatch(entry.Path))
                continue;
            var chatDir = entry.Path[..entry.Path.LastIndexOf('/')];
            if (!byChat.TryGetValue(chatDir, out var list))
                byChat[chatDir] = list = new List<SourceEntry>();
            list.Add(entry);
        }

        if (byChat.Count == 0)
            throw new InvalidDataException($"«{source.DisplayName}» no contiene chats de Telegram en HTML.");

        // Se descartan los chats sin mensajes reales (solo servicio: separadores de
        // fecha, canales de notificaciones), que no aportan conversación a inspeccionar.
        var threads = byChat
            .OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .Select(c => ParseThread(source, c.Key, c.Value, entriesByPath))
            .Where(t => t.MessageCount > 0)
            .ToList();

        return new IngestedPackage
        {
            Platform = Platform.Telegram,
            Threads = threads,
        };
    }

    private ConversationThread ParseThread(IInspectionSource source, string chatDir,
        List<SourceEntry> files, IReadOnlyDictionary<string, SourceEntry> entriesByPath)
    {
        // Orden natural: messages.html, messages2.html, … (Telegram es cronológico ascendente).
        files.Sort((a, b) => FileOrder(a.Name).CompareTo(FileOrder(b.Name)));

        string? title = null;
        var messages = new List<ChatMessage>();
        var attachments = new List<AttachmentInfo>();
        var seenAttachments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? currentSender = null;

        foreach (var file in files)
        {
            var html = ReadAll(source, file);
            title ??= ExtractTitle(html);
            ParseMessagesInFile(html, chatDir, source, entriesByPath, messages, attachments, seenAttachments, ref currentSender);
        }

        var participants = messages
            .Where(m => m.Sender is not null)
            .Select(m => m.Sender!)
            .Distinct()
            .ToList();

        var timestamps = messages.Where(m => m.Timestamp.HasValue).Select(m => m.Timestamp!.Value).ToList();
        var dateRange = timestamps.Count > 0
            ? new DateRange(timestamps.Min(), timestamps.Max())
            : new DateRange(null, null);

        return new ConversationThread
        {
            Title = title ?? chatDir[(chatDir.LastIndexOf('/') + 1)..],
            Participants = participants,
            DateRange = dateRange,
            Attachments = attachments,
            Messages = messages,
        };
    }

    private void ParseMessagesInFile(string html, string chatDir, IInspectionSource source,
        IReadOnlyDictionary<string, SourceEntry> entriesByPath,
        List<ChatMessage> messages, List<AttachmentInfo> attachments, HashSet<string> seenAttachments,
        ref string? currentSender)
    {
        var starts = MessageStartPattern.Matches(html);
        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            var blockEnd = i + 1 < starts.Count ? starts[i + 1].Index : html.Length;
            var block = html[(start.Index + start.Length)..blockEnd];
            var kind = start.Groups["kind"].Value;

            // Mensajes de servicio (separadores de fecha, altas/bajas): fuera de la conversación.
            if (kind.Contains("service", StringComparison.OrdinalIgnoreCase))
                continue;

            var fromName = FromNamePattern.Match(block);
            if (fromName.Success)
            {
                var sender = CleanText(fromName.Groups["n"].Value);
                if (!string.IsNullOrEmpty(sender))
                    currentSender = sender;
            }

            var timestamp = ParseDate(DatePattern.Match(block).Groups["dt"].Value);

            var textMatch = TextPattern.Match(block);
            var text = textMatch.Success ? CleanText(BreakPattern.Replace(textMatch.Groups["x"].Value, "\n")) : "";

            var attachmentName = ResolveMedia(block, chatDir, source, entriesByPath, attachments, seenAttachments);

            messages.Add(new ChatMessage(timestamp, currentSender, text, attachmentName, IsSystemMessage: false));
        }
    }

    private static string? ResolveMedia(string block, string chatDir, IInspectionSource source,
        IReadOnlyDictionary<string, SourceEntry> entriesByPath,
        List<AttachmentInfo> attachments, HashSet<string> seenAttachments)
    {
        string? first = null;

        foreach (Match m in MediaAnchorPattern.Matches(block))
        {
            var href = WebUtility.HtmlDecode(m.Groups["h"].Value);
            // Solo archivos locales de media: se descartan enlaces externos y navegación.
            if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("#", StringComparison.Ordinal)
                || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || href.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                continue;

            var fullPath = CombinePath(chatDir, href);
            var name = fullPath[(fullPath.LastIndexOf('/') + 1)..];
            first ??= name;

            if (!seenAttachments.Add(fullPath))
                continue;

            var contentType = ContentTypes.FromFileName(name);
            if (entriesByPath.TryGetValue(fullPath, out var entry))
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

    /// <summary>Fecha de Telegram: «17.02.2021 12:57:04 UTC-03:00» → hora local mostrada.</summary>
    private static DateTime? ParseDate(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;

        var m = DatePartsPattern.Match(raw);
        if (!m.Success)
            return null;

        try
        {
            var day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(m.Groups["mo"].Value, CultureInfo.InvariantCulture);
            var year = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
            var hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
            var min = int.Parse(m.Groups["mi"].Value, CultureInfo.InvariantCulture);
            var sec = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
            // Se conserva la hora local tal como la mostró Telegram (el offset queda documentado en el HTML).
            return new DateTime(year, month, day, hour, min, sec, DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Une la carpeta del chat con un href relativo, resolviendo «.» y «..».</summary>
    private static string CombinePath(string baseDir, string relative)
    {
        var segments = new List<string>(baseDir.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(part);
            }
        }

        return string.Join('/', segments);
    }

    private static int FileOrder(string name)
    {
        var m = TrailingNumberPattern.Match(name);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
    }

    private static string CleanText(string html)
    {
        var stripped = TagPattern.Replace(html, string.Empty);
        var decoded = WebUtility.HtmlDecode(stripped);
        decoded = Regex.Replace(decoded, @"[ \t]+", " ");
        decoded = Regex.Replace(decoded, @" *\n *", "\n");
        decoded = Regex.Replace(decoded, @"\n{3,}", "\n\n");
        return decoded.Trim();
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
