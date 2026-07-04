using System.Security.Cryptography;
using PRViewer.Core.Ingestion;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Tests;

/// <summary>
/// Verificación del invariante central: la inspección completa de un paquete
/// no altera ni un byte del material de origen.
/// </summary>
public class ReadOnlyInvariantTests : IDisposable
{
    private readonly TestPackage _package = new();

    public void Dispose() => _package.Dispose();

    [Fact]
    public void FullIngestDoesNotModifySourcePackage()
    {
        var zipPath = _package.CreateZip("export.zip", new Dictionary<string, byte[]>
        {
            ["_chat.txt"] = TestPackage.Utf8(
                "[25/6/2024, 14:03:12] Ana: ‎<attached: IMG-0001.jpg>\n" +
                "[25/6/2024, 14:04:00] Juan: Recibido"),
            ["IMG-0001.jpg"] = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 9, 9, 9 },
        });

        var hashBefore = HashFile(zipPath);
        var lastWriteBefore = File.GetLastWriteTimeUtc(zipPath);

        // Ingesta completa: listado, detección, parseo y hash de adjuntos.
        using (var source = InspectionSource.Open(zipPath))
        {
            var conversation = ExportIngestorRegistry.CreateDefault().Ingest(source);
            Assert.Equal(2, conversation.MessageCount);
            Assert.Single(conversation.Attachments);
        }

        Assert.Equal(hashBefore, HashFile(zipPath));
        Assert.Equal(lastWriteBefore, File.GetLastWriteTimeUtc(zipPath));
    }

    [Fact]
    public void SourceDoesNotLockOthersOutOfReading()
    {
        var zipPath = _package.CreateZip("export.zip", new Dictionary<string, byte[]>
        {
            ["_chat.txt"] = TestPackage.Utf8("[25/6/2024, 14:03:12] Ana: Hola"),
        });

        using var source = InspectionSource.Open(zipPath);

        // Otro proceso (p. ej. la herramienta de hash del consumidor) puede leer en paralelo.
        using var parallelReader = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.True(parallelReader.CanRead);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
