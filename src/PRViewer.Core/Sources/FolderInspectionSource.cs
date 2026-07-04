namespace PRViewer.Core.Sources;

/// <summary>
/// Fuente de inspección sobre una carpeta del sistema de archivos.
/// Cada archivo se abre bajo demanda con FileAccess.Read.
/// </summary>
public sealed class FolderInspectionSource : IInspectionSource
{
    private readonly string _rootPath;

    public string DisplayName { get; }

    public IReadOnlyList<SourceEntry> Entries { get; }

    /// <param name="folderPath">Carpeta raíz a inspeccionar (recursiva).</param>
    public FolderInspectionSource(string folderPath)
    {
        _rootPath = Path.GetFullPath(folderPath);
        DisplayName = Path.GetFileName(_rootPath.TrimEnd(Path.DirectorySeparatorChar));

        var entries = new List<SourceEntry>();
        foreach (var file in Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(_rootPath, file).Replace('\\', '/');
            entries.Add(new SourceEntry(relativePath, new FileInfo(file).Length));
        }

        Entries = entries;
    }

    public Stream OpenRead(SourceEntry entry)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, entry.Path));

        // Defensa contra rutas que escapen de la carpeta raíz (p. ej. «../»).
        if (!fullPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"La entrada «{entry.Path}» apunta fuera de la carpeta inspeccionada.");

        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public void Dispose()
    {
        // Sin recursos persistentes: los streams se abren y cierran por entrada.
    }
}
