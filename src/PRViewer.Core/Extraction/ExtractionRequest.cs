using PRViewer.Core.Reporting;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Extraction;

/// <summary>
/// Pedido de extracción controlada (maestro, Enmienda Nº 1, E1.3.b).
/// La extracción es una derivación de evidencia auditada: copia entradas del
/// paquete a un destino elegido por el perito, verifica cada copia por SHA-256
/// contra la entrada dentro del paquete y deja constancia automática.
/// El material de origen jamás se modifica.
/// </summary>
public sealed class ExtractionRequest
{
    /// <summary>Identificación del paquete de origen (incluye su hash si es archivo).</summary>
    public required PackageIdentity Package { get; init; }

    /// <summary>Fuente abierta del paquete. Solo se lee; el llamador conserva la propiedad.</summary>
    public required IInspectionSource Source { get; init; }

    /// <summary>Entradas a extraer, elegidas explícitamente por el perito.</summary>
    public required IReadOnlyList<SourceEntry> Entries { get; init; }

    /// <summary>
    /// Directorio de destino elegido por el perito. Debe existir y no puede ser
    /// el propio paquete ni (para fuentes de carpeta) la carpeta inspeccionada.
    /// </summary>
    public required string DestinationDirectory { get; init; }

    /// <summary>
    /// Hashes ya calculados por la ingesta (nombre de entrada → SHA-256), para
    /// contrastar además contra lo que la ingesta observó. Opcional.
    /// </summary>
    public IReadOnlyDictionary<string, string>? KnownHashes { get; init; }
}

/// <summary>Constancia de una entrada extraída (o del intento fallido).</summary>
/// <param name="EntryPath">Ruta de la entrada dentro del paquete.</param>
/// <param name="SizeBytes">Tamaño declarado de la entrada.</param>
/// <param name="SourceSha256">SHA-256 de la entrada dentro del paquete, o null si no pudo leerse.</param>
/// <param name="CopySha256">SHA-256 de la copia escrita en el destino, o null si falló.</param>
/// <param name="ExportedAs">Nombre del archivo escrito en el destino, o null si falló.</param>
/// <param name="Verified">True solo si la copia se escribió y su hash coincide con el del origen.</param>
/// <param name="Error">Descripción del problema cuando la extracción no se pudo verificar.</param>
public sealed record ExtractedEntryRecord(
    string EntryPath,
    long SizeBytes,
    string? SourceSha256,
    string? CopySha256,
    string? ExportedAs,
    bool Verified,
    string? Error);

/// <summary>Resultado de la extracción: registros por entrada y ruta de la constancia.</summary>
public sealed record ExtractionResult(
    IReadOnlyList<ExtractedEntryRecord> Entries,
    string ManifestPath,
    DateTime ExtractedAtUtc)
{
    public int VerifiedCount => Entries.Count(e => e.Verified);
    public int ErrorCount => Entries.Count(e => !e.Verified);
}
