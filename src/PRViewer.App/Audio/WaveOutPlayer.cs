using System.Runtime.InteropServices;

namespace PRViewer.App.Audio;

/// <summary>Callback que llena un buffer PCM; devuelve la cantidad de bytes escritos.</summary>
public delegate Result<int> ReadPcmDelegate(byte[] buffer, int offset, int count);

/// <summary>
/// Reproductor waveOut nativo (motor PR-Opus): triple buffer de ~100 ms con
/// hilo de bombeo propio. Corrección respecto del motor original: tanto los
/// datos PCM como las WAVEHDR se alojan en memoria NATIVA (AllocHGlobal),
/// porque el driver conserva el puntero a la cabecera mientras el buffer
/// está en cola y el GC podría mover un objeto administrado.
/// </summary>
public sealed class WaveOutPlayer : IDisposable
{
    private const int BuffersCount = 3;
    private const int BufferSize = 9600; // 100 ms a 48 kHz mono PCM 16-bit

    private IntPtr _hWaveOut = IntPtr.Zero;
    private readonly AutoResetEvent _bufferFinishedEvent = new(false);
    private readonly WaveBuffer[] _buffers;
    private Thread? _playbackThread;

    private volatile bool _isPlaying;
    private volatile bool _isPaused;
    private volatile bool _stopRequested;
    private volatile bool _endOfStreamReached;
    private string _lastError = string.Empty;
    private float _volume = 1.0f;

    /// <summary>Buffer nativo: PCM y WAVEHDR en memoria no administrada.</summary>
    private sealed class WaveBuffer : IDisposable
    {
        private static readonly int HeaderSize = Marshal.SizeOf<Win32WaveOut.WAVEHDR>();
        private static readonly int FlagsOffset = (int)Marshal.OffsetOf<Win32WaveOut.WAVEHDR>(nameof(Win32WaveOut.WAVEHDR.dwFlags));
        private static readonly int LengthOffset = (int)Marshal.OffsetOf<Win32WaveOut.WAVEHDR>(nameof(Win32WaveOut.WAVEHDR.dwBufferLength));

        public IntPtr HeaderPtr { get; private set; }
        public IntPtr DataPtr { get; private set; }

        public static uint NativeHeaderSize => (uint)HeaderSize;

        public WaveBuffer(int size)
        {
            DataPtr = Marshal.AllocHGlobal(size);
            HeaderPtr = Marshal.AllocHGlobal(HeaderSize);

            // Silencio inicial para evitar chasquidos.
            var zeros = new byte[size];
            Marshal.Copy(zeros, 0, DataPtr, size);

            var header = new Win32WaveOut.WAVEHDR
            {
                lpData = DataPtr,
                dwBufferLength = (uint)size,
            };
            Marshal.StructureToPtr(header, HeaderPtr, fDeleteOld: false);
        }

        /// <summary>Flags actuales, leídos de la memoria nativa que actualiza el driver.</summary>
        public uint Flags => (uint)Marshal.ReadInt32(HeaderPtr, FlagsOffset);

        public void SetDataLength(int bytes) => Marshal.WriteInt32(HeaderPtr, LengthOffset, bytes);

        public void CopyPcm(byte[] source, int bytes)
        {
            Marshal.Copy(source, 0, DataPtr, bytes);
            SetDataLength(bytes);
        }

        public void Dispose()
        {
            if (HeaderPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(HeaderPtr);
                HeaderPtr = IntPtr.Zero;
            }

            if (DataPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(DataPtr);
                DataPtr = IntPtr.Zero;
            }
        }
    }

    public bool IsPlaying => _isPlaying;
    public bool IsPaused => _isPaused;

    public WaveOutPlayer()
    {
        _buffers = new WaveBuffer[BuffersCount];
        for (var i = 0; i < BuffersCount; i++)
            _buffers[i] = new WaveBuffer(BufferSize);
    }

    public Result Open(uint sampleRate, ushort channels)
    {
        if (_hWaveOut != IntPtr.Zero)
            return Result.Failure("El dispositivo ya está abierto.");

        var format = Win32WaveOut.WAVEFORMATEX.CreatePcm(sampleRate, channels, 16);
        var eventHandle = _bufferFinishedEvent.SafeWaitHandle.DangerousGetHandle();

        var mmResult = Win32WaveOut.waveOutOpen(
            out _hWaveOut,
            (IntPtr)Win32WaveOut.WAVE_MAPPER,
            ref format,
            eventHandle,
            IntPtr.Zero,
            Win32WaveOut.CALLBACK_EVENT);

        if (mmResult != Win32WaveOut.MMSYSERR_NOERROR)
        {
            _hWaveOut = IntPtr.Zero;
            return Result.Failure($"No se pudo abrir el dispositivo de audio: {Win32WaveOut.GetErrorDescription(mmResult)}");
        }

        SetVolume(_volume);
        return Result.Success();
    }

    public Result Play(ReadPcmDelegate callback)
    {
        if (_hWaveOut == IntPtr.Zero)
            return Result.Failure("El dispositivo de audio no está abierto.");

        if (_isPlaying)
            return Result.Failure("Ya se está reproduciendo audio.");

        _isPlaying = true;
        _isPaused = false;
        _stopRequested = false;
        _endOfStreamReached = false;
        _lastError = string.Empty;

        _playbackThread = new Thread(() => PlaybackLoop(callback))
        {
            IsBackground = true,
            Name = "PR-Opus Playback Thread",
        };

        var threadStartResult = Result.Try(() => _playbackThread.Start());
        if (threadStartResult.IsFailure)
        {
            _isPlaying = false;
            return Result.Failure($"Error al iniciar el hilo de reproducción: {threadStartResult.Error}");
        }

        return Result.Success();
    }

    public Result Pause()
    {
        if (_hWaveOut == IntPtr.Zero) return Result.Failure("El reproductor no está inicializado.");
        if (!_isPlaying) return Result.Failure("No hay audio en reproducción para pausar.");
        if (_isPaused) return Result.Success();

        var res = Win32WaveOut.waveOutPause(_hWaveOut);
        if (res != Win32WaveOut.MMSYSERR_NOERROR)
            return Result.Failure($"No se pudo pausar el audio: {Win32WaveOut.GetErrorDescription(res)}");

        _isPaused = true;
        return Result.Success();
    }

    public Result Resume()
    {
        if (_hWaveOut == IntPtr.Zero) return Result.Failure("El reproductor no está inicializado.");
        if (!_isPlaying) return Result.Failure("No hay audio cargado para reanudar.");
        if (!_isPaused) return Result.Success();

        var res = Win32WaveOut.waveOutRestart(_hWaveOut);
        if (res != Win32WaveOut.MMSYSERR_NOERROR)
            return Result.Failure($"No se pudo reanudar el audio: {Win32WaveOut.GetErrorDescription(res)}");

        _isPaused = false;
        return Result.Success();
    }

    public Result Stop()
    {
        _stopRequested = true;
        _bufferFinishedEvent.Set(); // despierta el hilo si estaba esperando

        if (_playbackThread is { IsAlive: true })
            _playbackThread.Join(1000);

        if (_hWaveOut != IntPtr.Zero)
        {
            Win32WaveOut.waveOutReset(_hWaveOut);
            UnprepareAllBuffers();
        }

        _isPlaying = false;
        _isPaused = false;
        return string.IsNullOrEmpty(_lastError) ? Result.Success() : Result.Failure(_lastError);
    }

    public Result SetVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0.0f, 1.0f);
        if (_hWaveOut == IntPtr.Zero)
            return Result.Success(); // se aplica al abrir

        var volWord = (ushort)(_volume * ushort.MaxValue);
        var dwVolume = ((uint)volWord << 16) | volWord;

        var res = Win32WaveOut.waveOutSetVolume(_hWaveOut, dwVolume);
        if (res != Win32WaveOut.MMSYSERR_NOERROR)
            return Result.Failure($"No se pudo ajustar el volumen: {Win32WaveOut.GetErrorDescription(res)}");

        return Result.Success();
    }

    private void PlaybackLoop(ReadPcmDelegate callback)
    {
        var tempBuffer = new byte[BufferSize];

        // 1. Llenado inicial de la cola.
        foreach (var buffer in _buffers)
        {
            if (_stopRequested) break;
            if (!TryFillAndQueue(buffer, callback, tempBuffer)) break;
        }

        // 2. Bucle de bombeo.
        while (!_stopRequested)
        {
            _bufferFinishedEvent.WaitOne(50);
            if (_stopRequested) break;

            var anyActive = false;

            foreach (var buffer in _buffers)
            {
                var flags = buffer.Flags;

                if ((flags & Win32WaveOut.WHDR_PREPARED) != 0 && (flags & Win32WaveOut.WHDR_DONE) != 0)
                {
                    Win32WaveOut.waveOutUnprepareHeader(_hWaveOut, buffer.HeaderPtr, WaveBuffer.NativeHeaderSize);

                    if (!_endOfStreamReached && TryFillAndQueue(buffer, callback, tempBuffer))
                        anyActive = true;
                }
                else if ((flags & Win32WaveOut.WHDR_INQUEUE) != 0)
                {
                    anyActive = true;
                }
            }

            if (!anyActive && _endOfStreamReached)
                break; // terminaron todos los buffers
        }

        _isPlaying = false;
        _isPaused = false;
    }

    /// <summary>Llena un buffer desde el callback y lo encola; false si hubo EOF o error.</summary>
    private bool TryFillAndQueue(WaveBuffer buffer, ReadPcmDelegate callback, byte[] tempBuffer)
    {
        var readResult = callback(tempBuffer, 0, BufferSize);
        if (readResult.IsFailure)
        {
            _lastError = readResult.Error;
            _stopRequested = true;
            return false;
        }

        var bytesRead = readResult.Value;
        if (bytesRead <= 0)
        {
            _endOfStreamReached = true;
            return false;
        }

        buffer.CopyPcm(tempBuffer, bytesRead);

        var res = Win32WaveOut.waveOutPrepareHeader(_hWaveOut, buffer.HeaderPtr, WaveBuffer.NativeHeaderSize);
        if (res != Win32WaveOut.MMSYSERR_NOERROR)
        {
            _lastError = $"Error preparando buffer: {Win32WaveOut.GetErrorDescription(res)}";
            _stopRequested = true;
            return false;
        }

        res = Win32WaveOut.waveOutWrite(_hWaveOut, buffer.HeaderPtr, WaveBuffer.NativeHeaderSize);
        if (res != Win32WaveOut.MMSYSERR_NOERROR)
        {
            _lastError = $"Error escribiendo buffer: {Win32WaveOut.GetErrorDescription(res)}";
            _stopRequested = true;
            return false;
        }

        return true;
    }

    private void UnprepareAllBuffers()
    {
        if (_hWaveOut == IntPtr.Zero) return;

        foreach (var buffer in _buffers)
        {
            if ((buffer.Flags & Win32WaveOut.WHDR_PREPARED) != 0)
                Win32WaveOut.waveOutUnprepareHeader(_hWaveOut, buffer.HeaderPtr, WaveBuffer.NativeHeaderSize);
        }
    }

    public void Close()
    {
        Stop();

        if (_hWaveOut != IntPtr.Zero)
        {
            Win32WaveOut.waveOutClose(_hWaveOut);
            _hWaveOut = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Close();

        foreach (var buffer in _buffers)
            buffer.Dispose();

        _bufferFinishedEvent.Dispose();
    }
}
