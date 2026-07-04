using PRViewer.Core.Ingestion.WhatsApp;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Tests;

public class WhatsAppTxtIngestorTests : IDisposable
{
    private readonly TestPackage _package = new();
    private readonly WhatsAppTxtIngestor _ingestor = new();

    public void Dispose() => _package.Dispose();

    private const string AndroidChat = """
        25/6/24 14:03 - Los mensajes y las llamadas están cifrados de extremo a extremo.
        25/6/24 14:03 - Ana García: Hola, ¿cómo estás?
        25/6/24 14:05 - Juan Pérez: Bien, te mando la foto
        25/6/24 14:06 - Juan Pérez: IMG-20240625-WA0001.jpg (archivo adjunto)
        25/6/24 14:07 - Ana García: Perfecto, gracias
        con esto queda resuelto
        26/6/24 09:15 - Juan Pérez: Dale
        """;

    [Fact]
    public void ParsesAndroidFormatChat()
    {
        var filePath = _package.CreateFile("_chat.txt", TestPackage.Utf8(AndroidChat));
        using var source = InspectionSource.Open(filePath);

        var conversation = _ingestor.Ingest(source);

        Assert.Equal(Platform.WhatsApp, conversation.Platform);
        Assert.Equal(new[] { "Ana García", "Juan Pérez" }, conversation.Participants);
        Assert.Equal(6, conversation.MessageCount);
        Assert.Equal(new DateTime(2024, 6, 25, 14, 3, 0), conversation.DateRange.First);
        Assert.Equal(new DateTime(2024, 6, 26, 9, 15, 0), conversation.DateRange.Last);
    }

    [Fact]
    public void FirstLineWithoutSenderIsSystemMessage()
    {
        var filePath = _package.CreateFile("_chat.txt", TestPackage.Utf8(AndroidChat));
        using var source = InspectionSource.Open(filePath);

        var conversation = _ingestor.Ingest(source);

        Assert.True(conversation.Messages[0].IsSystemMessage);
        Assert.Null(conversation.Messages[0].Sender);
        Assert.False(conversation.Messages[1].IsSystemMessage);
    }

    [Fact]
    public void MultilineMessageIsAppendedToPrevious()
    {
        var filePath = _package.CreateFile("_chat.txt", TestPackage.Utf8(AndroidChat));
        using var source = InspectionSource.Open(filePath);

        var conversation = _ingestor.Ingest(source);

        var multiline = conversation.Messages.Single(m => m.Text.Contains("con esto queda resuelto"));
        Assert.Equal("Perfecto, gracias\ncon esto queda resuelto", multiline.Text);
    }

    [Fact]
    public void ParsesIosFormatWithPresentAndMissingAttachments()
    {
        var mediaBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        var chat = "‎[25/6/2024, 14:03:12] Ana García: ‎<attached: 00000012-PHOTO-2024-06-25.jpg>\n" +
                   "[25/6/2024, 14:04:00] Juan Pérez: ‎<attached: 00000013-VIDEO-2024-06-25.mp4>\n" +
                   "[25/6/2024, 14:05:30] Ana García: Qué lindo!";

        var zipPath = _package.CreateZip("export.zip", new Dictionary<string, byte[]>
        {
            ["_chat.txt"] = TestPackage.Utf8(chat),
            ["00000012-PHOTO-2024-06-25.jpg"] = mediaBytes,
            // El video referenciado NO está en el paquete: export incompleto.
        });

        using var source = InspectionSource.Open(zipPath);
        var conversation = _ingestor.Ingest(source);

        Assert.Equal(3, conversation.MessageCount);
        Assert.Equal(2, conversation.Attachments.Count);

        var photo = conversation.Attachments.Single(a => a.Name.EndsWith(".jpg"));
        Assert.True(photo.IsPresent);
        Assert.Equal(mediaBytes.Length, photo.Size);
        Assert.Equal("image/jpeg", photo.ContentType);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(mediaBytes)).ToLowerInvariant(), photo.Sha256);

        var video = conversation.Attachments.Single(a => a.Name.EndsWith(".mp4"));
        Assert.False(video.IsPresent);
        Assert.Null(video.Size);
        Assert.Null(video.Sha256);
    }

    [Fact]
    public void ParsesSpanishAmPmTimestamps()
    {
        var chat = "25/6/24 2:03 p. m. - Ana García: Hola";
        var filePath = _package.CreateFile("_chat.txt", TestPackage.Utf8(chat));
        using var source = InspectionSource.Open(filePath);

        var conversation = _ingestor.Ingest(source);

        Assert.Equal(new DateTime(2024, 6, 25, 14, 3, 0), conversation.Messages[0].Timestamp);
    }

    [Fact]
    public void DetectsChatByContentEvenWithNonStandardName()
    {
        // Detección por contenido: el nombre del .txt no importa.
        var filePath = _package.CreateFile("conversacion exportada.txt",
            TestPackage.Utf8("25/6/24 14:03 - Ana García: Hola"));
        using var source = InspectionSource.Open(filePath);

        Assert.True(_ingestor.CanIngest(source));
    }

    [Fact]
    public void RejectsPlainTextThatIsNotWhatsApp()
    {
        var filePath = _package.CreateFile("notas.txt",
            TestPackage.Utf8("Estas son notas sueltas\nsin formato de chat"));
        using var source = InspectionSource.Open(filePath);

        Assert.False(_ingestor.CanIngest(source));
    }
}
