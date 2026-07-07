using PRViewer.Core.Model;

namespace PRViewer.Core.Ingestion.Meta;

/// <summary>Ingestor de mensajes de Facebook/Messenger en HTML (export de Meta).</summary>
public sealed class MetaFacebookHtmlIngestor : MetaHtmlIngestor
{
    public override Platform Platform => Platform.MetaFacebook;
    protected override string InboxPrefix => "your_facebook_activity/messages/inbox/";
}
