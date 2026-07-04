namespace PRViewer.Core.Sources;

/// <summary>
/// Fuente de inspección sobre un archivo suelto (p. ej. un _chat.txt sin ZIP).
/// </summary>
public sealed class SingleFileInspectionSource : IInspectionSource
{
    private readonly string _filePath;

    public string DisplayName { get; }

    public IReadOnlyList<SourceEntry> Entries { get; }

    public SingleFileInspectionSource(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        DisplayName = Path.GetFileName(_filePath);
        Entries = new[] { new SourceEntry(DisplayName, new FileInfo(_filePath).Length) };
    }

    public Stream OpenRead(SourceEntry entry)
    {
        if (!string.Equals(entry.Path, Entries[0].Path, StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException($"La entrada «{entry.Path}» no corresponde a esta fuente.", entry.Path);

        return new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public void Dispose()
    {
        // Sin recursos persistentes.
    }
}
