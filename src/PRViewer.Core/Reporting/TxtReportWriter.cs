using System.Text;

namespace PRViewer.Core.Reporting;

/// <summary>
/// Escribe la versión de texto plano del informe, con encabezado estilo
/// RFC 3227 (cadena de custodia), coherente con los reportes TXT de la
/// familia PRImager. Mismo contenido que la versión HTML: metadatos,
/// estadísticas y hashes — nunca el contenido de los mensajes.
/// </summary>
internal static class TxtReportWriter
{
    private const string Rule = "================================================================================";
    private const string ThinRule = "--------------------------------------------------------------------------------";

    public static void Write(string outputPath, ReportData data)
    {
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var w = new StreamWriter(stream, Encoding.UTF8);

        w.WriteLine(Rule);
        w.WriteLine("          PR-VIEWER — INFORME TÉCNICO DE INSPECCIÓN DE PAQUETE (RFC 3227)");
        w.WriteLine(Rule);

        if (data.Request.CaseInfo is { HasAnyValue: true } info)
        {
            Field(w, "NÚMERO DE CASO", info.CaseNumber);
            Field(w, "CARÁTULA", info.CaseName);
            Field(w, "JUZGADO", info.CourtName);
            Field(w, "FISCALÍA", info.ProsecutorOffice);
            Field(w, "Nº ACTUACIÓN", info.CaseFileNumber);
            Field(w, "PERITO", info.ExaminerName);
            Field(w, "LEGAJO/MATRÍCULA", info.ExaminerBadge);
            Field(w, "DIVISIÓN", info.ExaminerDivision);
            Field(w, "CARGO", info.ExaminerRole);
            Field(w, "RECIBIDO DE", info.ReceivedFrom);
            Field(w, "FECHA DE RECEPCIÓN", info.ReceivedDate?.ToString("yyyy-MM-dd"));
            Field(w, "Nº ACTA DE RECEPCIÓN", info.ReceivedActNumber);
            w.WriteLine(ThinRule);
        }

        Field(w, "FECHA DE GENERACIÓN", $"{data.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        Field(w, "GENERADO POR", $"{InspectionReportGenerator.GeneratorVersion} — Patagonia Robot");
        w.WriteLine(Rule);
        w.WriteLine();

        // ── Identificación del paquete ──
        w.WriteLine("### IDENTIFICACIÓN DEL PAQUETE ###");
        w.WriteLine();
        Field(w, "  Nombre", data.Package.DisplayName);
        Field(w, "  Ruta en el equipo", data.Package.FullPath);
        Field(w, "  Tipo de contenedor", ReportData.KindText(data.Package.Kind));
        Field(w, "  Tamaño", ReportData.FormatSize(data.Package.SizeBytes));
        Field(w, "  Última modificación", data.Package.LastModifiedUtc is { } m ? $"{m:yyyy-MM-dd HH:mm:ss} UTC" : null);
        Field(w, "  Plataforma detectada", data.Conversation.Platform.ToString());
        Field(w, "  SHA-256 del paquete", data.Package.Sha256
            ?? "(carpeta: sin hash único; ver inventario por entrada)");
        w.WriteLine();

        // ── Resumen de la conversación ──
        w.WriteLine("### RESUMEN DE LA CONVERSACIÓN ###");
        w.WriteLine();
        Field(w, "  Mensajes totales", data.Conversation.MessageCount.ToString("N0"));
        Field(w, "  Mensajes de sistema", data.SystemMessageCount.ToString("N0"));
        Field(w, "  Participantes", data.Conversation.Participants.Count.ToString("N0"));
        Field(w, "  Listado", string.Join("; ", data.Conversation.Participants));
        Field(w, "  Rango temporal", data.Conversation.DateRange.HasValue
            ? $"{ReportData.FormatDate(data.Conversation.DateRange.First)} -> {ReportData.FormatDate(data.Conversation.DateRange.Last)}"
            : "sin fechas parseables");
        Field(w, "  Adjuntos referenciados", data.Conversation.Attachments.Count.ToString("N0"));
        Field(w, "  Adjuntos presentes", data.PresentAttachments.Count.ToString("N0"));
        Field(w, "  Adjuntos AUSENTES", data.MissingAttachments.Count.ToString("N0"));
        w.WriteLine();

        // ── Inventario de adjuntos ──
        w.WriteLine($"### INVENTARIO DE ADJUNTOS ({data.Conversation.Attachments.Count:N0}) ###");
        w.WriteLine();
        if (data.Conversation.Attachments.Count == 0)
        {
            w.WriteLine("  La conversación no referencia adjuntos.");
        }
        else
        {
            var index = 1;
            foreach (var a in data.Conversation.Attachments)
            {
                w.WriteLine($"  Adjunto #{index++}");
                Field(w, "    Nombre", a.Name);
                Field(w, "    Tipo estimado", a.ContentType);
                Field(w, "    Tamaño", a.IsPresent ? ReportData.FormatSize(a.Size) : null);
                Field(w, "    Presente", a.IsPresent ? "Sí" : "NO — AUSENTE DEL PAQUETE");
                Field(w, "    SHA-256", a.Sha256);
                w.WriteLine();
            }
        }

        // ── Otras entradas ──
        w.WriteLine($"### OTRAS ENTRADAS DEL PAQUETE ({data.UnreferencedEntries.Count:N0}) ###");
        w.WriteLine("  (no referenciadas como adjuntos por la conversación; incluye el archivo del chat)");
        w.WriteLine();
        foreach (var (entry, sha256) in data.UnreferencedEntries)
        {
            Field(w, "  Ruta", entry.Path);
            Field(w, "    Tamaño", ReportData.FormatSize(entry.Size));
            Field(w, "    SHA-256", sha256);
            w.WriteLine();
        }

        // ── Anomalías ──
        w.WriteLine("### ANOMALÍAS DETECTADAS ###");
        w.WriteLine();
        var anyAnomaly = data.MissingAttachments.Count > 0
                         || data.FailedTimestampCount > 0
                         || data.TemporalBacktrackCount > 0;
        if (!anyAnomaly)
        {
            w.WriteLine("  Sin anomalías: adjuntos completos, timestamps parseados, sin retrocesos temporales.");
        }
        else
        {
            if (data.MissingAttachments.Count > 0)
            {
                w.WriteLine($"  [!] Adjuntos referenciados pero AUSENTES del paquete ({data.MissingAttachments.Count:N0}) — export incompleto:");
                foreach (var a in data.MissingAttachments)
                    w.WriteLine($"      - {a.Name} ({a.ContentType})");
            }
            if (data.FailedTimestampCount > 0)
                w.WriteLine($"  [!] Mensajes con fecha/hora no parseable: {data.FailedTimestampCount:N0}");
            if (data.TemporalBacktrackCount > 0)
                w.WriteLine($"  [!] Retrocesos temporales entre mensajes consecutivos: {data.TemporalBacktrackCount:N0} (posible cambio de huso horario o ambigüedad día/mes)");
        }
        w.WriteLine();

        // ── Metodología ──
        w.WriteLine("### METODOLOGÍA Y TRAZABILIDAD ###");
        w.WriteLine();
        w.WriteLine("  * Inspección con PR-Viewer, visor forense de solo lectura: apertura");
        w.WriteLine("    exclusivamente con permisos de lectura, en streaming, sin extracción a");
        w.WriteLine("    disco ni modificación de ningún byte del material.");
        w.WriteLine("  * Detección de plataforma por análisis de contenido, nunca por extensión.");
        w.WriteLine("  * Hashes SHA-256 calculados en streaming sobre las entradas del paquete.");
        w.WriteLine("  * Este informe NO incluye el contenido de los mensajes ni material");
        w.WriteLine("    multimedia: contiene exclusivamente metadatos, estadísticas y hashes.");
        w.WriteLine("  * Este informe es material nuevo generado por PR-Viewer; no forma parte");
        w.WriteLine("    del paquete inspeccionado y su generación no lo altera.");
        w.WriteLine("  * ADVERTENCIA: el formato de exportación de la plataforma de origen no");
        w.WriteLine("    incluye firmas criptográficas de integridad. Este informe documenta el");
        w.WriteLine("    estado del paquete tal como fue recibido; no certifica la autenticidad");
        w.WriteLine("    de origen del contenido.");
        w.WriteLine();
        w.WriteLine(Rule);
        w.WriteLine("FIN DEL INFORME");
        w.WriteLine(Rule);
    }

    private static void Field(StreamWriter w, string label, string? value)
    {
        var display = string.IsNullOrWhiteSpace(value) ? "—" : value;
        w.WriteLine($"{label,-24}: {display}");
    }
}
