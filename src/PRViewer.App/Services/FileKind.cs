using System.IO;

namespace PRViewer.App.Services;

/// <summary>Clasificación superficial por extensión, solo para elegir ícono y vista de preview.</summary>
public enum FileKind
{
    Image,
    Video,
    Audio,
    Pdf,
    Docx,
    Document,
    Text,
    Json,
    Other,
}

public static class FileKindResolver
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico" };
    private static readonly string[] VideoExtensions = { ".mp4", ".3gp", ".mov", ".avi", ".mkv", ".webm" };
    private static readonly string[] AudioExtensions = { ".opus", ".ogg", ".mp3", ".m4a", ".aac", ".wav", ".amr" };
    private static readonly string[] DocumentExtensions = { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt" };
    private static readonly string[] TextExtensions = { ".txt", ".csv", ".log", ".vcf", ".xml", ".html" };

    /// <summary>
    /// Clasifica por extensión. Es solo una pista para la UI (ícono, vista);
    /// el render real de imágenes valida por contenido: si no decodifica, cae al ícono.
    /// </summary>
    public static FileKind Resolve(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".json") return FileKind.Json;
        if (extension == ".pdf") return FileKind.Pdf;
        if (extension == ".docx") return FileKind.Docx;
        if (ImageExtensions.Contains(extension)) return FileKind.Image;
        if (VideoExtensions.Contains(extension)) return FileKind.Video;
        if (AudioExtensions.Contains(extension)) return FileKind.Audio;
        if (DocumentExtensions.Contains(extension)) return FileKind.Document;
        if (TextExtensions.Contains(extension)) return FileKind.Text;
        return FileKind.Other;
    }

    /// <summary>Glifo para mostrar junto al nombre (texto, sin assets binarios).</summary>
    public static string Glyph(FileKind kind) => kind switch
    {
        FileKind.Image => "🖼",
        FileKind.Video => "🎬",
        FileKind.Audio => "🎵",
        FileKind.Pdf => "📄",
        FileKind.Docx => "📃",
        FileKind.Document => "📄",
        FileKind.Text => "📝",
        FileKind.Json => "🧩",
        _ => "📦",
    };

    /// <summary>Tamaño legible (es-AR usa coma decimal; se respeta la cultura actual).</summary>
    public static string FormatSize(long? bytes)
    {
        if (bytes is not { } value)
            return "—";

        string[] units = { "bytes", "KB", "MB", "GB" };
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:N0} {units[unit]}" : $"{size:N1} {units[unit]}";
    }
}
