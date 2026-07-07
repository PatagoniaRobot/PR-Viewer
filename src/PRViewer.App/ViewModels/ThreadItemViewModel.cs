using PRViewer.Core.Model;

namespace PRViewer.App.ViewModels;

/// <summary>
/// Ítem de la lista de conversaciones del paquete (pestaña «Conversaciones»).
/// Representa un hilo; al seleccionarlo, el visor muestra sus mensajes parseados.
/// </summary>
public sealed class ThreadItemViewModel
{
    public ThreadItemViewModel(ConversationThread thread)
    {
        Thread = thread;
        Title = thread.Title;
        ParticipantsText = string.Join(", ", thread.Participants);
        MessageCountText = thread.MessageCount == 1 ? "1 mensaje" : $"{thread.MessageCount:N0} mensajes";

        var present = thread.Attachments.Count(a => a.IsPresent);
        var missing = thread.Attachments.Count - present;
        AttachmentText = thread.Attachments.Count == 0
            ? string.Empty
            : missing == 0 ? $"📎 {present}" : $"📎 {present} · ⚠ {missing} ausentes";
        HasMissingAttachments = missing > 0;
    }

    public ConversationThread Thread { get; }
    public string Title { get; }
    public string ParticipantsText { get; }
    public string MessageCountText { get; }
    public string AttachmentText { get; }
    public bool HasMissingAttachments { get; }
}
