using System.Security.Cryptography;
using PRViewer.Core.Ingestion.Twitter;
using PRViewer.Core.Model;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Tests;

public class TwitterDmIngestorTests : IDisposable
{
    private readonly TestPackage _package = new();
    private readonly TwitterDmIngestor _ingestor = new();

    public void Dispose() => _package.Dispose();

    // Titular: id 100, @yo / «Yo Titular». El export de X asigna a window.YTD.…
    private const string Account = """
        window.YTD.account.part0 = [
          { "account" : { "accountId" : "100", "username" : "yo", "accountDisplayName" : "Yo Titular" } }
        ]
        """;

    // Dos conversaciones (dos corresponsales): 200 y 300.
    private const string DirectMessages = """
        window.YTD.direct_messages.part0 = [
          {
            "dmConversation" : {
              "conversationId" : "100-200",
              "messages" : [
                { "messageCreate" : { "recipientId" : "200", "senderId" : "100", "text" : "Hola 200", "mediaUrls" : [], "id" : "1", "createdAt" : "2024-06-25T14:03:00.000Z" } },
                { "messageCreate" : { "recipientId" : "100", "senderId" : "200", "text" : "Hola titular", "mediaUrls" : [], "id" : "2", "createdAt" : "2024-06-25T14:05:00.000Z" } }
              ]
            }
          },
          {
            "dmConversation" : {
              "conversationId" : "300-100",
              "messages" : [
                { "messageCreate" : { "recipientId" : "100", "senderId" : "300", "text" : "Otra charla", "mediaUrls" : [], "id" : "9", "createdAt" : "2024-07-01T09:00:00.000Z" } }
              ]
            }
          }
        ]
        """;

    private string CreateExport(string directMessages = DirectMessages,
        IReadOnlyDictionary<string, byte[]>? extraMedia = null)
    {
        var entries = new Dictionary<string, byte[]>
        {
            ["data/account.js"] = TestPackage.Utf8(Account),
            ["data/direct-messages.js"] = TestPackage.Utf8(directMessages),
        };
        if (extraMedia is not null)
        {
            foreach (var (name, content) in extraMedia)
                entries[name] = content;
        }

        return _package.CreateZip("twitter.zip", entries);
    }

    [Fact]
    public void DetectsByContentNotExtension()
    {
        var zipPath = CreateExport();
        using var source = InspectionSource.Open(zipPath);

        Assert.True(_ingestor.CanIngest(source));
        Assert.Equal(Platform.TwitterX, _ingestor.Platform);
    }

    [Fact]
    public void EachDmConversationBecomesAThread()
    {
        var zipPath = CreateExport();
        using var source = InspectionSource.Open(zipPath);

        var package = _ingestor.Ingest(source);

        Assert.Equal(Platform.TwitterX, package.Platform);
        Assert.Equal(2, package.ThreadCount);
        Assert.Equal(3, package.MessageCount);
    }

    [Fact]
    public void OwnerIsLabeledByNameOthersByNumericId()
    {
        var zipPath = CreateExport();
        using var source = InspectionSource.Open(zipPath);

        var package = _ingestor.Ingest(source);
        var thread = package.Threads[0];

        // El titular figura por su nombre; el corresponsal por su id numérico.
        Assert.Contains("Yo Titular", thread.Participants);
        Assert.Contains("200", thread.Participants);
        Assert.Equal("Conversación con 200", thread.Title);
    }

    [Fact]
    public void ParsesIso8601TimestampsAsUtc()
    {
        var zipPath = CreateExport();
        using var source = InspectionSource.Open(zipPath);

        var package = _ingestor.Ingest(source);
        var first = package.Threads[0].Messages[0];

        Assert.Equal(new DateTime(2024, 6, 25, 14, 3, 0, DateTimeKind.Utc), first.Timestamp!.Value.ToUniversalTime());
        Assert.Equal(new DateTime(2024, 6, 25, 14, 3, 0), package.DateRange.First);
        Assert.Equal(new DateTime(2024, 7, 1, 9, 0, 0), package.DateRange.Last);
    }

    [Fact]
    public void OtherParticipantResolvedRegardlessOfIdOrderInConversationId()
    {
        var zipPath = CreateExport();
        using var source = InspectionSource.Open(zipPath);

        var package = _ingestor.Ingest(source);

        // Segundo hilo: conversationId «300-100», el titular es 100 → corresponsal 300.
        Assert.Equal("Conversación con 300", package.Threads[1].Title);
    }

    [Fact]
    public void MediaFilePresentIsAttachedAndHashed()
    {
        var mediaBytes = new byte[] { 1, 2, 3, 4, 5 };
        var dm = """
            window.YTD.direct_messages.part0 = [
              {
                "dmConversation" : {
                  "conversationId" : "100-200",
                  "messages" : [
                    { "messageCreate" : { "senderId" : "200", "text" : "mirá esto", "mediaUrls" : [ "https://ton.twitter.com/dm/5-99/x.jpg" ], "id" : "5", "createdAt" : "2024-06-25T14:03:00.000Z" } }
                  ]
                }
              }
            ]
            """;
        var zipPath = CreateExport(dm, new Dictionary<string, byte[]>
        {
            ["data/direct_messages_media/5-99.jpg"] = mediaBytes,
        });
        using var source = InspectionSource.Open(zipPath);

        var package = _ingestor.Ingest(source);
        var attachment = package.Attachments.Single();

        Assert.Equal("5-99.jpg", attachment.Name);
        Assert.True(attachment.IsPresent);
        Assert.Equal(mediaBytes.Length, attachment.Size);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(mediaBytes)).ToLowerInvariant(), attachment.Sha256);
    }

    [Fact]
    public void MediaReferencedButMissingIsMarkedNotPresent()
    {
        var dm = """
            window.YTD.direct_messages.part0 = [
              {
                "dmConversation" : {
                  "conversationId" : "100-200",
                  "messages" : [
                    { "messageCreate" : { "senderId" : "200", "text" : "foto", "mediaUrls" : [ "https://ton.twitter.com/dm/7-88/foto.jpg" ], "id" : "7", "createdAt" : "2024-06-25T14:03:00.000Z" } }
                  ]
                }
              }
            ]
            """;
        // No se incluye el archivo local: adjunto referenciado pero ausente.
        var zipPath = CreateExport(dm);
        using var source = InspectionSource.Open(zipPath);

        var package = _ingestor.Ingest(source);
        var attachment = package.Attachments.Single();

        Assert.False(attachment.IsPresent);
        Assert.Null(attachment.Sha256);
    }

    [Fact]
    public void ReactionEventsBecomeSystemMessages()
    {
        var dm = """
            window.YTD.direct_messages.part0 = [
              {
                "dmConversation" : {
                  "conversationId" : "100-200",
                  "messages" : [
                    { "messageCreate" : { "senderId" : "200", "text" : "hola", "mediaUrls" : [], "id" : "1", "createdAt" : "2024-06-25T14:03:00.000Z" } },
                    { "reactionCreate" : { "senderId" : "100", "reactionKey" : "like", "eventId" : "2", "createdAt" : "2024-06-25T14:04:00.000Z" } }
                  ]
                }
              }
            ]
            """;
        var zipPath = CreateExport(dm);
        using var source = InspectionSource.Open(zipPath);

        var package = _ingestor.Ingest(source);
        var messages = package.Threads[0].Messages;

        Assert.False(messages[0].IsSystemMessage);
        Assert.True(messages[1].IsSystemMessage);
        Assert.Null(messages[1].Sender);
    }
}
