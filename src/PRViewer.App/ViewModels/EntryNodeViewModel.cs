using PRViewer.App.Services;
using PRViewer.Core.Sources;

namespace PRViewer.App.ViewModels;

/// <summary>
/// Nodo del árbol de entradas del paquete: carpeta (con hijos) o archivo (con SourceEntry).
/// </summary>
public sealed class EntryNodeViewModel
{
    private EntryNodeViewModel(string name, SourceEntry? entry, IReadOnlyList<EntryNodeViewModel> children)
    {
        Name = name;
        Entry = entry;
        Children = children;
        Glyph = entry is null ? "📁" : FileKindResolver.Glyph(FileKindResolver.Resolve(name));
        SizeText = entry is null ? string.Empty : FileKindResolver.FormatSize(entry.Size);
    }

    public string Name { get; }
    public SourceEntry? Entry { get; }
    public IReadOnlyList<EntryNodeViewModel> Children { get; }
    public string Glyph { get; }
    public string SizeText { get; }
    public bool IsFolder => Entry is null;

    /// <summary>Construye el árbol a partir del listado plano de entradas (rutas con «/»).</summary>
    public static IReadOnlyList<EntryNodeViewModel> BuildTree(IReadOnlyList<SourceEntry> entries)
    {
        var root = new FolderBuilder();

        foreach (var entry in entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
        {
            var segments = entry.Path.Split('/');
            var current = root;
            for (var i = 0; i < segments.Length - 1; i++)
                current = current.GetOrAddFolder(segments[i]);
            current.Files.Add(new EntryNodeViewModel(segments[^1], entry, Array.Empty<EntryNodeViewModel>()));
        }

        return root.Build();
    }

    /// <summary>Acumulador mutable intermedio; el árbol expuesto es inmutable.</summary>
    private sealed class FolderBuilder
    {
        private readonly Dictionary<string, FolderBuilder> _folders = new(StringComparer.OrdinalIgnoreCase);
        public List<EntryNodeViewModel> Files { get; } = new();

        public FolderBuilder GetOrAddFolder(string name)
        {
            if (!_folders.TryGetValue(name, out var folder))
            {
                folder = new FolderBuilder();
                _folders[name] = folder;
            }

            return folder;
        }

        public IReadOnlyList<EntryNodeViewModel> Build()
        {
            var nodes = new List<EntryNodeViewModel>();
            foreach (var (name, folder) in _folders.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                nodes.Add(new EntryNodeViewModel(name, entry: null, folder.Build()));
            nodes.AddRange(Files);
            return nodes;
        }
    }
}
