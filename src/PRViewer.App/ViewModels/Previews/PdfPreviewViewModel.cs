using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media.Imaging;
using PRViewer.App.Services;
using PRViewer.Core.Sources;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace PRViewer.App.ViewModels.Previews;

/// <summary>
/// Preview de PDF renderizado con Windows.Data.Pdf (API del propio sistema
/// operativo, sin dependencias de terceros). Todo el pipeline es en memoria:
/// stream de la fuente → PdfDocument → páginas como imágenes. Nada toca disco.
///
/// Como el contenido es visual, queda sujeto al mismo estado de visibilidad
/// que las imágenes: oculto por defecto, revelado explícito del perito.
/// </summary>
public sealed class PdfPreviewViewModel : ViewModelBase
{
    private const int MaxPages = 30;
    private const uint RenderWidth = 1200;

    private readonly IInspectionSource _source;
    private readonly SourceEntry _entry;
    private readonly MediaVisibilityState _visibility;
    private bool _revealedIndividually;
    private bool _renderStarted;
    private IReadOnlyList<PdfPageItem> _pages = Array.Empty<PdfPageItem>();
    private string _renderStatusText = string.Empty;

    public PdfPreviewViewModel(IInspectionSource source, SourceEntry entry,
        MediaVisibilityState visibility, string? knownSha256)
    {
        _source = source;
        _entry = entry;
        _visibility = visibility;
        _visibility.PropertyChanged += OnVisibilityChanged;

        EntryName = entry.Name;
        SizeText = FileKindResolver.FormatSize(entry.Size);
        Sha256 = knownSha256;
        RevealCommand = new RelayCommand(_ =>
        {
            _revealedIndividually = true;
            OnVisibilityChanged(this, new PropertyChangedEventArgs(null));
        });
    }

    public string EntryName { get; }
    public string SizeText { get; }
    public string? Sha256 { get; }
    public RelayCommand RevealCommand { get; }

    public bool IsContentVisible => _visibility.ShowMedia || _revealedIndividually;
    public bool IsContentHidden => !IsContentVisible;

    public IReadOnlyList<PdfPageItem> Pages
    {
        get => _pages;
        private set => SetProperty(ref _pages, value);
    }

    public string RenderStatusText
    {
        get => _renderStatusText;
        private set => SetProperty(ref _renderStatusText, value);
    }

    private void OnVisibilityChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(IsContentVisible));
        RaisePropertyChanged(nameof(IsContentHidden));

        // El render arranca recién en el primer revelado, una sola vez.
        if (IsContentVisible && !_renderStarted)
        {
            _renderStarted = true;
            _ = RenderAsync();
        }
    }

    private async Task RenderAsync()
    {
        RenderStatusText = "Renderizando PDF en memoria…";
        try
        {
            var pages = await Task.Run(async () =>
            {
                // Copia en memoria del PDF (la fuente permanece en solo lectura).
                using var input = _source.OpenRead(_entry);
                var buffer = new MemoryStream();
                input.CopyTo(buffer);
                buffer.Position = 0;

                var document = await PdfDocument.LoadFromStreamAsync(buffer.AsRandomAccessStream());

                var count = (int)Math.Min(document.PageCount, MaxPages);
                var rendered = new List<PdfPageItem>(count);
                for (var i = 0; i < count; i++)
                {
                    using var page = document.GetPage((uint)i);
                    using var renderTarget = new InMemoryRandomAccessStream();
                    await page.RenderToStreamAsync(renderTarget, new PdfPageRenderOptions { DestinationWidth = RenderWidth });

                    renderTarget.Seek(0);
                    var pageBuffer = new MemoryStream();
                    renderTarget.AsStreamForRead().CopyTo(pageBuffer);
                    pageBuffer.Position = 0;

                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = pageBuffer;
                    image.EndInit();
                    image.Freeze();

                    rendered.Add(new PdfPageItem(i + 1, image));
                }

                return (rendered, total: (int)document.PageCount);
            });

            Pages = pages.rendered;
            RenderStatusText = pages.total > pages.rendered.Count
                ? $"{pages.rendered.Count} de {pages.total} páginas (tope del preview: {MaxPages})"
                : $"{pages.total} página(s)";
        }
        catch (Exception ex)
        {
            // PDF cifrado, corrupto o irreconocible: se informa sin tocar el material.
            Pages = Array.Empty<PdfPageItem>();
            RenderStatusText = $"No se pudo renderizar el PDF ({ex.Message.Trim()}). " +
                               "Puede estar protegido con contraseña o dañado; el archivo se conserva tal como llegó.";
        }
    }
}

/// <summary>Página renderizada del PDF.</summary>
public sealed record PdfPageItem(int PageNumber, BitmapImage Image);
