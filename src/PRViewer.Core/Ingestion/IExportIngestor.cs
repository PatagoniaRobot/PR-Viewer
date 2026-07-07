using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Ingestion;

/// <summary>
/// Ingestor de una plataforma concreta. Replica el patrón registry + implementaciones
/// por plataforma (modelo ForensicImageReaderRegistry de PR-Analyzer): nuevas plataformas
/// se enchufan sin tocar el núcleo.
/// </summary>
public interface IExportIngestor
{
    /// <summary>Plataforma que este ingestor sabe interpretar.</summary>
    Platform Platform { get; }

    /// <summary>
    /// Determina, inspeccionando el contenido (nunca extensiones),
    /// si la fuente corresponde a esta plataforma.
    /// </summary>
    bool CanIngest(IInspectionSource source);

    /// <summary>
    /// Normaliza el contenido de la fuente a la abstracción común (paquete con
    /// uno o más hilos). Solo lectura: no modifica ni extrae el material.
    /// </summary>
    IngestedPackage Ingest(IInspectionSource source);
}
