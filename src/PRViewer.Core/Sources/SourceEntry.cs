namespace PRViewer.Core.Sources;

/// <summary>
/// Entrada (archivo) dentro de una fuente de inspección.
/// </summary>
/// <param name="Path">Ruta relativa dentro del contenedor, con «/» como separador.</param>
/// <param name="Size">Tamaño en bytes.</param>
public sealed record SourceEntry(string Path, long Size)
{
    /// <summary>Nombre de archivo sin la ruta.</summary>
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
}
