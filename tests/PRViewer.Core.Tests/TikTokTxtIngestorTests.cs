using PRViewer.Core.Ingestion.TikTok;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Tests;

public class TikTokTxtIngestorTests : IDisposable
{
    private readonly TestPackage _package = new();
    private readonly TikTokTxtIngestor _ingestor = new();

    public void Dispose() => _package.Dispose();

    // Dos conversaciones (dos bloques), con un video compartido por URL.
    private const string DirectMessages =
        ">>> Chat History with anthonycof::\n" +
        "\n" +
        "2026-07-03 23:00:11 UTC rominacofre860: hola\n" +
        "2026-07-01 03:01:15 UTC anthonycof: https://www.tiktokv.com/share/video/7637602829980552468/\n" +
        "\n" +
        ">>> Chat History with otrouser::\n" +
        "\n" +
        "2026-06-01 10:00:00 UTC rominacofre860: buenas\n";

    private string CreateExport(string dmContent = DirectMessages) =>
        _package.CreateZip("tiktok.zip", new Dictionary<string, byte[]>
        {
            // Nombre de archivo en español; la detección es por contenido, no por nombre.
            ["TikTok/Mensajes directos/Mensajes directos.txt"] = TestPackage.Utf8(dmContent),
            // Sección vacía: no aporta hilos.
            ["TikTok/Chat de grupo/Chat de grupo.txt"] = TestPackage.Utf8("No hay datos en esta sección"),
        });

    [Fact]
    public void DetectsByContentMarkerNotFilename()
    {
        var zipPath = CreateExport();
        using var source = InspectionSource.Open(zipPath);

        Assert.True(_ingestor.CanIngest(source));
        Assert.Equal(Platform.TikTok, _ingestor.Platform);
    }

    [Fact]
    public void EachChatBlockBecomesAThread()
    {
        var zipPath = CreateExport();
        using var source = InspectionSource.Open(zipPath);

        var package = _ingestor.Ingest(source);

        Assert.Equal(Platform.TikTok, package.Platform);
        Assert.Equal(2, package.ThreadCount);
        Assert.Equal(3, package.MessageCount);
        Assert.Equal("Conversación con anthonycof", package.Threads[0].Title);
        Assert.Equal("Conversación con otrouser", package.Threads[1].Title);
    }

    [Fact]
    public void ParsesUtcTimestampsAndParticipants()
    {
        var zipPath = CreateExport();
        using var source = InspectionSource.Open(zipPath);

        var package = _ingestor.Ingest(source);
        var thread = package.Threads[0];

        Assert.Contains("rominacofre860", thread.Participants);
        Assert.Contains("anthonycof", thread.Participants);
        Assert.Equal(new DateTime(2026, 7, 1, 3, 1, 15), thread.DateRange.First);
        Assert.Equal(new DateTime(2026, 7, 3, 23, 0, 11), thread.DateRange.Last);
    }

    [Fact]
    public void SharedVideoUrlIsReferencedButNotPresent()
    {
        var zipPath = CreateExport();
        using var source = InspectionSource.Open(zipPath);

        var package = _ingestor.Ingest(source);

        var attachment = package.Attachments.Single();
        Assert.Equal("TikTok video 7637602829980552468", attachment.Name);
        Assert.False(attachment.IsPresent);
        Assert.Null(attachment.Sha256);
    }

    [Fact]
    public void EmptySectionContributesNoThreads()
    {
        // Solo la sección vacía: no hay chats reconocibles.
        var zipPath = _package.CreateZip("vacio.zip", new Dictionary<string, byte[]>
        {
            ["TikTok/Chat de grupo/Chat de grupo.txt"] = TestPackage.Utf8("No hay datos en esta sección"),
        });
        using var source = InspectionSource.Open(zipPath);

        Assert.False(_ingestor.CanIngest(source));
    }

    [Fact]
    public void MultilineMessageIsAppendedToPrevious()
    {
        var dm =
            ">>> Chat History with anthonycof::\n" +
            "\n" +
            "2026-07-03 23:00:11 UTC rominacofre860: primera línea\n" +
            "segunda línea del mismo mensaje\n";
        var zipPath = CreateExport(dm);
        using var source = InspectionSource.Open(zipPath);

        var package = _ingestor.Ingest(source);
        var message = package.Threads[0].Messages.Single();

        Assert.Equal("primera línea\nsegunda línea del mismo mensaje", message.Text);
    }
}
