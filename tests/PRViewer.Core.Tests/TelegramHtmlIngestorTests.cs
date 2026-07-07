using System.Security.Cryptography;
using PRViewer.Core.Ingestion.Telegram;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Tests;

public class TelegramHtmlIngestorTests : IDisposable
{
    private readonly TestPackage _package = new();
    private readonly TelegramHtmlIngestor _ingestor = new();

    public void Dispose() => _package.Dispose();

    // Un chat con: separador de servicio (se ignora), un mensaje con remitente y otro
    // «joined» (sin from_name → mismo emisor, pegajoso).
    private static string Chat(string extraMessages = "") =>
        "<html><head><link href=\"../../css/style.css\"></head><body>" +
        "<div class=\"page_header\"><div class=\"text bold\">Gonza</div></div>" +
        "<div class=\"history\">" +
        "<div class=\"message service\" id=\"message-1\"><div class=\"body details\">17 February 2021</div></div>" +
        "<div class=\"message default clearfix\" id=\"message1\"><div class=\"body\">" +
        "<div class=\"pull_right date details\" title=\"17.02.2021 12:57:04 UTC-03:00\">12:57</div>" +
        "<div class=\"from_name\">Gonza</div><div class=\"text\">Hola Claudio</div></div></div>" +
        "<div class=\"message default clearfix joined\" id=\"message2\"><div class=\"body\">" +
        "<div class=\"pull_right date details\" title=\"17.02.2021 12:58:00 UTC-03:00\">12:58</div>" +
        "<div class=\"text\">¿todo bien?</div></div></div>" +
        extraMessages +
        "</div></body></html>";

    [Fact]
    public void DetectsByChatPathAndContent()
    {
        var zip = _package.CreateZip("tg.zip", new Dictionary<string, byte[]>
        {
            ["chats/chat_001/messages.html"] = TestPackage.Utf8(Chat()),
        });
        using var source = InspectionSource.Open(zip);

        Assert.True(_ingestor.CanIngest(source));
        Assert.Equal(Platform.Telegram, _ingestor.Platform);
    }

    [Fact]
    public void ParsesMessagesSkippingServiceAndTitleFromHeader()
    {
        var zip = _package.CreateZip("tg.zip", new Dictionary<string, byte[]>
        {
            ["chats/chat_001/messages.html"] = TestPackage.Utf8(Chat()),
        });
        using var source = InspectionSource.Open(zip);

        var package = _ingestor.Ingest(source);

        Assert.Equal(1, package.ThreadCount);
        Assert.Equal("Gonza", package.Threads[0].Title);
        Assert.Equal(2, package.MessageCount); // el mensaje de servicio se ignora
    }

    [Fact]
    public void StickySenderAndTimestampWithTimezone()
    {
        var zip = _package.CreateZip("tg.zip", new Dictionary<string, byte[]>
        {
            ["chats/chat_001/messages.html"] = TestPackage.Utf8(Chat()),
        });
        using var source = InspectionSource.Open(zip);

        var msgs = _ingestor.Ingest(source).Threads[0].Messages;

        Assert.Equal("Gonza", msgs[0].Sender);
        Assert.Equal("Hola Claudio", msgs[0].Text);
        Assert.Equal(new DateTime(2021, 2, 17, 12, 57, 4), msgs[0].Timestamp);
        // El segundo es «joined» sin from_name: hereda el remitente.
        Assert.Equal("Gonza", msgs[1].Sender);
        Assert.Equal("¿todo bien?", msgs[1].Text);
    }

    [Fact]
    public void ResolvesLocalMediaIgnoringNavigationAndCss()
    {
        var mediaBytes = new byte[] { 4, 5, 6 };
        var mediaMessage =
            "<div class=\"message default clearfix\" id=\"message3\"><div class=\"body\">" +
            "<div class=\"pull_right date details\" title=\"18.02.2021 09:00:00 UTC-03:00\">09:00</div>" +
            "<div class=\"from_name\">Gonza</div>" +
            "<div class=\"media_wrap clearfix\">" +
            "<a class=\"photo_wrap clearfix pull_left\" href=\"photos/p.jpg\"></a></div>" +
            // navegación: no es adjunto
            "<a class=\"content block_link\" href=\"../../chats.html\"></a>" +
            "</div></div>";
        var zip = _package.CreateZip("tg.zip", new Dictionary<string, byte[]>
        {
            ["chats/chat_001/messages.html"] = TestPackage.Utf8(Chat(mediaMessage)),
            ["chats/chat_001/photos/p.jpg"] = mediaBytes,
        });
        using var source = InspectionSource.Open(zip);

        var package = _ingestor.Ingest(source);

        var attachment = Assert.Single(package.Attachments);
        Assert.Equal("p.jpg", attachment.Name);
        Assert.True(attachment.IsPresent);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(mediaBytes)).ToLowerInvariant(), attachment.Sha256);
    }

    [Fact]
    public void ChatsWithoutRealMessagesAreDropped()
    {
        // Chat solo con mensajes de servicio: no aporta conversación.
        var serviceOnly =
            "<html><head></head><body><div class=\"page_header\"><div class=\"text bold\">Canal</div></div>" +
            "<div class=\"message service\" id=\"message-1\"><div class=\"body details\">Canal creado</div></div>" +
            "</body></html>";
        var zip = _package.CreateZip("tg.zip", new Dictionary<string, byte[]>
        {
            ["chats/chat_001/messages.html"] = TestPackage.Utf8(Chat()),
            ["chats/chat_050/messages.html"] = TestPackage.Utf8(serviceOnly),
        });
        using var source = InspectionSource.Open(zip);

        // Solo el chat con mensajes reales queda.
        Assert.Equal(1, _ingestor.Ingest(source).ThreadCount);
    }

    [Fact]
    public void MultipleChatsBecomeSeparateThreads()
    {
        var zip = _package.CreateZip("tg.zip", new Dictionary<string, byte[]>
        {
            ["chats/chat_001/messages.html"] = TestPackage.Utf8(Chat()),
            ["chats/chat_002/messages.html"] = TestPackage.Utf8(Chat()),
        });
        using var source = InspectionSource.Open(zip);

        Assert.Equal(2, _ingestor.Ingest(source).ThreadCount);
    }
}
