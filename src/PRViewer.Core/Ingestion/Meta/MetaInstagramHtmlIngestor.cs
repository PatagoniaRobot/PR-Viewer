using PRViewer.Core.Model;

namespace PRViewer.Core.Ingestion.Meta;

/// <summary>Ingestor de mensajes directos de Instagram en HTML (export de Meta).</summary>
public sealed class MetaInstagramHtmlIngestor : MetaHtmlIngestor
{
    public override Platform Platform => Platform.MetaInstagram;
    protected override string InboxPrefix => "your_instagram_activity/messages/inbox/";
}
