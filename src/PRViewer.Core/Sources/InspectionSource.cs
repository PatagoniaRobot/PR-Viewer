namespace PRViewer.Core.Sources;

/// <summary>
/// Fábrica de fuentes de inspección. Decide el tipo de fuente por contenido
/// (firma de bytes), nunca por extensión, coherente con la regla de detección
/// del resto del núcleo.
/// </summary>
public static class InspectionSource
{
    // Firmas ZIP: PK\x03\x04 (normal), PK\x05\x06 (archivo vacío), PK\x07\x08 (spanned).
    private static readonly byte[][] ZipSignatures =
    {
        new byte[] { 0x50, 0x4B, 0x03, 0x04 },
        new byte[] { 0x50, 0x4B, 0x05, 0x06 },
        new byte[] { 0x50, 0x4B, 0x07, 0x08 },
    };

    /// <summary>
    /// Abre una ruta en solo lectura: carpeta, ZIP (detectado por firma) o archivo suelto.
    /// </summary>
    public static IInspectionSource Open(string path)
    {
        if (Directory.Exists(path))
            return new FolderInspectionSource(path);

        if (!File.Exists(path))
            throw new FileNotFoundException($"No existe la ruta «{path}».", path);

        return IsZip(path)
            ? new ZipInspectionSource(path)
            : new SingleFileInspectionSource(path);
    }

    /// <summary>Detecta si un archivo es ZIP leyendo sus primeros bytes.</summary>
    public static bool IsZip(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[4];
        if (stream.Read(header) < 4)
            return false;

        foreach (var signature in ZipSignatures)
        {
            if (header.SequenceEqual(signature))
                return true;
        }

        return false;
    }
}
