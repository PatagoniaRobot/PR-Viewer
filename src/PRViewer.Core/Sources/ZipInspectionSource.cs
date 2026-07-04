using System.IO.Compression;

namespace PRViewer.Core.Sources;

/// <summary>
/// Fuente de inspección sobre un contenedor ZIP.
/// El archivo se abre con FileAccess.Read y el archivo ZIP en ZipArchiveMode.Read:
/// no existe ruta de código que pueda escribir sobre el material.
/// </summary>
public sealed class ZipInspectionSource : IInspectionSource
{
    private readonly FileStream _fileStream;
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entriesByPath;

    public string DisplayName { get; }

    public IReadOnlyList<SourceEntry> Entries { get; }

    /// <param name="zipPath">Ruta al archivo ZIP. Se abre en solo lectura.</param>
    public ZipInspectionSource(string zipPath)
    {
        // FileShare.Read: otros procesos pueden leer, nadie escribe mientras inspeccionamos.
        _fileStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _archive = new ZipArchive(_fileStream, ZipArchiveMode.Read, leaveOpen: false);

        DisplayName = Path.GetFileName(zipPath);

        _entriesByPath = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<SourceEntry>();
        foreach (var entry in _archive.Entries)
        {
            // Las entradas de directorio terminan en «/» y no llevan contenido.
            if (entry.FullName.EndsWith('/'))
                continue;

            var normalizedPath = entry.FullName.Replace('\\', '/');
            _entriesByPath[normalizedPath] = entry;
            entries.Add(new SourceEntry(normalizedPath, entry.Length));
        }

        Entries = entries;
    }

    public Stream OpenRead(SourceEntry entry)
    {
        if (!_entriesByPath.TryGetValue(entry.Path, out var zipEntry))
            throw new FileNotFoundException($"La entrada «{entry.Path}» no existe en el ZIP.", entry.Path);

        // ZipArchiveMode.Read garantiza streams de solo lectura, descomprimidos al vuelo.
        return zipEntry.Open();
    }

    public void Dispose() => _archive.Dispose();
}
