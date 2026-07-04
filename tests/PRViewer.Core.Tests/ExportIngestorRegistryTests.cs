using PRViewer.Core.Ingestion;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Tests;

public class ExportIngestorRegistryTests : IDisposable
{
    private readonly TestPackage _package = new();

    public void Dispose() => _package.Dispose();

    [Fact]
    public void DefaultRegistryDetectsWhatsAppExport()
    {
        var zipPath = _package.CreateZip("export.zip", new Dictionary<string, byte[]>
        {
            ["_chat.txt"] = TestPackage.Utf8("[25/6/2024, 14:03:12] Ana: Hola"),
        });

        using var source = InspectionSource.Open(zipPath);
        var registry = ExportIngestorRegistry.CreateDefault();

        var ingestor = registry.Detect(source);

        Assert.NotNull(ingestor);
        Assert.Equal(Platform.WhatsApp, ingestor.Platform);
    }

    [Fact]
    public void UnknownContentThrowsUnknownPlatform()
    {
        var zipPath = _package.CreateZip("otro.zip", new Dictionary<string, byte[]>
        {
            ["datos.bin"] = new byte[] { 1, 2, 3 },
        });

        using var source = InspectionSource.Open(zipPath);
        var registry = ExportIngestorRegistry.CreateDefault();

        Assert.Null(registry.Detect(source));
        Assert.Throws<UnknownPlatformException>(() => registry.Ingest(source));
    }
}
