using System.Runtime.InteropServices;

namespace PRViewer.App.Audio;

/// <summary>
/// Interop con waveOut de winmm.dll (API del sistema operativo, sin dependencias).
/// Las funciones que encolan cabeceras reciben IntPtr: las WAVEHDR viven en
/// memoria nativa porque el driver conserva el puntero mientras el buffer suena.
/// </summary>
public static class Win32WaveOut
{
    private const string Winmm = "winmm.dll";

    public const ushort WAVE_FORMAT_PCM = 1;
    public const uint CALLBACK_EVENT = 0x00050000;
    public const int WAVE_MAPPER = -1;

    // Flags de WAVEHDR
    public const uint WHDR_DONE = 0x00000001;
    public const uint WHDR_PREPARED = 0x00000002;
    public const uint WHDR_INQUEUE = 0x00000010;

    public const int MMSYSERR_NOERROR = 0;
    public const int MMSYSERR_ERROR = 1;
    public const int MMSYSERR_BADDEVICEID = 2;
    public const int MMSYSERR_NOTENABLED = 3;
    public const int MMSYSERR_ALLOCATED = 4;
    public const int MMSYSERR_INVALHANDLE = 5;
    public const int MMSYSERR_NODRIVER = 6;
    public const int MMSYSERR_NOMEM = 7;
    public const int WAVERR_STILLPLAYING = 33;
    public const int WAVERR_UNPREPARED = 34;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;

        public static WAVEFORMATEX CreatePcm(uint sampleRate, ushort channels, ushort bitsPerSample)
        {
            var blockAlign = (ushort)(channels * bitsPerSample / 8);
            return new WAVEFORMATEX
            {
                wFormatTag = WAVE_FORMAT_PCM,
                nChannels = channels,
                nSamplesPerSec = sampleRate,
                nAvgBytesPerSec = sampleRate * blockAlign,
                nBlockAlign = blockAlign,
                wBitsPerSample = bitsPerSample,
                cbSize = 0,
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport(Winmm)]
    public static extern int waveOutOpen(
        out IntPtr phwo,
        IntPtr uDeviceID,
        ref WAVEFORMATEX pwfx,
        IntPtr dwCallback,
        IntPtr dwInstance,
        uint fdwOpen);

    [DllImport(Winmm)]
    public static extern int waveOutClose(IntPtr hwo);

    [DllImport(Winmm)]
    public static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

    [DllImport(Winmm)]
    public static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

    [DllImport(Winmm)]
    public static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh);

    [DllImport(Winmm)]
    public static extern int waveOutPause(IntPtr hwo);

    [DllImport(Winmm)]
    public static extern int waveOutRestart(IntPtr hwo);

    [DllImport(Winmm)]
    public static extern int waveOutReset(IntPtr hwo);

    [DllImport(Winmm)]
    public static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);

    [DllImport(Winmm)]
    public static extern int waveOutGetVolume(IntPtr hwo, out uint dwVolume);

    public static string GetErrorDescription(int mmError) => mmError switch
    {
        MMSYSERR_NOERROR => "sin error",
        MMSYSERR_ERROR => "error no especificado",
        MMSYSERR_BADDEVICEID => "identificador de dispositivo fuera de rango",
        MMSYSERR_NOTENABLED => "el driver no pudo habilitarse",
        MMSYSERR_ALLOCATED => "el dispositivo ya está asignado",
        MMSYSERR_INVALHANDLE => "handle de dispositivo inválido",
        MMSYSERR_NODRIVER => "no hay driver de audio presente",
        MMSYSERR_NOMEM => "no se pudo asignar o bloquear memoria",
        WAVERR_STILLPLAYING => "los buffers de audio siguen reproduciéndose",
        WAVERR_UNPREPARED => "la cabecera del buffer no está preparada",
        _ => $"código de error desconocido: {mmError}",
    };
}
