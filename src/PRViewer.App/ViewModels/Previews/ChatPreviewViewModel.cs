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

    private readonly IInspectionSource? _source;
    private readonly SourceEntry? _chatEntry;
    private string? _rawText;

    /// <summary>
    /// Preview de un hilo de conversación. El texto crudo es opcional: solo lo hay
    /// cuando el hilo proviene de un único archivo del paquete (p. ej. el _chat.txt de
    /// WhatsApp). En exports multi-hilo (X, Meta) un hilo no tiene archivo crudo propio.
    /// </summary>
    public ChatPreviewViewModel(IReadOnlyList<ChatMessage> messages,
        IInspectionSource? rawSource = null, SourceEntry? rawEntry = null)
    {
        _source = rawSource;
        _chatEntry = rawEntry;

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

    /// <summary>Indica si el hilo tiene un archivo crudo asociado (pestaña «Texto crudo»).</summary>
    public bool HasRawText => _source is not null && _chatEntry is not null;

    /// <summary>Texto crudo, leído bajo demanda al abrir la pestaña.</summary>
    public string RawText => _rawText ??= ReadRawText();

    private string ReadRawText()
    {
        if (_source is null || _chatEntry is null)
            return string.Empty;

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
