using System.ComponentModel;
using System.Windows.Media.Imaging;
using PRViewer.App.Services;
using PRViewer.Core.Sources;

namespace PRViewer.App.ViewModels.Previews;

/// <summary>
/// Preview de imagen, decodificada en memoria y sujeta al estado de visibilidad:
/// oculta por defecto, se revela con el interruptor global o con el botón propio.
/// </summary>
public sealed class ImagePreviewViewModel : ViewModelBase
{
    private readonly IInspectionSource _source;
    private readonly SourceEntry _entry;
    private readonly MediaVisibilityState _visibility;
    private bool _revealedIndividually;
    private BitmapImage? _image;
    private bool _decodeFailed;

    public ImagePreviewViewModel(IInspectionSource source, SourceEntry entry,
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
            RaiseVisibilityChanged();
        });
    }

    public string EntryName { get; }
    public string SizeText { get; }
    public string? Sha256 { get; }
    public RelayCommand RevealCommand { get; }

    public bool IsContentVisible => _visibility.ShowMedia || _revealedIndividually;
    public bool IsContentHidden => !IsContentVisible;

    /// <summary>Imagen completa, decodificada recién cuando se revela.</summary>
    public BitmapImage? Image
    {
        get
        {
            if (!IsContentVisible || _decodeFailed)
                return null;

            if (_image is null)
            {
                _image = InMemoryImageDecoder.TryDecode(_source, _entry);
                _decodeFailed = _image is null;
            }

            return _image;
        }
    }

    public bool DecodeFailed => IsContentVisible && _decodeFailed && Image is null;

    private void OnVisibilityChanged(object? sender, PropertyChangedEventArgs e) => RaiseVisibilityChanged();

    private void RaiseVisibilityChanged()
    {
        RaisePropertyChanged(nameof(IsContentVisible));
        RaisePropertyChanged(nameof(IsContentHidden));
        RaisePropertyChanged(nameof(Image));
        RaisePropertyChanged(nameof(DecodeFailed));
    }
}
