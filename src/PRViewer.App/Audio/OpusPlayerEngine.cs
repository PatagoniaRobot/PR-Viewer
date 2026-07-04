using System.IO;

namespace PRViewer.App.Audio;

/// <summary>
/// Fachada del motor PR-Opus: carga un stream Ogg/Opus en memoria y lo
/// reproduce por waveOut con pausa, reanudación, detención y volumen.
/// </summary>
public sealed class OpusPlayerEngine : IDisposable
{
    private OpusAudioDecoder? _decoder;
    private WaveOutPlayer? _player;
    private float _cachedVolume = 1.0f;

    public bool IsPlaying => _player?.IsPlaying ?? false;
    public bool IsPaused => _player?.IsPaused ?? false;

    /// <summary>Carga un stream en memoria. El motor toma posesión del stream.</summary>
    public Result Load(Stream stream)
    {
        if (IsPlaying)
        {
            var stopResult = Stop();
            if (stopResult.IsFailure)
                return stopResult;
        }

        CleanUpCurrentSession();

        var decoderResult = OpusAudioDecoder.Open(stream);
        if (decoderResult.IsFailure)
            return Result.Failure(decoderResult.Error);

        _decoder = decoderResult.Value;

        _player = new WaveOutPlayer();
        var openResult = _player.Open(_decoder.SampleRate, _decoder.Channels);
        if (openResult.IsFailure)
        {
            CleanUpCurrentSession();
            return Result.Failure(openResult.Error);
        }

        _player.SetVolume(_cachedVolume);
        return Result.Success();
    }

    public Result Play()
    {
        if (_decoder is null || _player is null)
            return Result.Failure("No hay ningún audio cargado para reproducir.");

        return _player.Play((buffer, offset, count) => _decoder.Read(buffer, offset, count));
    }

    public Result Pause()
        => _player?.Pause() ?? Result.Failure("El reproductor no está inicializado.");

    public Result Resume()
        => _player?.Resume() ?? Result.Failure("El reproductor no está inicializado.");

    public Result Stop()
        => _player?.Stop() ?? Result.Success();

    public Result SetVolume(float volume)
    {
        _cachedVolume = Math.Clamp(volume, 0.0f, 1.0f);
        return _player?.SetVolume(_cachedVolume) ?? Result.Success();
    }

    private void CleanUpCurrentSession()
    {
        _player?.Dispose();
        _player = null;
        _decoder?.Dispose();
        _decoder = null;
    }

    public void Dispose() => CleanUpCurrentSession();
}
