namespace PRViewer.Core.Ingestion;

/// <summary>
/// Estimación de tipo de contenido (MIME) por extensión, solo con fines
/// informativos para el visor. La detección de plataforma nunca usa extensiones;
/// esto es únicamente etiquetado para mostrar.
/// </summary>
public static class ContentTypes
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".mp4"] = "video/mp4",
        [".3gp"] = "video/3gpp",
        [".mov"] = "video/quicktime",
        [".opus"] = "audio/ogg",
        [".ogg"] = "audio/ogg",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".vcf"] = "text/vcard",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    public static string FromFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return Map.TryGetValue(extension, out var contentType)
            ? contentType
            : "application/octet-stream";
    }
}
