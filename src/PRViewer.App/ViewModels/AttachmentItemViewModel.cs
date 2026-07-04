using System.ComponentModel;
using System.Windows.Media.Imaging;
using PRViewer.App.Services;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.App.ViewModels;

/// <summary>
/// Elemento de la galería multimedia: adjunto referenciado por la conversación,
/// con miniatura en memoria (solo imágenes) sujeta al estado de visibilidad.
/// </summary>
public sealed class AttachmentItemViewModel : ViewModelBase
{
    private const int ThumbnailWidth = 160;

    private readonly IInspectionSource _source;
    private readonly SourceEntry? _entry;
    private readonly MediaVisibilityState _visibility;
    private bool _revealedIndividually;
    private BitmapImage? _thumbnail;
    private bool _decodeFailed;

    public AttachmentItemViewModel(IInspectionSource source, AttachmentInfo attachment,
        SourceEntry? entry, MediaVisibilityState visibility)
    {
        _source = source;
        _entry = entry;
        _visibility = visibility;
        _visibility.PropertyChanged += OnVisibilityChanged;

        Attachment = attachment;
        Kind = FileKindResolver.Resolve(attachment.Name);
        Glyph = FileKindResolver.Glyph(Kind);
        SizeText = FileKindResolver.FormatSize(attachment.Size);
        RevealCommand = new RelayCommand(_ =>
        {
            _revealedIndividually = true;
            RaiseVisibilityChanged();
        });
    }

    public AttachmentInfo Attachment { get; }
    public FileKind Kind { get; }
    public string Glyph { get; }
    public string SizeText { get; }
    public RelayCommand RevealCommand { get; }

    public string Name => Attachment.Name;
    public bool IsPresent => Attachment.IsPresent;
    public bool IsMissing => !Attachment.IsPresent;
    public string? Sha256 => Attachment.Sha256;
    public SourceEntry? Entry => _entry;

    /// <summary>Solo las imágenes presentes tienen miniatura; el resto muestra su glifo.</summary>
    public bool CanHaveThumbnail => Kind == FileKind.Image && IsPresent;

    public bool IsThumbnailVisible => CanHaveThumbnail && !_decodeFailed
        && (_visibility.ShowMedia || _revealedIndividually);

    /// <summary>Placeholder «oculto»: habría miniatura pero la visibilidad está apagada.</summary>
    public bool IsThumbnailHidden => CanHaveThumbnail && !_decodeFailed
        && !(_visibility.ShowMedia || _revealedIndividually);

    public bool ShowGlyph => !IsThumbnailVisible && !IsThumbnailHidden;

    public BitmapImage? Thumbnail
    {
        get
        {
            if (!IsThumbnailVisible || _entry is null)
                return null;

            if (_thumbnail is null)
            {
                _thumbnail = InMemoryImageDecoder.TryDecode(_source, _entry, ThumbnailWidth);
                if (_thumbnail is null)
                {
                    _decodeFailed = true;
                    RaiseVisibilityChanged();
                }
            }

            return _thumbnail;
        }
    }

    private void OnVisibilityChanged(object? sender, PropertyChangedEventArgs e) => RaiseVisibilityChanged();

    private void RaiseVisibilityChanged()
    {
        RaisePropertyChanged(nameof(IsThumbnailVisible));
        RaisePropertyChanged(nameof(IsThumbnailHidden));
        RaisePropertyChanged(nameof(ShowGlyph));
        RaisePropertyChanged(nameof(Thumbnail));
    }
}
