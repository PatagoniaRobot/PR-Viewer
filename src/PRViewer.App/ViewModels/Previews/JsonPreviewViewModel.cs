using System.IO;
using System.Text.Json;
using PRViewer.Core.Sources;

namespace PRViewer.App.ViewModels.Previews;

/// <summary>
/// Preview de JSON formateado y navegable como árbol (pedido explícito del
/// documento maestro, pensando en los exports Meta/Telegram).
/// </summary>
public sealed class JsonPreviewViewModel : ViewModelBase
{
    private const long MaxJsonBytes = 50_000_000; // tope defensivo

    public JsonPreviewViewModel(IInspectionSource source, SourceEntry entry)
    {
        EntryName = entry.Name;
        SizeText = Services.FileKindResolver.FormatSize(entry.Size);

        if (entry.Size > MaxJsonBytes)
        {
            ErrorText = "El archivo supera el tope del preview JSON (50 MB).";
            RootNodes = Array.Empty<JsonNodeViewModel>();
            return;
        }

        try
        {
            using var stream = source.OpenRead(entry);
            using var document = JsonDocument.Parse(stream);
            RootNodes = new[] { JsonNodeViewModel.FromElement("(raíz)", document.RootElement) };
        }
        catch (JsonException ex)
        {
            ErrorText = $"El contenido no es JSON válido: {ex.Message}";
            RootNodes = Array.Empty<JsonNodeViewModel>();
        }
    }

    public string EntryName { get; }
    public string SizeText { get; }
    public string? ErrorText { get; }
    public IReadOnlyList<JsonNodeViewModel> RootNodes { get; }

    public bool HasError => ErrorText is not null;
}

/// <summary>Nodo del árbol JSON. Se materializa completo al parsear (JsonDocument ya está en memoria).</summary>
public sealed class JsonNodeViewModel
{
    private JsonNodeViewModel(string name, string valueText, IReadOnlyList<JsonNodeViewModel> children)
    {
        Name = name;
        ValueText = valueText;
        Children = children;
    }

    public string Name { get; }
    public string ValueText { get; }
    public IReadOnlyList<JsonNodeViewModel> Children { get; }
    public bool HasChildren => Children.Count > 0;

    public static JsonNodeViewModel FromElement(string name, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var objectChildren = element.EnumerateObject()
                    .Select(p => FromElement(p.Name, p.Value))
                    .ToList();
                return new JsonNodeViewModel(name, $"{{…}} ({objectChildren.Count} propiedades)", objectChildren);

            case JsonValueKind.Array:
                var index = 0;
                var arrayChildren = element.EnumerateArray()
                    .Select(item => FromElement($"[{index++}]", item))
                    .ToList();
                return new JsonNodeViewModel(name, $"[…] ({arrayChildren.Count} elementos)", arrayChildren);

            case JsonValueKind.String:
                return new JsonNodeViewModel(name, $"\"{element.GetString()}\"", Array.Empty<JsonNodeViewModel>());

            default:
                return new JsonNodeViewModel(name, element.GetRawText(), Array.Empty<JsonNodeViewModel>());
        }
    }
}
