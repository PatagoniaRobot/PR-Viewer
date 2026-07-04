using System.IO;
using System.Windows.Threading;
using PRViewer.App.Audio;
using PRViewer.App.Services;
using PRViewer.Core.Ingestion;
using PRViewer.Core.Sources;

namespace PRViewer.App.ViewModels.Previews;

/// <summary>
/// Preview de audio con el motor PR-Opus: reproduce notas de voz Ogg/Opus
/// desde una copia en memoria de la entrada (nunca se extrae a disco).
/// La reproducción arranca solo por acción explícita del perito — el sonido
/// revela contenido igual que una pantalla.
/// </summary>
public sealed class AudioPreviewViewModel : ViewModelBase, IDisposable
{
    private readonly IInspectionSource _source;
    private readonly SourceEntry _entry;
    private readonly OpusPlayerEngine _engine = new();
    private readonly DispatcherTimer _stateTimer;

    private string _statusText = "listo para reproducir";
    private double _volume = 100;

    public AudioPreviewViewModel(IInspectionSource source, SourceEntry entry, string? knownSha256)
    {
        _source = source;
        _entry = entry;

        EntryName = entry.Name;
        SizeText = FileKindResolver.FormatSize(entry.Size);
        ContentType = ContentTypes.FromFileName(entry.Name);
        Sha256 = knownSha256;

        PlayCommand = new RelayCommand(_ => Play());
        PauseCommand = new RelayCommand(_ => Pause());
        StopCommand = new RelayCommand(_ => StopPlayback());

        // El hilo de bombeo termina solo al llegar al final del audio;
        // este timer refresca el estado visible mientras suena.
        _stateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _stateTimer.Tick += (_, _) =>
        {
            RaisePlaybackStateChanged();
            if (!_engine.IsPlaying)
            {
                _stateTimer.Stop();
                StatusText = "reproducción finalizada";
            }
        };
    }

    public string EntryName { get; }
    public string SizeText { get; }
    public string ContentType { get; }
    public string? Sha256 { get; }

    public RelayCommand PlayCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand StopCommand { get; }

    public bool IsPlaying => _engine.IsPlaying && !_engine.IsPaused;
    public bool IsPaused => _engine.IsPaused;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Volumen 0–100 para el slider.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value))
                _engine.SetVolume((float)(value / 100.0));
        }
    }

    private void Play()
    {
        Result result;
        if (_engine.IsPaused)
        {
            result = _engine.Resume();
            StatusText = result.IsSuccess ? "reproduciendo…" : result.Error;
        }
        else if (_engine.IsPlaying)
        {
            return;
        }
        else
        {
            // Copia fresca en memoria en cada reproducción: el decodificador
            // consume el stream y así el audio puede volver a escucharse.
            var buffer = new MemoryStream();
            using (var input = _source.OpenRead(_entry))
                input.CopyTo(buffer);
            buffer.Position = 0;

            result = _engine.Load(buffer);
            if (result.IsSuccess)
            {
                _engine.SetVolume((float)(_volume / 100.0));
                result = _engine.Play();
            }

            StatusText = result.IsSuccess ? "reproduciendo…" : result.Error;
        }

        if (result.IsSuccess)
            _stateTimer.Start();
        RaisePlaybackStateChanged();
    }

    private void Pause()
    {
        var result = _engine.Pause();
        if (result.IsSuccess)
            StatusText = "en pausa";
        RaisePlaybackStateChanged();
    }

    private void StopPlayback()
    {
        _engine.Stop();
        _stateTimer.Stop();
        StatusText = "detenido";
        RaisePlaybackStateChanged();
    }

    private void RaisePlaybackStateChanged()
    {
        RaisePropertyChanged(nameof(IsPlaying));
        RaisePropertyChanged(nameof(IsPaused));
    }

    public void Dispose()
    {
        _stateTimer.Stop();
        _engine.Dispose();
    }
}
