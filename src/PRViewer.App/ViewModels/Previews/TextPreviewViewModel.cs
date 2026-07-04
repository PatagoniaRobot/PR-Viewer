using System.IO;
using PRViewer.Core.Sources;

namespace PRViewer.App.ViewModels.Previews;

/// <summary>Preview de texto plano genérico (archivos .txt que no son el chat, .csv, etc.).</summary>
public sealed class TextPreviewViewModel : ViewModelBase
{
    private const int MaxChars = 2_000_000;

    public TextPreviewViewModel(IInspectionSource source, SourceEntry entry)
    {
        EntryName = entry.Name;
        SizeText = Services.FileKindResolver.FormatSize(entry.Size);

        using var stream = source.OpenRead(entry);
        using var reader = new StreamReader(stream);
        var buffer = new char[MaxChars];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        var text = new string(buffer, 0, read);
        Text = reader.Peek() >= 0
            ? text + Environment.NewLine + "… (truncado para el preview; el archivo original está intacto)"
            : text;
    }

    public string EntryName { get; }
    public string SizeText { get; }
    public string Text { get; }
}
