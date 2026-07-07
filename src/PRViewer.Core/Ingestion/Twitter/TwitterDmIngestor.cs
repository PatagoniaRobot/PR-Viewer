using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Ingestion.Twitter;

/// <summary>
/// Ingestor del export de mensajes directos de X/Twitter («Descargar un archivo de
/// tus datos»). Los DM viven en <c>data/direct-messages.js</c>: un archivo JavaScript
/// que asigna un array JSON a <c>window.YTD.direct_messages.partN</c>. Cada
/// <c>dmConversation</c> es un hilo independiente (uno por corresponsal), por lo que
/// un paquete de X normaliza a un paquete multi-hilo.
///
/// El titular se identifica por <c>data/account.js</c>; los demás participantes solo
/// figuran por su identificador numérico de usuario (el export no incluye @handles de
/// terceros), y así se reportan, sin inventar datos.
///
/// Nota sobre multimedia: el export de referencia con el que se desarrolló este
/// ingestor no contenía adjuntos (mediaUrls vacíos, carpeta de media sin archivos).
/// La asociación de archivos de <c>direct_messages_media/</c> a los mensajes sigue la
/// convención documentada (nombre con prefijo del id del mensaje) y está cubierta por
/// pruebas sintéticas; conviene confirmarla contra un export real con multimedia.
/// </summary>
public sealed class TwitterDmIngestor : IExportIngestor
{
    public Platform Platform => Platform.TwitterX;

    private const string DirectMessagesName = "direct-messages.js";
    private const string AccountName = "account.js";
    private const string YtdPrefix = "window.YTD.direct_messages";
    private static readonly string[] MediaDirs =
    {
        "data/direct_messages_media/",
        "data/direct_messages_group_media/",
    };

    public bool CanIngest(IInspectionSource source)
    {
        var entry = FindDirectMessages(source);
        if (entry is null)
            return false;

        // Confirmación por contenido: el archivo asigna al espacio window.YTD.direct_messages.
        using var stream = source.OpenRead(entry);
        using var reader = new StreamReader(stream);
        var head = new char[64];
        var read = reader.ReadBlock(head, 0, head.Length);
        return new string(head, 0, read).TrimStart().StartsWith(YtdPrefix, StringComparison.Ordinal);
    }

    public IngestedPackage Ingest(IInspectionSource source)
    {
        var dmEntry = FindDirectMessages(source)
            ?? throw new InvalidDataException($"«{source.DisplayName}» no contiene direct-messages.js de X/Twitter.");

        var owner = LoadOwner(source);
        var mediaEntries = CollectMediaEntries(source);

        using var document = LoadYtdArray(source, dmEntry)
            ?? throw new InvalidDataException("direct-messages.js no contiene un array JSON reconocible.");

        var threads = new List<ConversationThread>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.TryGetProperty("dmConversation", out var conversation))
                threads.Add(BuildThread(conversation, owner, source, mediaEntries));
        }

        return new IngestedPackage
        {
            Platform = Platform.TwitterX,
            Threads = threads,
        };
    }

    private static SourceEntry? FindDirectMessages(IInspectionSource source)
        => source.Entries.FirstOrDefault(e =>
            e.Name.Equals(DirectMessagesName, StringComparison.OrdinalIgnoreCase));

    private ConversationThread BuildThread(JsonElement conversation, OwnerIdentity owner,
        IInspectionSource source, IReadOnlyDictionary<string, SourceEntry> mediaEntries)
    {
        var conversationId = conversation.TryGetProperty("conversationId", out var idElem)
            ? idElem.GetString() ?? ""
            : "";

        var otherId = OtherParticipantId(conversationId, owner.AccountId);

        var messages = new List<ChatMessage>();
        if (conversation.TryGetProperty("messages", out var messagesElem)
            && messagesElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var messageElem in messagesElem.EnumerateArray())
                messages.Add(BuildMessage(messageElem, owner, mediaEntries));
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

        var attachments = ResolveAttachments(source, messages, mediaEntries);

        var title = string.IsNullOrEmpty(otherId)
            ? (string.IsNullOrEmpty(conversationId) ? "Conversación" : $"Conversación {conversationId}")
            : $"Conversación con {Label(otherId, owner)}";

        return new ConversationThread
        {
            Title = title,
            Participants = participants,
            DateRange = dateRange,
            Attachments = attachments,
            Messages = messages,
        };
    }

    private ChatMessage BuildMessage(JsonElement element, OwnerIdentity owner,
        IReadOnlyDictionary<string, SourceEntry> mediaEntries)
    {
        // El tipo de evento es el nombre de la única propiedad del objeto.
        var property = element.EnumerateObject().FirstOrDefault();
        var kind = property.Name;
        var body = property.Value;

        // Mensajes de contenido: creación normal o mensaje de bienvenida (ambos con texto).
        if (kind is "messageCreate" or "welcomeMessageCreate")
        {
            var senderId = GetString(body, "senderId");
            var text = GetString(body, "text") ?? "";
            var timestamp = ParseTimestamp(GetString(body, "createdAt"));
            var messageId = GetString(body, "id") ?? "";
            var attachmentName = ResolveAttachmentName(body, messageId, mediaEntries);

            return new ChatMessage(timestamp, Label(senderId, owner), text, attachmentName, IsSystemMessage: false);
        }

        // Otros eventos del hilo (reacciones, altas/bajas de participantes, cambios de
        // nombre): se registran como mensajes de sistema, sin remitente atribuido.
        return new ChatMessage(
            Timestamp: ParseTimestamp(GetString(body, "createdAt")),
            Sender: null,
            Text: SystemEventText(kind),
            AttachmentName: null,
            IsSystemMessage: true);
    }

    /// <summary>
    /// Nombre del adjunto para un mensaje con multimedia: el archivo local de
    /// direct_messages_media cuyo nombre empieza con el id del mensaje si está presente;
    /// en su defecto, el último segmento de la primera mediaUrl (marcará ausente).
    /// </summary>
    private static string? ResolveAttachmentName(JsonElement body, string messageId,
        IReadOnlyDictionary<string, SourceEntry> mediaEntries)
    {
        if (!body.TryGetProperty("mediaUrls", out var mediaUrls)
            || mediaUrls.ValueKind != JsonValueKind.Array
            || mediaUrls.GetArrayLength() == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(messageId))
        {
            var local = mediaEntries.Values.FirstOrDefault(e =>
                e.Name.StartsWith(messageId + "-", StringComparison.Ordinal));
            if (local is not null)
                return local.Name;
        }

        var firstUrl = mediaUrls[0].GetString();
        if (!string.IsNullOrEmpty(firstUrl))
        {
            var lastSegment = firstUrl.TrimEnd('/').Split('/').LastOrDefault();
            if (!string.IsNullOrEmpty(lastSegment))
                return lastSegment;
        }

        return string.IsNullOrEmpty(messageId) ? "media" : messageId;
    }

    private static List<AttachmentInfo> ResolveAttachments(IInspectionSource source,
        List<ChatMessage> messages, IReadOnlyDictionary<string, SourceEntry> mediaEntries)
    {
        var attachments = new List<AttachmentInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in messages)
        {
            if (message.AttachmentName is null || !seen.Add(message.AttachmentName))
                continue;

            var contentType = ContentTypes.FromFileName(message.AttachmentName);

            if (mediaEntries.TryGetValue(message.AttachmentName, out var entry))
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

    private static IReadOnlyDictionary<string, SourceEntry> CollectMediaEntries(IInspectionSource source)
    {
        var map = new Dictionary<string, SourceEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source.Entries)
        {
            if (MediaDirs.Any(dir => entry.Path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)))
                map.TryAdd(entry.Name, entry);
        }

        return map;
    }

    private OwnerIdentity LoadOwner(IInspectionSource source)
    {
        var accountEntry = source.Entries.FirstOrDefault(e =>
            e.Name.Equals(AccountName, StringComparison.OrdinalIgnoreCase));
        if (accountEntry is null)
            return OwnerIdentity.Unknown;

        try
        {
            using var document = LoadYtdArray(source, accountEntry);
            if (document is null)
                return OwnerIdentity.Unknown;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("account", out var account))
                    continue;

                return new OwnerIdentity(
                    GetString(account, "accountId") ?? "",
                    GetString(account, "accountDisplayName"),
                    GetString(account, "username"));
            }
        }
        catch (JsonException)
        {
            // account.js ilegible: se continúa sin identidad del titular.
        }

        return OwnerIdentity.Unknown;
    }

    /// <summary>Etiqueta legible de un id de usuario: el titular por su nombre, el resto por su id.</summary>
    private static string? Label(string? userId, OwnerIdentity owner)
    {
        if (string.IsNullOrEmpty(userId))
            return null;
        if (userId == owner.AccountId)
            return owner.DisplayName ?? (owner.Username is { } u ? "@" + u : userId);
        return userId;
    }

    /// <summary>El id del otro participante de un conversationId «A-B»; vacío si no se puede resolver.</summary>
    private static string OtherParticipantId(string conversationId, string ownerId)
    {
        var parts = conversationId.Split('-');
        if (parts.Length != 2)
            return "";
        if (parts[0] == ownerId)
            return parts[1];
        if (parts[1] == ownerId)
            return parts[0];
        return parts[0]; // sin identidad del titular: se toma el primero como corresponsal.
    }

    private static string SystemEventText(string kind) => kind switch
    {
        "reactionCreate" => "(reacción a un mensaje)",
        "conversationNameUpdate" => "(cambio de nombre de la conversación)",
        "joinConversation" => "(alta en la conversación)",
        "participantsJoin" => "(se unieron participantes)",
        "participantsLeave" => "(se retiraron participantes)",
        _ => $"(evento {kind})",
    };

    private static DateTime? ParseTimestamp(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value.UtcDateTime
            : null;
    }

    private static JsonDocument? LoadYtdArray(IInspectionSource source, SourceEntry entry)
    {
        using var stream = source.OpenRead(entry);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();

        // Se salta el prefijo «window.YTD.… = » hasta el inicio del array JSON.
        var start = text.IndexOf('[');
        return start < 0 ? null : JsonDocument.Parse(text[start..]);
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string ComputeSha256(IInspectionSource source, SourceEntry entry)
    {
        using var stream = source.OpenRead(entry);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Identidad del titular de la cuenta, tomada de account.js.</summary>
    private sealed record OwnerIdentity(string AccountId, string? DisplayName, string? Username)
    {
        public static OwnerIdentity Unknown { get; } = new("", null, null);
    }
}
