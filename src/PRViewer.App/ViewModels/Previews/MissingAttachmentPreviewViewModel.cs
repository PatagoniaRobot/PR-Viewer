using PRViewer.Core.Model;

namespace PRViewer.App.ViewModels.Previews;

/// <summary>
/// Ficha de un adjunto referenciado en el chat pero AUSENTE del paquete:
/// la señal de export incompleto que el perito necesita ver antes de labrar acta.
/// </summary>
public sealed class MissingAttachmentPreviewViewModel : ViewModelBase
{
    public MissingAttachmentPreviewViewModel(AttachmentInfo attachment)
    {
        Name = attachment.Name;
        ContentType = attachment.ContentType;
    }

    public string Name { get; }
    public string ContentType { get; }
}
