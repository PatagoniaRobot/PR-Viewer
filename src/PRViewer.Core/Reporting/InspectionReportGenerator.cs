using System.Security.Cryptography;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Reporting;

/// <summary>
/// Generador del informe técnico de inspección (Capa 1, sin dependencias).
/// Produce HTML autocontenido y/o texto plano con metadatos, estadísticas y
/// hashes del paquete — nunca el contenido de los mensajes.
///
/// Solo lectura sobre el material: la única escritura es el informe mismo,
/// en el destino elegido por el perito, sin sobrescribir jamás un archivo existente.
/// </summary>
public static class InspectionReportGenerator
{
    /// <summary>Versión declarada en el pie de los informes.</summary>
    public const string GeneratorVersion = "PR-Viewer 1.1";

    public static InspectionReportResult Generate(InspectionReportRequest request)
    {
        if (!request.GenerateHtml && !request.GenerateTxt)
            throw new ArgumentException("Debe pedirse al menos un formato de informe.", nameof(request));

        if (!Directory.Exists(request.DestinationDirectory))
            throw new DirectoryNotFoundException($"No existe el directorio de destino «{request.DestinationDirectory}».");

        var data = ReportData.Compute(request);

        string? htmlPath = null;
        string? txtPath = null;

        if (request.GenerateHtml)
        {
            htmlPath = ReserveOutputPath(request, data, ".html");
            HtmlReportWriter.Write(htmlPath, data);
        }

        if (request.GenerateTxt)
        {
            txtPath = ReserveOutputPath(request, data, ".txt");
            TxtReportWriter.Write(txtPath, data);
        }

        return new InspectionReportResult(htmlPath, txtPath);
    }

    /// <summary>
    /// Reserva un nombre de salida único: nunca se sobrescribe un archivo existente.
    /// FileMode.CreateNew garantiza la exclusividad incluso ante carreras.
    /// </summary>
    private static string ReserveOutputPath(InspectionReportRequest request, ReportData data, string extension)
    {
        var caseTag = request.CaseInfo is { } info && !string.IsNullOrWhiteSpace(info.CaseNumber)
            ? info.CaseNumber
            : Path.GetFileNameWithoutExtension(request.Package.DisplayName);
        var baseName = $"PRViewer_Informe_{Sanitize(caseTag)}_{data.GeneratedAtUtc.ToLocalTime():yyyyMMdd_HHmmss}";

        for (var attempt = 0; ; attempt++)
        {
            var suffix = attempt == 0 ? "" : $"_{attempt + 1}";
            var candidate = Path.Combine(request.DestinationDirectory, baseName + suffix + extension);
            try
            {
                // Se crea vacío para reclamar el nombre; el writer lo abre a continuación.
                using var _ = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate) && attempt < 1000)
            {
                // El nombre ya estaba tomado: se prueba el siguiente sufijo.
            }
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray();
        return new string(chars);
    }
}

/// <summary>
/// Datos derivados que consumen los dos writers: estadísticas de la conversación,
/// anomalías detectadas e inventario completo de entradas con sus hashes.
/// Se computa una sola vez por informe.
/// </summary>
internal sealed class ReportData
{
    public required InspectionReportRequest Request { get; init; }
    public required DateTime GeneratedAtUtc { get; init; }

    // Estadísticas de la conversación.
    public required int SystemMessageCount { get; init; }
    public required int FailedTimestampCount { get; init; }
    public required int TemporalBacktrackCount { get; init; }

    // Adjuntos.
    public required IReadOnlyList<AttachmentInfo> PresentAttachments { get; init; }
    public required IReadOnlyList<AttachmentInfo> MissingAttachments { get; init; }

    /// <summary>Entradas del paquete no referenciadas como adjuntos (incluye el propio chat), con su SHA-256.</summary>
    public required IReadOnlyList<(SourceEntry Entry, string Sha256)> UnreferencedEntries { get; init; }

    public IngestedConversation Conversation => Request.Conversation;
    public PackageIdentity Package => Request.Package;

    public static ReportData Compute(InspectionReportRequest request)
    {
        var conversation = request.Conversation;

        // Retrocesos temporales: pares consecutivos con ambos timestamps donde el orden se invierte.
        var backtracks = 0;
        DateTime? previous = null;
        foreach (var message in conversation.Messages)
        {
            if (message.Timestamp is { } current)
            {
                if (previous is { } prev && current < prev)
                    backtracks++;
                previous = current;
            }
        }

        // Entradas no referenciadas por la conversación (mismo criterio de cruce
        // por nombre que usa la ingesta). Se hashean acá, en streaming y solo lectura.
        var attachmentNames = new HashSet<string>(
            conversation.Attachments.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);

        var unreferenced = new List<(SourceEntry, string)>();
        foreach (var entry in request.Source.Entries)
        {
            if (attachmentNames.Contains(entry.Name))
                continue;

            using var stream = request.Source.OpenRead(entry);
            var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            unreferenced.Add((entry, sha256));
        }

        return new ReportData
        {
            Request = request,
            GeneratedAtUtc = DateTime.UtcNow,
            SystemMessageCount = conversation.Messages.Count(m => m.IsSystemMessage),
            FailedTimestampCount = conversation.Messages.Count(m => m.Timestamp is null),
            TemporalBacktrackCount = backtracks,
            PresentAttachments = conversation.Attachments.Where(a => a.IsPresent).ToList(),
            MissingAttachments = conversation.Attachments.Where(a => !a.IsPresent).ToList(),
            UnreferencedEntries = unreferenced,
        };
    }

    // ── Helpers de formato compartidos por los writers ──

    public static string FormatSize(long? bytes) => bytes switch
    {
        null => "—",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };

    public static string FormatDate(DateTime? value)
        => value?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";

    public static string KindText(PackageKind kind) => kind switch
    {
        PackageKind.Zip => "Archivo ZIP",
        PackageKind.Folder => "Carpeta",
        _ => "Archivo suelto",
    };
}
