using System.Security.Cryptography;
using PRViewer.App.Services;
using PRViewer.Core.Ingestion;
using PRViewer.Core.Sources;

namespace PRViewer.App.ViewModels.Previews;

/// <summary>
/// Ficha de metadatos para entradas sin render (video, audio, PDF, documentos):
/// nombre, tipo, tamaño y SHA-256 calculado en streaming, siempre en solo lectura.
/// </summary>
public sealed class FileInfoPreviewViewModel : ViewModelBase
{
    private string _sha256Text = "calculando…";

    public FileInfoPreviewViewModel(IInspectionSource source, SourceEntry entry, string? knownSha256)
    {
        EntryName = entry.Name;
        SizeText = FileKindResolver.FormatSize(entry.Size);
        var kind = FileKindResolver.Resolve(entry.Name);
        Glyph = FileKindResolver.Glyph(kind);
        ContentType = ContentTypes.FromFileName(entry.Name);

        if (knownSha256 is not null)
        {
            _sha256Text = knownSha256;
        }
        else
        {
            // Cálculo asíncrono para no congelar la UI con archivos grandes.
            _ = ComputeHashAsync(source, entry);
        }
    }

    public string EntryName { get; }
    public string SizeText { get; }
    public string Glyph { get; }
    public string ContentType { get; }

    public string Sha256Text
    {
        get => _sha256Text;
        private set => SetProperty(ref _sha256Text, value);
    }

    private async Task ComputeHashAsync(IInspectionSource source, SourceEntry entry)
    {
        try
        {
            var hash = await Task.Run(() =>
            {
                using var stream = source.OpenRead(entry);
                return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            });
            Sha256Text = hash;
        }
        catch (Exception ex) when (ex is System.IO.IOException or ObjectDisposedException)
        {
            Sha256Text = "no disponible";
        }
    }
}
