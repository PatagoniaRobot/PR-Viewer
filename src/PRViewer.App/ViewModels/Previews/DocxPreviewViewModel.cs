using PRViewer.App.Services;
using PRViewer.Core.Sources;

namespace PRViewer.App.ViewModels.Previews;

/// <summary>
/// Preview de texto de un .docx (extraído en memoria, sin diseño fiel).
/// El .doc binario legado no se parsea: cae a la ficha de metadatos.
/// </summary>
public sealed class DocxPreviewViewModel : ViewModelBase
{
    private const int MaxChars = 2_000_000;

    public DocxPreviewViewModel(IInspectionSource source, SourceEntry entry, string? knownSha256)
    {
        EntryName = entry.Name;
        SizeText = FileKindResolver.FormatSize(entry.Size);
        Sha256 = knownSha256;

        var text = DocxTextExtractor.TryExtract(source, entry, MaxChars);
        if (text is null)
        {
            ErrorText = "No se pudo leer como .docx; el archivo se conserva tal como llegó.";
            Text = string.Empty;
        }
        else
        {
            Text = text;
        }
    }

    public string EntryName { get; }
    public string SizeText { get; }
    public string? Sha256 { get; }
    public string Text { get; }
    public string? ErrorText { get; }
    public bool HasError => ErrorText is not null;
    public bool HasSha256 => Sha256 is not null;
}
