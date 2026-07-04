using System.IO;
using Concentus;
using Concentus.Oggfile;

namespace PRViewer.App.Audio;

/// <summary>
/// Decodificador Ogg/Opus a PCM 16-bit (motor PR-Opus + Concentus).
///
/// Adaptación para PR-Viewer: recibe un Stream EN MEMORIA (copia de la entrada
/// del paquete) en lugar de una ruta de archivo. El material original nunca se
/// extrae a disco; el invariante de solo lectura queda intacto.
/// </summary>
public sealed class OpusAudioDecoder : IDisposable
{
    private Stream? _stream;
    private IOpusDecoder? _decoder;
    private OpusOggReadStream? _oggStream;

    private byte[] _pcmBuffer = Array.Empty<byte>();
    private int _pcmBufferOffset;
    private int _pcmBufferLength;

    public uint SampleRate { get; }
    public ushort Channels { get; }

    private OpusAudioDecoder(Stream stream, IOpusDecoder decoder, OpusOggReadStream oggStream, uint sampleRate, ushort channels)
    {
        _stream = stream;
        _decoder = decoder;
        _oggStream = oggStream;
        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>
    /// Abre un stream Ogg/Opus. El stream debe ser posicionable (MemoryStream);
    /// el decodificador toma posesión y lo libera en Dispose.
    /// </summary>
    public static Result<OpusAudioDecoder> Open(Stream stream)
    {
        if (!stream.CanSeek)
            return Result<OpusAudioDecoder>.Failure("El stream de audio debe ser posicionable (copia en memoria).");

        // 1. Cabecera OpusHead para canales reales. La decodificación se fuerza
        //    a 48000 Hz: es la frecuencia nativa de Opus y la de mayor calidad.
        var channels = ParseChannelsFromHeader(stream);
        const uint decodeSampleRate = 48000;

        try
        {
            stream.Position = 0;
            var decoder = OpusCodecFactory.CreateDecoder((int)decodeSampleRate, channels);
            var oggStream = new OpusOggReadStream(decoder, stream);
            return Result<OpusAudioDecoder>.Success(
                new OpusAudioDecoder(stream, decoder, oggStream, decodeSampleRate, channels));
        }
        catch (Exception ex)
        {
            return Result<OpusAudioDecoder>.Failure($"Error al abrir el flujo Opus: {ex.Message}");
        }
    }

    /// <summary>Lee PCM decodificado. Devuelve 0 al llegar al final del stream.</summary>
    public Result<int> Read(byte[] buffer, int offset, int count)
    {
        if (_oggStream is null)
            return Result<int>.Failure("El decodificador no está inicializado o ya fue liberado.");

        var totalBytesWritten = 0;

        while (totalBytesWritten < count)
        {
            var remainingInBuffer = _pcmBufferLength - _pcmBufferOffset;

            if (remainingInBuffer > 0)
            {
                var bytesToCopy = Math.Min(remainingInBuffer, count - totalBytesWritten);
                Buffer.BlockCopy(_pcmBuffer, _pcmBufferOffset, buffer, offset + totalBytesWritten, bytesToCopy);
                _pcmBufferOffset += bytesToCopy;
                totalBytesWritten += bytesToCopy;
            }
            else
            {
                // Siguiente paquete del contenedor Ogg.
                var hasNext = false;
                var hasNextResult = Result.Try(() => hasNext = _oggStream.HasNextPacket);
                if (hasNextResult.IsFailure)
                    return Result<int>.Failure($"Error al leer el contenedor Ogg: {hasNextResult.Error}");

                if (!hasNext)
                    break; // fin del stream

                short[]? packet = null;
                var decodeResult = Result.Try(() => packet = _oggStream.DecodeNextPacket());
                if (decodeResult.IsFailure)
                    return Result<int>.Failure($"Error decodificando paquete Opus: {decodeResult.Error}");

                // Con exports reales de WhatsApp, al agotarse los datos la librería
                // devuelve null indefinidamente sin apagar HasNextPacket: hay que
                // tratarlo como fin de stream o el bucle no termina nunca.
                if (packet is null)
                    break;

                if (packet.Length == 0)
                    continue;

                var packetBytes = packet.Length * 2; // short = 2 bytes
                if (_pcmBuffer.Length < packetBytes)
                    _pcmBuffer = new byte[packetBytes];

                Buffer.BlockCopy(packet, 0, _pcmBuffer, 0, packetBytes);
                _pcmBufferOffset = 0;
                _pcmBufferLength = packetBytes;
            }
        }

        return Result<int>.Success(totalBytesWritten);
    }

    /// <summary>
    /// Busca la firma «OpusHead» en los primeros bytes y lee la cantidad de
    /// canales. Fallback: mono, el formato típico de nota de voz de WhatsApp.
    /// </summary>
    private static ushort ParseChannelsFromHeader(Stream stream)
    {
        stream.Position = 0;
        var buffer = new byte[128];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);

        ReadOnlySpan<byte> signature = "OpusHead"u8;
        for (var i = 0; i + 16 <= bytesRead; i++)
        {
            if (buffer.AsSpan(i, signature.Length).SequenceEqual(signature))
            {
                var channels = (ushort)buffer[i + 9];
                return channels is >= 1 and <= 2 ? channels : (ushort)1;
            }
        }

        return 1;
    }

    public void Dispose()
    {
        _oggStream = null;
        _decoder?.Dispose();
        _decoder = null;
        _stream?.Dispose();
        _stream = null;
    }
}
