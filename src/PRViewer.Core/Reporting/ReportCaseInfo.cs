namespace PRViewer.Core.Reporting;

/// <summary>
/// Datos del caso judicial y del perito para el encabezado del informe.
/// Todos los campos son opcionales: el informe es del paquete, no del caso.
/// El vocabulario replica el de los reportes de PRImager para mantener
/// consistencia institucional entre las herramientas de la familia.
/// </summary>
public sealed class ReportCaseInfo
{
    // === Caso judicial ===
    public string CaseNumber { get; init; } = "";
    public string CaseName { get; init; } = "";
    public string CourtName { get; init; } = "";
    public string ProsecutorOffice { get; init; } = "";
    public string CaseFileNumber { get; init; } = "";

    // === Perito / examinador ===
    public string ExaminerName { get; init; } = "";
    public string ExaminerBadge { get; init; } = "";
    public string ExaminerDivision { get; init; } = "";
    public string ExaminerRole { get; init; } = "";

    // === Recepción del material ===
    public string ReceivedFrom { get; init; } = "";
    public DateTime? ReceivedDate { get; init; }
    public string ReceivedActNumber { get; init; } = "";

    /// <summary>Indica si hay al menos un dato cargado (si no, el informe omite la sección).</summary>
    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(CaseNumber) ||
        !string.IsNullOrWhiteSpace(CaseName) ||
        !string.IsNullOrWhiteSpace(CourtName) ||
        !string.IsNullOrWhiteSpace(ProsecutorOffice) ||
        !string.IsNullOrWhiteSpace(CaseFileNumber) ||
        !string.IsNullOrWhiteSpace(ExaminerName) ||
        !string.IsNullOrWhiteSpace(ExaminerBadge) ||
        !string.IsNullOrWhiteSpace(ExaminerDivision) ||
        !string.IsNullOrWhiteSpace(ExaminerRole) ||
        !string.IsNullOrWhiteSpace(ReceivedFrom) ||
        ReceivedDate.HasValue ||
        !string.IsNullOrWhiteSpace(ReceivedActNumber);
}
