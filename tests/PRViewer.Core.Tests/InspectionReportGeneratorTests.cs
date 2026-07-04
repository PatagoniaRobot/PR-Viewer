using System.Security.Cryptography;
using PRViewer.Core.Ingestion.WhatsApp;
using PRViewer.Core.Reporting;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Tests;

public class InspectionReportGeneratorTests : IDisposable
{
    private readonly TestPackage _package = new();
    private readonly string _outputDir;

    public InspectionReportGeneratorTests()
    {
        _outputDir = Path.Combine(_package.RootPath, "informes");
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose() => _package.Dispose();

    // Texto elegido para poder afirmar que NO aparece en el informe.
    private const string SecretText = "contenido-confidencial-del-mensaje";

    private const string Chat =
        "[25/6/2024, 14:03:12] Ana García: " + SecretText + "\n" +
        "[25/6/2024, 14:04:00] Juan Pérez: ‎<attached: 00000012-PHOTO-2024-06-25.jpg>\n" +
        "[25/6/2024, 14:05:30] Ana García: ‎<attached: 00000013-VIDEO-2024-06-25.mp4>\n";

    private static readonly byte[] MediaBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

    private string CreateExportZip() => _package.CreateZip("export.zip", new Dictionary<string, byte[]>
    {
        ["_chat.txt"] = TestPackage.Utf8(Chat),
        ["00000012-PHOTO-2024-06-25.jpg"] = MediaBytes,
        // El video referenciado NO está: adjunto ausente esperado en el informe.
    });

    private InspectionReportRequest BuildRequest(string zipPath, IInspectionSource source,
        ReportCaseInfo? caseInfo = null, bool html = true, bool txt = true)
    {
        var fileInfo = new FileInfo(zipPath);
        using var stream = File.OpenRead(zipPath);
        var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        return new InspectionReportRequest
        {
            Package = new PackageIdentity(
                fileInfo.Name, zipPath, PackageKind.Zip,
                sha256, fileInfo.Length, fileInfo.LastWriteTimeUtc),
            Conversation = new WhatsAppTxtIngestor().Ingest(source),
            Source = source,
            DestinationDirectory = _outputDir,
            CaseInfo = caseInfo,
            GenerateHtml = html,
            GenerateTxt = txt,
        };
    }

    [Fact]
    public void GeneratesBothFormatsWithExpectedContent()
    {
        var zipPath = CreateExportZip();
        using var source = InspectionSource.Open(zipPath);
        var request = BuildRequest(zipPath, source);

        var result = InspectionReportGenerator.Generate(request);

        Assert.NotNull(result.HtmlPath);
        Assert.NotNull(result.TxtPath);

        foreach (var path in new[] { result.HtmlPath!, result.TxtPath! })
        {
            var content = File.ReadAllText(path);

            // Identificación y hashes.
            Assert.Contains(request.Package.Sha256!, content);
            Assert.Contains("00000012-PHOTO-2024-06-25.jpg", content);
            Assert.Contains("00000013-VIDEO-2024-06-25.mp4", content);
            Assert.Contains("AUSENTE", content);
            Assert.Contains("WhatsApp", content);

            // Hash del adjunto presente, calculado por la ingesta.
            var attachmentHash = Convert.ToHexString(SHA256.HashData(MediaBytes)).ToLowerInvariant();
            Assert.Contains(attachmentHash, content);

            // El chat aparece inventariado como entrada no referenciada, con su hash.
            Assert.Contains("_chat.txt", content);
            var chatHash = Convert.ToHexString(SHA256.HashData(TestPackage.Utf8(Chat))).ToLowerInvariant();
            Assert.Contains(chatHash, content);
        }
    }

    [Fact]
    public void ReportNeverContainsMessageText()
    {
        var zipPath = CreateExportZip();
        using var source = InspectionSource.Open(zipPath);

        var result = InspectionReportGenerator.Generate(BuildRequest(zipPath, source));

        // Decisión de privacidad: solo metadatos, estadísticas y hashes.
        Assert.DoesNotContain(SecretText, File.ReadAllText(result.HtmlPath!));
        Assert.DoesNotContain(SecretText, File.ReadAllText(result.TxtPath!));
    }

    [Fact]
    public void SourcePackageRemainsIntactAfterGeneration()
    {
        var zipPath = CreateExportZip();
        var hashBefore = HashFile(zipPath);
        var modifiedBefore = File.GetLastWriteTimeUtc(zipPath);

        using (var source = InspectionSource.Open(zipPath))
        {
            InspectionReportGenerator.Generate(BuildRequest(zipPath, source));
        }

        Assert.Equal(hashBefore, HashFile(zipPath));
        Assert.Equal(modifiedBefore, File.GetLastWriteTimeUtc(zipPath));
    }

    [Fact]
    public void CaseInfoAppearsOnlyWhenProvided()
    {
        var zipPath = CreateExportZip();

        using (var source = InspectionSource.Open(zipPath))
        {
            var without = InspectionReportGenerator.Generate(BuildRequest(zipPath, source, txt: false));
            Assert.DoesNotContain("Caso Judicial", File.ReadAllText(without.HtmlPath!));
        }

        using (var source = InspectionSource.Open(zipPath))
        {
            var caseInfo = new ReportCaseInfo
            {
                CaseNumber = "IPP-12345/2026",
                ExaminerName = "Perito de Prueba",
            };
            var with = InspectionReportGenerator.Generate(BuildRequest(zipPath, source, caseInfo, txt: false));
            var content = File.ReadAllText(with.HtmlPath!);
            Assert.Contains("Caso Judicial", content);
            Assert.Contains("IPP-12345/2026", content);
            Assert.Contains("Perito de Prueba", content);
        }
    }

    [Fact]
    public void NeverOverwritesExistingReports()
    {
        var zipPath = CreateExportZip();
        using var source = InspectionSource.Open(zipPath);
        var caseInfo = new ReportCaseInfo { CaseNumber = "MISMO-CASO" };

        // Dos generaciones en el mismo segundo compiten por el mismo nombre base.
        var first = InspectionReportGenerator.Generate(BuildRequest(zipPath, source, caseInfo, txt: false));
        var second = InspectionReportGenerator.Generate(BuildRequest(zipPath, source, caseInfo, txt: false));

        Assert.NotEqual(first.HtmlPath, second.HtmlPath);
        Assert.True(File.Exists(first.HtmlPath!));
        Assert.True(File.Exists(second.HtmlPath!));
    }

    [Fact]
    public void MissingAttachmentIsReportedAsAnomaly()
    {
        var zipPath = CreateExportZip();
        using var source = InspectionSource.Open(zipPath);

        var result = InspectionReportGenerator.Generate(BuildRequest(zipPath, source, html: false));
        var content = File.ReadAllText(result.TxtPath!);

        Assert.Contains("export incompleto", content);
        Assert.Contains("00000013-VIDEO-2024-06-25.mp4", content);
    }

    [Fact]
    public void RequiresAtLeastOneFormat()
    {
        var zipPath = CreateExportZip();
        using var source = InspectionSource.Open(zipPath);

        Assert.Throws<ArgumentException>(() =>
            InspectionReportGenerator.Generate(BuildRequest(zipPath, source, html: false, txt: false)));
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
