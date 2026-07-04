using System.IO;
using System.Windows.Media.Imaging;
using PRViewer.Core.Sources;

namespace PRViewer.App.Services;

/// <summary>
/// Decodifica imágenes del material inspeccionado ÍNTEGRAMENTE EN MEMORIA.
///
/// INVARIANTE DE SOLO LECTURA: el stream de la fuente se copia a un
/// MemoryStream y se decodifica ahí; jamás se extrae a disco, ni siquiera
/// a un archivo temporal. Si el contenido no es una imagen decodificable,
/// devuelve null (la UI muestra ícono por tipo en su lugar).
/// </summary>
public static class InMemoryImageDecoder
{
    /// <summary>Decodifica la entrada como imagen, opcionalmente limitada a un ancho de miniatura.</summary>
    public static BitmapImage? TryDecode(IInspectionSource source, SourceEntry entry, int? decodePixelWidth = null)
    {
        try
        {
            using var input = source.OpenRead(entry);
            var buffer = new MemoryStream();
            input.CopyTo(buffer);
            buffer.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            // OnLoad: la imagen queda completa en memoria y el stream puede liberarse.
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth is { } width)
                image.DecodePixelWidth = width;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze(); // inmutable y usable desde cualquier hilo
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException)
        {
            // No es una imagen decodificable: no es un error del visor.
            return null;
        }
    }
}
