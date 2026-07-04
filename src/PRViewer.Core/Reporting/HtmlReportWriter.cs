using System.Globalization;
using System.Net;
using System.Text;
using PRViewer.Core.Model;

namespace PRViewer.Core.Reporting;

/// <summary>
/// Escribe la versión HTML del informe: autocontenida, sin JavaScript, con el
/// tema oscuro de PRImager y hoja de impresión para que el perito la lleve a
/// PDF desde el navegador. Nunca incluye contenido de mensajes.
/// </summary>
internal static class HtmlReportWriter
{
    public static void Write(string outputPath, ReportData data)
    {
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var w = new StreamWriter(stream, Encoding.UTF8);

        WriteHeader(w, data);
        WriteTitle(w, data);

        var section = 1;
        if (data.Request.CaseInfo is { HasAnyValue: true } caseInfo)
            WriteCaseSection(w, section++, caseInfo);

        WritePackageSection(w, section++, data);
        WriteConversationSection(w, section++, data);
        WriteAttachmentsSection(w, section++, data);
        WriteUnreferencedSection(w, section++, data);
        WriteAnomaliesSection(w, section++, data);
        WriteMethodologySection(w, section, data);
        WriteFooter(w, data);
    }

    private static void WriteHeader(StreamWriter w, ReportData data)
    {
        w.WriteLine("<!DOCTYPE html>");
        w.WriteLine("<html lang='es'>");
        w.WriteLine("<head>");
        w.WriteLine("<meta charset='UTF-8'>");
        w.WriteLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        w.WriteLine($"<title>PR-Viewer — Informe de Inspección — {Esc(data.Package.DisplayName)}</title>");
        w.WriteLine("<style>");
        w.WriteLine("""
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body { font-family: Consolas, 'Courier New', monospace; background: #0f172a; color: #e2e8f0; padding: 20px; line-height: 1.6; }
            .container { max-width: 1100px; margin: 0 auto; }
            h1 { color: #00D4AA; border-bottom: 2px solid #00D4AA; padding-bottom: 8px; margin: 30px 0 16px; font-size: 1.4em; }
            .card { background: #1a1a2e; border: 1px solid #334155; border-radius: 8px; padding: 16px; margin-bottom: 16px; }
            .card-header { color: #00D4AA; font-weight: bold; font-size: 1.05em; margin-bottom: 10px; }
            .info-grid { display: grid; grid-template-columns: 190px 1fr; gap: 4px 12px; }
            .info-label { color: #94a3b8; font-weight: bold; }
            .info-value { color: #e2e8f0; word-break: break-all; }
            .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; margin-bottom: 16px; }
            .stat-card { background: #252545; border: 1px solid #334155; border-radius: 8px; padding: 14px; text-align: center; }
            .stat-label { color: #00D4AA; font-size: 0.8em; font-weight: bold; }
            .stat-value { color: #10B981; font-size: 1.6em; font-weight: bold; margin-top: 4px; }
            .stat-value.danger { color: #ef4444; }
            .stat-value.warning { color: #f59e0b; }
            .table-wrap { overflow-x: auto; margin-bottom: 16px; }
            table { width: 100%; border-collapse: collapse; font-size: 0.85em; }
            th { background: #1a1a2e; color: #00D4AA; font-weight: bold; padding: 9px 8px; text-align: left; border: 1px solid #334155; white-space: nowrap; }
            td { background: #0f172a; color: #e2e8f0; padding: 7px 8px; border: 1px solid #334155; vertical-align: top; }
            tr:nth-child(even) td { background: #131b2e; }
            .hash { font-family: Consolas, monospace; font-size: 0.9em; color: #10B981; word-break: break-all; }
            .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 0.8em; font-weight: bold; }
            .badge-ok { background: #064e3b; color: #10B981; }
            .badge-missing { background: #7f1d1d; color: #ef4444; }
            .warn-box { color: #f59e0b; padding: 8px 12px; background: #1a1a2e; border-left: 3px solid #f59e0b; margin: 8px 0; font-size: 0.9em; }
            .ok-box { color: #10B981; padding: 8px 12px; background: #1a1a2e; border-left: 3px solid #10B981; margin: 8px 0; font-size: 0.9em; }
            .method-box { background: #0a1628; border: 2px solid #334155; border-radius: 8px; padding: 18px; margin: 10px 0; }
            .method-item { padding: 5px 0; border-bottom: 1px dotted #334155; }
            .method-item:last-child { border-bottom: none; }
            .report-header { text-align: center; padding: 26px 0; border-bottom: 2px solid #334155; margin-bottom: 24px; }
            .report-header .logo { color: #00D4AA; font-size: 1.9em; font-weight: bold; }
            .report-header .subtitle { color: #94a3b8; font-size: 1.05em; margin-top: 5px; }
            .report-header .brand { color: #475569; font-size: 0.85em; margin-top: 8px; }
            .footer { text-align: center; color: #475569; padding: 26px 0; border-top: 1px solid #334155; margin-top: 36px; font-size: 0.85em; }
            @media print {
                body { background: #fff; color: #000; }
                .card, .stat-card, th, .method-box { background: #f8f8f8; color: #000; border-color: #ccc; }
                td { background: #fff; color: #000; border-color: #ccc; }
                .stat-value, .stat-label, h1, .card-header, .report-header .logo, .hash { color: #000; }
                .badge { border: 1px solid #999; }
                h1, .card { page-break-inside: avoid; }
            }
            """);
        w.WriteLine("</style>");
        w.WriteLine("</head>");
        w.WriteLine("<body>");
        w.WriteLine("<div class='container'>");
    }

    private static void WriteTitle(StreamWriter w, ReportData data)
    {
        w.WriteLine("<div class='report-header'>");
        w.WriteLine("<div class='logo'>PR-Viewer</div>");
        w.WriteLine("<div class='subtitle'>Informe Técnico de Inspección de Paquete de Exportación</div>");
        w.WriteLine("<div class='brand'>Visor forense de solo lectura — Patagonia Robot</div>");
        w.WriteLine($"<div class='brand'>{Esc(InspectionReportGenerator.GeneratorVersion)} • {data.GeneratedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} (local)</div>");
        w.WriteLine("</div>");
    }

    private static void WriteCaseSection(StreamWriter w, int number, ReportCaseInfo info)
    {
        w.WriteLine($"<h1>{number}. Datos del Caso</h1>");

        w.WriteLine("<div class='card'>");
        w.WriteLine("<div class='card-header'>Caso Judicial</div>");
        w.WriteLine("<div class='info-grid'>");
        Row(w, "Nº de Caso", info.CaseNumber);
        Row(w, "Carátula", info.CaseName);
        Row(w, "Juzgado", info.CourtName);
        Row(w, "Fiscalía", info.ProsecutorOffice);
        Row(w, "Nº Actuación", info.CaseFileNumber);
        w.WriteLine("</div></div>");

        w.WriteLine("<div class='card'>");
        w.WriteLine("<div class='card-header'>Perito / Examinador</div>");
        w.WriteLine("<div class='info-grid'>");
        Row(w, "Nombre", info.ExaminerName);
        Row(w, "Legajo/Matrícula", info.ExaminerBadge);
        Row(w, "División", info.ExaminerDivision);
        Row(w, "Cargo", info.ExaminerRole);
        w.WriteLine("</div></div>");

        w.WriteLine("<div class='card'>");
        w.WriteLine("<div class='card-header'>Recepción del Material</div>");
        w.WriteLine("<div class='info-grid'>");
        Row(w, "Recibido de", info.ReceivedFrom);
        Row(w, "Fecha de Recepción", info.ReceivedDate?.ToString("yyyy-MM-dd") ?? "—");
        Row(w, "Nº Acta de Recepción", info.ReceivedActNumber);
        w.WriteLine("</div></div>");
    }

    private static void WritePackageSection(StreamWriter w, int number, ReportData data)
    {
        var package = data.Package;

        w.WriteLine($"<h1>{number}. Identificación del Paquete</h1>");
        w.WriteLine("<div class='card'>");
        w.WriteLine("<div class='info-grid'>");
        Row(w, "Nombre", package.DisplayName);
        Row(w, "Ruta en el equipo", package.FullPath);
        Row(w, "Tipo de contenedor", ReportData.KindText(package.Kind));
        Row(w, "Tamaño", ReportData.FormatSize(package.SizeBytes));
        Row(w, "Última modificación (UTC)", ReportData.FormatDate(package.LastModifiedUtc));
        Row(w, "Plataforma detectada", data.Conversation.Platform.ToString());
        w.WriteLine("</div>");

        if (package.Sha256 is { } hash)
        {
            w.WriteLine("<div style='margin-top:10px;'><span class='info-label'>SHA-256 del paquete:</span></div>");
            w.WriteLine($"<div class='hash'>{Esc(hash)}</div>");
        }
        else
        {
            w.WriteLine("<div class='warn-box'>El paquete es una carpeta: no existe un hash único del contenedor. La integridad se documenta entrada por entrada en las secciones siguientes.</div>");
        }

        w.WriteLine("</div>");
    }

    private static void WriteConversationSection(StreamWriter w, int number, ReportData data)
    {
        var c = data.Conversation;

        w.WriteLine($"<h1>{number}. Resumen de la Conversación</h1>");

        w.WriteLine("<div class='stats-grid'>");
        Stat(w, "Mensajes", c.MessageCount.ToString("N0"), "");
        Stat(w, "De sistema", data.SystemMessageCount.ToString("N0"), "");
        Stat(w, "Participantes", c.Participants.Count.ToString("N0"), "");
        Stat(w, "Adjuntos referenciados", c.Attachments.Count.ToString("N0"), "");
        Stat(w, "Adjuntos presentes", data.PresentAttachments.Count.ToString("N0"), "");
        Stat(w, "Adjuntos AUSENTES", data.MissingAttachments.Count.ToString("N0"),
            data.MissingAttachments.Count > 0 ? "danger" : "");
        w.WriteLine("</div>");

        w.WriteLine("<div class='card'>");
        w.WriteLine("<div class='info-grid'>");
        Row(w, "Rango temporal", c.DateRange.HasValue
            ? $"{ReportData.FormatDate(c.DateRange.First)} → {ReportData.FormatDate(c.DateRange.Last)}"
            : "sin fechas parseables");
        Row(w, "Participantes", string.Join("; ", c.Participants));
        w.WriteLine("</div>");
        w.WriteLine("</div>");
    }

    private static void WriteAttachmentsSection(StreamWriter w, int number, ReportData data)
    {
        var attachments = data.Conversation.Attachments;

        w.WriteLine($"<h1>{number}. Inventario de Adjuntos</h1>");

        if (attachments.Count == 0)
        {
            w.WriteLine("<div class='card'>La conversación no referencia adjuntos.</div>");
            return;
        }

        w.WriteLine("<div class='table-wrap'>");
        w.WriteLine("<table>");
        w.WriteLine("<thead><tr><th>#</th><th>Nombre</th><th>Tipo</th><th>Tamaño</th><th>Presente</th><th>SHA-256</th></tr></thead>");
        w.WriteLine("<tbody>");

        var index = 1;
        foreach (var a in attachments)
        {
            var badge = a.IsPresent
                ? "<span class='badge badge-ok'>Sí</span>"
                : "<span class='badge badge-missing'>AUSENTE</span>";

            w.WriteLine("<tr>");
            w.WriteLine($"<td>{index++}</td>");
            w.WriteLine($"<td>{Esc(a.Name)}</td>");
            w.WriteLine($"<td>{Esc(a.ContentType)}</td>");
            w.WriteLine($"<td>{ReportData.FormatSize(a.Size)}</td>");
            w.WriteLine($"<td>{badge}</td>");
            w.WriteLine($"<td class='hash'>{Esc(a.Sha256 ?? "—")}</td>");
            w.WriteLine("</tr>");
        }

        w.WriteLine("</tbody></table>");
        w.WriteLine("</div>");
    }

    private static void WriteUnreferencedSection(StreamWriter w, int number, ReportData data)
    {
        w.WriteLine($"<h1>{number}. Otras Entradas del Paquete</h1>");
        w.WriteLine("<p style='color:#94a3b8;margin-bottom:10px;'>Entradas no referenciadas como adjuntos por la conversación (incluye el archivo del chat). Se hashean igualmente para el inventario completo.</p>");

        if (data.UnreferencedEntries.Count == 0)
        {
            w.WriteLine("<div class='card'>Todas las entradas del paquete están referenciadas por la conversación.</div>");
            return;
        }

        w.WriteLine("<div class='table-wrap'>");
        w.WriteLine("<table>");
        w.WriteLine("<thead><tr><th>#</th><th>Ruta</th><th>Tamaño</th><th>SHA-256</th></tr></thead>");
        w.WriteLine("<tbody>");

        var index = 1;
        foreach (var (entry, sha256) in data.UnreferencedEntries)
        {
            w.WriteLine("<tr>");
            w.WriteLine($"<td>{index++}</td>");
            w.WriteLine($"<td>{Esc(entry.Path)}</td>");
            w.WriteLine($"<td>{ReportData.FormatSize(entry.Size)}</td>");
            w.WriteLine($"<td class='hash'>{Esc(sha256)}</td>");
            w.WriteLine("</tr>");
        }

        w.WriteLine("</tbody></table>");
        w.WriteLine("</div>");
    }

    private static void WriteAnomaliesSection(StreamWriter w, int number, ReportData data)
    {
        w.WriteLine($"<h1>{number}. Anomalías Detectadas</h1>");

        var anyAnomaly = data.MissingAttachments.Count > 0
                         || data.FailedTimestampCount > 0
                         || data.TemporalBacktrackCount > 0;

        if (!anyAnomaly)
        {
            w.WriteLine("<div class='ok-box'>Sin anomalías: todos los adjuntos referenciados están presentes, todos los timestamps se parsearon y no se detectaron retrocesos temporales.</div>");
            return;
        }

        if (data.MissingAttachments.Count > 0)
        {
            w.WriteLine($"<div class='warn-box'>Adjuntos referenciados en la conversación pero AUSENTES del paquete ({data.MissingAttachments.Count}) — señal de export incompleto:</div>");
            w.WriteLine("<div class='table-wrap'><table>");
            w.WriteLine("<thead><tr><th>Nombre</th><th>Tipo estimado</th></tr></thead><tbody>");
            foreach (var a in data.MissingAttachments)
                w.WriteLine($"<tr><td>{Esc(a.Name)}</td><td>{Esc(a.ContentType)}</td></tr>");
            w.WriteLine("</tbody></table></div>");
        }

        if (data.FailedTimestampCount > 0)
            w.WriteLine($"<div class='warn-box'>Mensajes cuya fecha/hora no pudo parsearse: {data.FailedTimestampCount:N0}.</div>");

        if (data.TemporalBacktrackCount > 0)
            w.WriteLine($"<div class='warn-box'>Retrocesos temporales entre mensajes consecutivos: {data.TemporalBacktrackCount:N0}. Puede deberse a cambios de huso horario del dispositivo o a ambigüedad día/mes del formato de fecha.</div>");
    }

    private static void WriteMethodologySection(StreamWriter w, int number, ReportData data)
    {
        w.WriteLine($"<h1>{number}. Metodología y Trazabilidad</h1>");
        w.WriteLine("<div class='method-box'>");
        w.WriteLine("<div class='method-item'>✓ La inspección se realizó con PR-Viewer, visor forense de solo lectura: el paquete se abrió exclusivamente con permisos de lectura, en streaming, sin extracción a disco ni modificación de ningún byte del material.</div>");
        w.WriteLine("<div class='method-item'>✓ La plataforma de origen se detectó por análisis de contenido, nunca por extensión de archivo.</div>");
        w.WriteLine("<div class='method-item'>✓ Los hashes SHA-256 se calcularon en streaming directamente sobre las entradas del paquete.</div>");
        w.WriteLine("<div class='method-item'>✓ Este informe NO incluye el contenido de los mensajes ni material multimedia: contiene exclusivamente metadatos, estadísticas y hashes. La transcripción del contenido corresponde al acta de la aplicación consumidora.</div>");
        w.WriteLine("<div class='method-item'>✓ Este informe es material nuevo generado por PR-Viewer; no forma parte del paquete inspeccionado y su generación no lo altera.</div>");
        w.WriteLine("<div class='method-item'>⚠ El formato de exportación de la plataforma de origen no incluye firmas criptográficas de integridad. Este informe documenta el estado del paquete tal como fue recibido; no certifica la autenticidad de origen del contenido.</div>");
        w.WriteLine($"<div class='method-item' style='margin-top:12px;'><strong>Generado (UTC):</strong> {data.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC</div>");

        if (data.Package.Sha256 is { } hash)
        {
            w.WriteLine("<div class='method-item'><strong>SHA-256 del paquete inspeccionado:</strong></div>");
            w.WriteLine($"<div class='hash'>{Esc(hash)}</div>");
        }

        w.WriteLine("</div>");
    }

    private static void WriteFooter(StreamWriter w, ReportData data)
    {
        w.WriteLine("<div class='footer'>");
        w.WriteLine($"<p><strong>{Esc(InspectionReportGenerator.GeneratorVersion)} — Patagonia Robot</strong></p>");
        w.WriteLine("<p>Visor forense de solo lectura — Apache 2.0 — github.com/PatagoniaRobot/PR-Viewer</p>");
        w.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "<p>Informe generado: {0:yyyy-MM-dd HH:mm:ss} (local) / {1:yyyy-MM-dd HH:mm:ss} (UTC)</p>",
            data.GeneratedAtUtc.ToLocalTime(), data.GeneratedAtUtc));
        w.WriteLine("</div>");
        w.WriteLine("</div>");
        w.WriteLine("</body>");
        w.WriteLine("</html>");
    }

    private static void Row(StreamWriter w, string label, string value)
    {
        var display = string.IsNullOrWhiteSpace(value) ? "—" : value;
        w.WriteLine($"<div class='info-label'>{Esc(label)}:</div><div class='info-value'>{Esc(display)}</div>");
    }

    private static void Stat(StreamWriter w, string label, string value, string cssClass)
    {
        var cls = string.IsNullOrEmpty(cssClass) ? "" : $" {cssClass}";
        w.WriteLine($"<div class='stat-card'><div class='stat-label'>{Esc(label)}</div><div class='stat-value{cls}'>{Esc(value)}</div></div>");
    }

    private static string Esc(string? text) => WebUtility.HtmlEncode(text ?? "");
}
