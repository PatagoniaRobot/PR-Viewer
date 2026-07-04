using System.IO;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.App.ViewModels.Previews;

/// <summary>
/// Preview de la conversación parseada: lista de mensajes normalizados
/// más la pestaña de texto crudo (el material tal como llegó, sin reescritura).
/// </summary>
public sealed class ChatPreviewViewModel : ViewModelBase
{
    private const int MaxRawChars = 2_000_000; // tope defensivo para el preview crudo

    private readonly IInspectionSource _source;
    private readonly SourceEntry _chatEntry;
    private string? _rawText;

    public ChatPreviewViewModel(IInspectionSource source, SourceEntry chatEntry, IReadOnlyList<ChatMessage> messages)
    {
        _source = source;
        _chatEntry = chatEntry;

        // Paleta de remitentes: índice estable por orden de aparición.
        var senderIndex = new Dictionary<string, int>();
        var items = new List<ChatMessageItem>(messages.Count);
        foreach (var message in messages)
        {
            var index = 0;
            if (message.Sender is { } sender && !senderIndex.TryGetValue(sender, out index))
            {
                index = senderIndex.Count;
                senderIndex[sender] = index;
            }

            items.Add(new ChatMessageItem(message, index));
        }

        Messages = items;
    }

    public IReadOnlyList<ChatMessageItem> Messages { get; }

    /// <summary>Texto crudo, leído bajo demanda al abrir la pestaña.</summary>
    public string RawText => _rawText ??= ReadRawText();

    private string ReadRawText()
    {
        using var stream = _source.OpenRead(_chatEntry);
        using var reader = new StreamReader(stream);

        var buffer = new char[MaxRawChars];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        var text = new string(buffer, 0, read);
        return reader.Peek() >= 0
            ? text + Environment.NewLine + "… (truncado para el preview; el archivo original está intacto)"
            : text;
    }
}

/// <summary>Mensaje individual listo para presentar.</summary>
public sealed class ChatMessageItem
{
    public ChatMessageItem(ChatMessage message, int senderIndex)
    {
        TimestampText = message.Timestamp?.ToString("dd/MM/yyyy HH:mm:ss") ?? "sin fecha";
        Sender = message.Sender;
        Text = message.Text;
        AttachmentName = message.AttachmentName;
        IsSystemMessage = message.IsSystemMessage;
        SenderIndex = senderIndex;
    }

    public string TimestampText { get; }
    public string? Sender { get; }
    public string Text { get; }
    public string? AttachmentName { get; }
    public bool IsSystemMessage { get; }

    /// <summary>Índice del remitente (orden de aparición) para asignarle color estable.</summary>
    public int SenderIndex { get; }

    public bool HasAttachment => AttachmentName is not null;
}
