using System.Security.Cryptography;
using PRViewer.Core.Ingestion.Meta;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Tests;

public class MetaHtmlIngestorTests : IDisposable
{
    private readonly TestPackage _package = new();

    public void Dispose() => _package.Dispose();

    private const string IgInbox = "your_instagram_activity/messages/inbox";
    private const string FbInbox = "your_facebook_activity/messages/inbox";

    // Plantilla Instagram: un <h2> por mensaje, timestamp sin segundos.
    private static string IgThread() =>
        "<html><head><title>Ana</title></head><body>" +
        "<header><div><h1>Ana García</h1></div></header><main>" +
        "<div class=\"pam _a6-g uiBoxWhite noborder\"><h2 class=\"_a6-h\">Ana García</h2>" +
        "<div class=\"_a6-p\"><div>Hola Juan</div></div><div class=\"_a6-o\">jun 11, 2026 9:59 am</div></div>" +
        "<div class=\"pam _a6-g uiBoxWhite noborder\"><h2 class=\"_a6-h\">Juan Pérez</h2>" +
        "<div class=\"_a6-p\"><div>Hola Ana</div></div><div class=\"_a6-o\">jun 11, 2026 10:00 am</div></div>" +
        "</main></body></html>";

    // Plantilla Facebook: <h2> «pegajoso» (solo al cambiar de emisor), timestamp con segundos.
    private static string FbThread() =>
        "<html><head><title>Grupo</title></head><body>" +
        "<header><div><h1>Grupo</h1></div></header><main>" +
        "<div class=\"_a6-p\"><div>mensaje sin h2 inicial</div></div><div class=\"_a72d\">dic 08, 2020 10:19:48 pm</div>" +
        "<h2 class=\"_a6-h\">Beto</h2><div class=\"_a6-p\"><div>segundo</div></div><div class=\"_a72d\">dic 09, 2020 11:00:00 am</div>" +
        "<div class=\"_a6-p\"><div>tercero mismo Beto</div></div><div class=\"_a72d\">dic 10, 2020 12:00:00 pm</div>" +
        "</main></body></html>";

    [Fact]
    public void DetectsInstagramByInboxPathNotExtension()
    {
        var zip = _package.CreateZip("ig.zip", new Dictionary<string, byte[]>
        {
            [$"{IgInbox}/ana_1/message_1.html"] = TestPackage.Utf8(IgThread()),
        });
        using var source = InspectionSource.Open(zip);

        Assert.True(new MetaInstagramHtmlIngestor().CanIngest(source));
        Assert.False(new MetaFacebookHtmlIngestor().CanIngest(source));
    }

    [Fact]
    public void InstagramParsesSendersTextAndTimestamp()
    {
        var zip = _package.CreateZip("ig.zip", new Dictionary<string, byte[]>
        {
            [$"{IgInbox}/ana_1/message_1.html"] = TestPackage.Utf8(IgThread()),
        });
        using var source = InspectionSource.Open(zip);

        var package = new MetaInstagramHtmlIngestor().Ingest(source);

        Assert.Equal(Platform.MetaInstagram, package.Platform);
        Assert.Equal(1, package.ThreadCount);
        Assert.Equal("Ana García", package.Threads[0].Title);
        Assert.Equal(2, package.MessageCount);

        var first = package.Threads[0].Messages[0];
        Assert.Equal("Ana García", first.Sender);
        Assert.Equal("Hola Juan", first.Text);
        Assert.Equal(new DateTime(2026, 6, 11, 9, 59, 0), first.Timestamp);
        Assert.Equal("Juan Pérez", package.Threads[0].Messages[1].Sender);
    }

    [Fact]
    public void FacebookStickySenderAndSecondsTimestamp()
    {
        var zip = _package.CreateZip("fb.zip", new Dictionary<string, byte[]>
        {
            [$"{FbInbox}/grupo_1/message_1.html"] = TestPackage.Utf8(FbThread()),
        });
        using var source = InspectionSource.Open(zip);

        var package = new MetaFacebookHtmlIngestor().Ingest(source);
        var msgs = package.Threads[0].Messages; // ordenados cronológicamente

        Assert.Equal(Platform.MetaFacebook, package.Platform);
        Assert.Equal(3, msgs.Count);

        // El primer mensaje no tiene <h2> previo: remitente desconocido.
        Assert.Null(msgs[0].Sender);
        Assert.Equal(new DateTime(2020, 12, 8, 22, 19, 48), msgs[0].Timestamp);
        // El <h2> «Beto» aplica al segundo y, pegajoso, también al tercero.
        Assert.Equal("Beto", msgs[1].Sender);
        Assert.Equal("Beto", msgs[2].Sender);
    }

    [Fact]
    public void OrdersMessagesChronologically()
    {
        // Meta exporta del más nuevo al más viejo; el ingestor reordena ascendente.
        var html =
            "<html><head></head><body><header><h1>T</h1></header><main>" +
            "<div class=\"_a6-p\"><div>nuevo</div></div><div class=\"x\">jun 11, 2026 10:00 am</div>" +
            "<div class=\"_a6-p\"><div>viejo</div></div><div class=\"x\">ene 05, 2026 8:00 am</div>" +
            "</main></body></html>";
        var zip = _package.CreateZip("ig.zip", new Dictionary<string, byte[]>
        {
            [$"{IgInbox}/t/message_1.html"] = TestPackage.Utf8(html),
        });
        using var source = InspectionSource.Open(zip);

        var msgs = new MetaInstagramHtmlIngestor().Ingest(source).Threads[0].Messages;

        Assert.Equal("viejo", msgs[0].Text);
        Assert.Equal("nuevo", msgs[1].Text);
    }

    [Fact]
    public void LocalMediaPresentIsHashedExternalIgnored()
    {
        var mediaBytes = new byte[] { 9, 8, 7, 6 };
        var mediaPath = $"{IgInbox}/ana_1/photos/1.jpg";
        var html =
            "<html><head></head><body><header><h1>Ana</h1></header><main>" +
            "<div class=\"_a6-p\"><h2>Ana</h2><div>mirá</div>" +
            $"<img src=\"{mediaPath}\" /><img src=\"https://cdn.instagram.com/externa.jpg\" /></div>" +
            "<div class=\"x\">jun 11, 2026 9:59 am</div>" +
            "</main></body></html>";
        var zip = _package.CreateZip("ig.zip", new Dictionary<string, byte[]>
        {
            [$"{IgInbox}/ana_1/message_1.html"] = TestPackage.Utf8(html),
            [mediaPath] = mediaBytes,
        });
        using var source = InspectionSource.Open(zip);

        var package = new MetaInstagramHtmlIngestor().Ingest(source);

        // Solo el archivo local es adjunto; el src externo (http) se ignora.
        var attachment = Assert.Single(package.Attachments);
        Assert.Equal("1.jpg", attachment.Name);
        Assert.True(attachment.IsPresent);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(mediaBytes)).ToLowerInvariant(), attachment.Sha256);
    }

    [Fact]
    public void ReferencedMediaMissingFromPackageIsMarkedAbsent()
    {
        var html =
            "<html><head></head><body><header><h1>Ana</h1></header><main>" +
            "<div class=\"_a6-p\"><h2>Ana</h2><div>foto</div>" +
            $"<img src=\"{IgInbox}/ana_1/photos/perdida.jpg\" /></div>" +
            "<div class=\"x\">jun 11, 2026 9:59 am</div>" +
            "</main></body></html>";
        // No se incluye el archivo de la foto: referenciado pero ausente.
        var zip = _package.CreateZip("ig.zip", new Dictionary<string, byte[]>
        {
            [$"{IgInbox}/ana_1/message_1.html"] = TestPackage.Utf8(html),
        });
        using var source = InspectionSource.Open(zip);

        var attachment = Assert.Single(new MetaInstagramHtmlIngestor().Ingest(source).Attachments);
        Assert.Equal("perdida.jpg", attachment.Name);
        Assert.False(attachment.IsPresent);
        Assert.Null(attachment.Sha256);
    }

    [Fact]
    public void MultipleInboxFoldersBecomeSeparateThreads()
    {
        var zip = _package.CreateZip("ig.zip", new Dictionary<string, byte[]>
        {
            [$"{IgInbox}/ana_1/message_1.html"] = TestPackage.Utf8(IgThread()),
            [$"{IgInbox}/otro_2/message_1.html"] = TestPackage.Utf8(IgThread()),
        });
        using var source = InspectionSource.Open(zip);

        Assert.Equal(2, new MetaInstagramHtmlIngestor().Ingest(source).ThreadCount);
    }
}
