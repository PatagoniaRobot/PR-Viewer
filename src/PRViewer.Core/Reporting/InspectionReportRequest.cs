using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Reporting;

/// <summary>
/// Pedido de generación del informe técnico de inspección de un paquete.
///
/// El informe es material NUEVO generado por PR-Viewer: nunca toca el paquete
/// inspeccionado y solo escribe en el directorio de destino elegido por el
/// perito. Por decisión de diseño no incluye el contenido de los mensajes:
/// contiene exclusivamente metadatos, estadísticas y hashes (la transcripción
/// del contenido es responsabilidad de la aplicación consumidora).
/// </summary>
public sealed class InspectionReportRequest
{
    /// <summary>Identificación del paquete tal como fue recibido.</summary>
    public required PackageIdentity Package { get; init; }

    /// <summary>Paquete normalizado por la ingesta (uno o más hilos de conversación).</summary>
    public required IngestedPackage Conversation { get; init; }

    /// <summary>
    /// Fuente abierta del paquete, para inventariar y hashear las entradas
    /// no cubiertas por la ingesta. Solo se lee; el llamador conserva la propiedad.
    /// </summary>
    public required IInspectionSource Source { get; init; }

    /// <summary>Directorio de destino elegido por el perito. Debe existir.</summary>
    public required string DestinationDirectory { get; init; }

    /// <summary>Datos del caso para el encabezado; null u vacío omite la sección.</summary>
    public ReportCaseInfo? CaseInfo { get; init; }

    /// <summary>Genera la versión HTML autocontenida (imprimible a PDF desde el navegador).</summary>
    public bool GenerateHtml { get; init; } = true;

    /// <summary>Genera la versión de texto plano (estilo acta, RFC 3227).</summary>
    public bool GenerateTxt { get; init; } = true;
}

/// <summary>Rutas de los informes generados (null si el formato no fue pedido).</summary>
public sealed record InspectionReportResult(string? HtmlPath, string? TxtPath);
