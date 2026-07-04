using System.Security.Cryptography;
using PRViewer.Core.Extraction;
using PRViewer.Core.Reporting;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Tests;

public class ControlledExtractionTests : IDisposable
{
    private readonly TestPackage _package = new();
    private readonly string _destination;

    public ControlledExtractionTests()
    {
        _destination = Path.Combine(_package.RootPath, "extraccion");
        Directory.CreateDirectory(_destination);
    }

    public void Dispose() => _package.Dispose();

    private static readonly byte[] PhotoBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 10, 20, 30, 40, 50 };

    private string CreateZip() => _package.CreateZip("export.zip", new Dictionary<string, byte[]>
    {
        ["_chat.txt"] = TestPackage.Utf8("[25/6/2024, 14:03:12] Ana García: hola"),
        ["IMG-0001.jpg"] = PhotoBytes,
    });

    private static ExtractionRequest BuildRequest(string packagePath, IInspectionSource source,
        IReadOnlyList<SourceEntry> entries, string destination, PackageKind kind = PackageKind.Zip,
        IReadOnlyDictionary<string, string>? knownHashes = null)
    {
        string? sha256 = null;
        if (File.Exists(packagePath))
        {
            using var stream = File.OpenRead(packagePath);
            sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        return new ExtractionRequest
        {
            Package = new PackageIdentity(Path.GetFileName(packagePath), packagePath, kind, sha256, null, null),
            Source = source,
            Entries = entries,
            DestinationDirectory = destination,
            KnownHashes = knownHashes,
        };
    }

    [Fact]
    public void ExtractsEntryVerifiedWithManifest()
    {
        var zipPath = CreateZip();
        using var source = InspectionSource.Open(zipPath);
        var photo = source.Entries.Single(e => e.Name == "IMG-0001.jpg");

        var expectedHash = Convert.ToHexString(SHA256.HashData(PhotoBytes)).ToLowerInvariant();
        var result = ControlledExtractionService.Extract(BuildRequest(
            zipPath, source, new[] { photo }, _destination,
            knownHashes: new Dictionary<string, string> { ["IMG-0001.jpg"] = expectedHash }));

        // Copia escrita, verificada, con el contenido exacto.
        var record = Assert.Single(result.Entries);
        Assert.True(record.Verified);
        Assert.Equal(expectedHash, record.SourceSha256);
        Assert.Equal(expectedHash, record.CopySha256);
        var copyPath = Path.Combine(_destination, record.ExportedAs!);
        Assert.Equal(PhotoBytes, File.ReadAllBytes(copyPath));

        // Constancia con paquete, entrada, hashes y veredicto.
        var manifest = File.ReadAllText(result.ManifestPath);
        Assert.Contains("CONSTANCIA DE EXTRACCIÓN CONTROLADA", manifest);
        Assert.Contains("export.zip", manifest);
        Assert.Contains(expectedHash, manifest);
        Assert.Contains("VERIFICADA", manifest);
        Assert.Contains("UTC", manifest);
        Assert.Contains("1 entrada(s) extraída(s) y verificada(s), 0 error(es)", manifest);
    }

    [Fact]
    public void NeverOverwritesExistingFilesInDestination()
    {
        var zipPath = CreateZip();
        using var source = InspectionSource.Open(zipPath);
        var photo = source.Entries.Single(e => e.Name == "IMG-0001.jpg");

        // Un archivo ajeno con el mismo nombre ya vive en el destino.
        var preexisting = Path.Combine(_destination, "IMG-0001.jpg");
        File.WriteAllBytes(preexisting, new byte[] { 1, 2, 3 });

        var result = ControlledExtractionService.Extract(BuildRequest(zipPath, source, new[] { photo }, _destination));

        var record = Assert.Single(result.Entries);
        Assert.True(record.Verified);
        Assert.NotEqual("IMG-0001.jpg", record.ExportedAs);

        // El preexistente quedó intacto; la copia salió con sufijo.
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(preexisting));
        Assert.Equal(PhotoBytes, File.ReadAllBytes(Path.Combine(_destination, record.ExportedAs!)));
    }

    [Fact]
    public void SourcePackageRemainsIntact()
    {
        var zipPath = CreateZip();
        var hashBefore = HashFile(zipPath);

        using (var source = InspectionSource.Open(zipPath))
        {
            ControlledExtractionService.Extract(BuildRequest(zipPath, source, source.Entries, _destination));
        }

        Assert.Equal(hashBefore, HashFile(zipPath));
    }

    [Fact]
    public void RejectsDestinationInsideInspectedFolder()
    {
        var folderPath = _package.CreateFolder("export-carpeta", new Dictionary<string, byte[]>
        {
            ["_chat.txt"] = TestPackage.Utf8("[25/6/2024, 14:03:12] Ana García: hola"),
        });
        var insideDestination = Path.Combine(folderPath, "sub");
        Directory.CreateDirectory(insideDestination);

        using var source = InspectionSource.Open(folderPath);

        Assert.Throws<ArgumentException>(() => ControlledExtractionService.Extract(BuildRequest(
            folderPath, source, source.Entries, insideDestination, PackageKind.Folder)));

        Assert.Throws<ArgumentException>(() => ControlledExtractionService.Extract(BuildRequest(
            folderPath, source, source.Entries, folderPath, PackageKind.Folder)));
    }

    [Fact]
    public void MismatchAgainstIngestedHashDiscardsCopy()
    {
        var zipPath = CreateZip();
        using var source = InspectionSource.Open(zipPath);
        var photo = source.Entries.Single(e => e.Name == "IMG-0001.jpg");

        // Hash de ingesta deliberadamente distinto: simula una entrada que cambió
        // entre la ingesta y la extracción. La copia debe descartarse.
        var result = ControlledExtractionService.Extract(BuildRequest(
            zipPath, source, new[] { photo }, _destination,
            knownHashes: new Dictionary<string, string> { ["IMG-0001.jpg"] = new string('0', 64) }));

        var record = Assert.Single(result.Entries);
        Assert.False(record.Verified);
        Assert.Null(record.ExportedAs);
        Assert.Contains("no coincide", record.Error);

        // En el destino solo queda la constancia, ninguna copia.
        var files = Directory.GetFiles(_destination).Select(Path.GetFileName).ToList();
        var manifestName = Path.GetFileName(result.ManifestPath);
        Assert.Equal(new[] { manifestName }, files);

        Assert.Contains("FALLIDA", File.ReadAllText(result.ManifestPath));
    }

    [Fact]
    public void ExtractsMultipleEntriesIncludingChat()
    {
        var zipPath = CreateZip();
        using var source = InspectionSource.Open(zipPath);

        var result = ControlledExtractionService.Extract(BuildRequest(zipPath, source, source.Entries, _destination));

        Assert.Equal(2, result.VerifiedCount);
        Assert.Equal(0, result.ErrorCount);
        Assert.True(File.Exists(Path.Combine(_destination, "_chat.txt")));
        Assert.True(File.Exists(Path.Combine(_destination, "IMG-0001.jpg")));
    }

    [Fact]
    public void RequiresAtLeastOneEntry()
    {
        var zipPath = CreateZip();
        using var source = InspectionSource.Open(zipPath);

        Assert.Throws<ArgumentException>(() => ControlledExtractionService.Extract(
            BuildRequest(zipPath, source, Array.Empty<SourceEntry>(), _destination)));
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
