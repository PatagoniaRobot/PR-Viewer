using System.Security.Cryptography;
using System.Text;
using PRViewer.Core.Reporting;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Extraction;

/// <summary>
/// Extracción controlada de entradas del paquete (Enmienda Nº 1, E1.3.b).
///
/// Garantías que implementa este servicio:
///  - el paquete solo se lee (streaming); el destino nunca es el paquete ni,
///    para carpetas, la carpeta inspeccionada;
///  - jamás se sobrescribe un archivo existente en el destino;
///  - cada copia se verifica por SHA-256 contra los bytes leídos del paquete
///    (y contra el hash de la ingesta, si se conoce); discrepancia = la copia
///    se descarta y se deja constancia del error;
///  - toda extracción produce una constancia automática en el destino.
/// </summary>
public static class ControlledExtractionService
{
    public static ExtractionResult Extract(ExtractionRequest request)
    {
        if (request.Entries.Count == 0)
            throw new ArgumentException("No se indicaron entradas a extraer.", nameof(request));

        if (!Directory.Exists(request.DestinationDirectory))
            throw new DirectoryNotFoundException($"No existe el directorio de destino «{request.DestinationDirectory}».");

        ValidateDestination(request);

        var extractedAtUtc = DateTime.UtcNow;
        var records = request.Entries.Select(entry => ExtractOne(request, entry)).ToList();
        var manifestPath = WriteManifest(request, records, extractedAtUtc);

        return new ExtractionResult(records, manifestPath, extractedAtUtc);
    }

    /// <summary>
    /// El destino no puede caer dentro del material inspeccionado: escribir ahí
    /// contaminaría justamente lo que se jura no tocar (E1.3.b.2).
    /// </summary>
    private static void ValidateDestination(ExtractionRequest request)
    {
        if (request.Package.Kind != PackageKind.Folder)
            return;

        var inspectedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Package.FullPath));
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.DestinationDirectory));

        if (destination.Equals(inspectedRoot, StringComparison.OrdinalIgnoreCase) ||
            destination.StartsWith(inspectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "El destino no puede ser la carpeta inspeccionada ni estar dentro de ella: " +
                "la extracción jamás escribe sobre el material de origen.");
        }
    }

    private static ExtractedEntryRecord ExtractOne(ExtractionRequest request, SourceEntry entry)
    {
        string? targetPath = null;
        try
        {
            // El hash del origen se calcula sobre los mismos bytes que se copian:
            // es, por definición, el hash de la entrada dentro del paquete.
            using var sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            targetPath = ReserveTargetPath(request.DestinationDirectory, entry.Name);
            using (var sourceStream = request.Source.OpenRead(entry))
            using (var targetStream = new FileStream(targetPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sourceHash.AppendData(buffer, 0, read);
                    targetStream.Write(buffer, 0, read);
                }
            }

            var sourceSha256 = Convert.ToHexString(sourceHash.GetHashAndReset()).ToLowerInvariant();

            // Verificación 1: lo que quedó en disco coincide con lo leído del paquete.
            string copySha256;
            using (var copyStream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                copySha256 = Convert.ToHexString(SHA256.HashData(copyStream)).ToLowerInvariant();

            if (!copySha256.Equals(sourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(targetPath);
                return new ExtractedEntryRecord(entry.Path, entry.Size, sourceSha256, copySha256,
                    ExportedAs: null, Verified: false,
                    Error: "La copia escrita no coincide con los bytes leídos del paquete; se descartó.");
            }

            // Verificación 2: coincide con lo que la ingesta observó, si hay hash previo.
            if (request.KnownHashes is { } known
                && known.TryGetValue(entry.Name, out var ingestedSha256)
                && !ingestedSha256.Equals(sourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(targetPath);
                return new ExtractedEntryRecord(entry.Path, entry.Size, sourceSha256, copySha256,
                    ExportedAs: null, Verified: false,
                    Error: "El contenido actual de la entrada no coincide con el hash calculado en la ingesta; se descartó la copia.");
            }

            return new ExtractedEntryRecord(entry.Path, entry.Size, sourceSha256, copySha256,
                Path.GetFileName(targetPath), Verified: true, Error: null);
        }
        catch (Exception ex)
        {
            // Copia a medias: se descarta; el error queda en la constancia.
            if (targetPath is not null && File.Exists(targetPath))
            {
                try { File.Delete(targetPath); } catch (IOException) { /* best-effort */ }
            }
            return new ExtractedEntryRecord(entry.Path, entry.Size, SourceSha256: null,
                CopySha256: null, ExportedAs: null, Verified: false, Error: ex.Message);
        }
    }

    /// <summary>
    /// Reserva un nombre libre en el destino: nunca se sobrescribe (E1.3.b.3).
    /// FileMode.CreateNew garantiza exclusividad incluso ante carreras.
    /// </summary>
    private static string ReserveTargetPath(string destinationDirectory, string entryName)
    {
        var safeName = Sanitize(entryName);
        var baseName = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);

        for (var attempt = 0; ; attempt++)
        {
            var suffix = attempt == 0 ? "" : $"_{attempt + 1}";
            var candidate = Path.Combine(destinationDirectory, baseName + suffix + extension);
            try
            {
                using var _ = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate) && attempt < 10_000)
            {
                // Nombre tomado: se prueba el siguiente sufijo.
            }
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars);
        return result.Length == 0 ? "entrada" : result;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Constancia automática (E1.3.b.5)
    // ═══════════════════════════════════════════════════════════════

    private static string WriteManifest(ExtractionRequest request,
        IReadOnlyList<ExtractedEntryRecord> records, DateTime extractedAtUtc)
    {
        const string rule = "================================================================================";
        const string thinRule = "--------------------------------------------------------------------------------";

        var manifestPath = ReserveManifestPath(request.DestinationDirectory, extractedAtUtc);

        using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Write, FileShare.Read);
        using var w = new StreamWriter(stream, Encoding.UTF8);

        w.WriteLine(rule);
        w.WriteLine("        PR-VIEWER — CONSTANCIA DE EXTRACCIÓN CONTROLADA (Enmienda E1.3.b)");
        w.WriteLine(rule);
        w.WriteLine($"{"FECHA DE EXTRACCIÓN",-24}: {extractedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        w.WriteLine($"{"DESTINO",-24}: {request.DestinationDirectory}");
        w.WriteLine($"{"PAQUETE DE ORIGEN",-24}: {request.Package.DisplayName}");
        w.WriteLine($"{"RUTA DEL PAQUETE",-24}: {request.Package.FullPath}");
        w.WriteLine($"{"TIPO DE CONTENEDOR",-24}: {ReportData.KindText(request.Package.Kind)}");
        w.WriteLine($"{"SHA-256 DEL PAQUETE",-24}: {request.Package.Sha256 ?? "(carpeta: sin hash único; ver hash por entrada)"}");
        w.WriteLine($"{"GENERADO POR",-24}: {ToolInfo.NameAndVersion} — Patagonia Robot");
        w.WriteLine(rule);
        w.WriteLine();

        var index = 1;
        foreach (var record in records)
        {
            w.WriteLine($"Entrada #{index++}");
            w.WriteLine($"{"  Ruta en el paquete",-24}: {record.EntryPath}");
            w.WriteLine($"{"  Tamaño",-24}: {record.SizeBytes:N0} bytes");
            w.WriteLine($"{"  SHA-256 en el paquete",-24}: {record.SourceSha256 ?? "—"}");
            if (record.Verified)
            {
                w.WriteLine($"{"  Exportado como",-24}: {record.ExportedAs}");
                w.WriteLine($"{"  SHA-256 de la copia",-24}: {record.CopySha256}");
                w.WriteLine($"{"  Verificación",-24}: VERIFICADA — la copia coincide con la entrada del paquete");
            }
            else
            {
                w.WriteLine($"{"  Verificación",-24}: FALLIDA — {record.Error}");
            }
            w.WriteLine();
        }

        w.WriteLine(thinRule);
        var verified = records.Count(r => r.Verified);
        w.WriteLine($"Resultado: {verified} entrada(s) extraída(s) y verificada(s), {records.Count - verified} error(es).");
        w.WriteLine();
        w.WriteLine("  * La extracción se realizó con acceso de solo lectura sobre el paquete;");
        w.WriteLine("    el material de origen permanece intacto.");
        w.WriteLine("  * El SHA-256 de cada copia fue verificado contra el hash de la entrada");
        w.WriteLine("    dentro del paquete; toda discrepancia descarta la copia y se asienta.");
        w.WriteLine("  * Ningún archivo existente en el destino fue sobrescrito.");
        w.WriteLine(rule);
        w.WriteLine("FIN DE LA CONSTANCIA");
        w.WriteLine(rule);

        return manifestPath;
    }

    private static string ReserveManifestPath(string destinationDirectory, DateTime extractedAtUtc)
    {
        var baseName = $"PRViewer_Extraccion_{extractedAtUtc.ToLocalTime():yyyyMMdd_HHmmss}";
        for (var attempt = 0; ; attempt++)
        {
            var suffix = attempt == 0 ? "" : $"_{attempt + 1}";
            var candidate = Path.Combine(destinationDirectory, baseName + suffix + ".txt");
            try
            {
                using var _ = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate) && attempt < 1000)
            {
            }
        }
    }
}
