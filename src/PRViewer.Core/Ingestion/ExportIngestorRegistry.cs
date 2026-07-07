using PRViewer.Core.Model;
using PRViewer.Core.Ingestion.Meta;
using PRViewer.Core.Ingestion.Telegram;
using PRViewer.Core.Ingestion.TikTok;
using PRViewer.Core.Ingestion.Twitter;
using PRViewer.Core.Ingestion.WhatsApp;
using PRViewer.Core.Sources;

namespace PRViewer.Core.Ingestion;

/// <summary>
/// Registro de ingestores. Recorre los ingestores registrados y detecta,
/// por contenido, cuál corresponde a la fuente.
/// </summary>
public sealed class ExportIngestorRegistry
{
    private readonly List<IExportIngestor> _ingestors = new();

    /// <summary>Registra un ingestor. El orden de registro define la prioridad de detección.</summary>
    public void Register(IExportIngestor ingestor) => _ingestors.Add(ingestor);

    /// <summary>Ingestores registrados, en orden de prioridad.</summary>
    public IReadOnlyList<IExportIngestor> Ingestors => _ingestors;

    /// <summary>
    /// Devuelve el primer ingestor que reconoce el contenido de la fuente,
    /// o null si ninguno la reconoce.
    /// </summary>
    public IExportIngestor? Detect(IInspectionSource source)
        => _ingestors.FirstOrDefault(i => i.CanIngest(source));

    /// <summary>
    /// Detecta la plataforma e ingesta en un solo paso.
    /// </summary>
    /// <exception cref="UnknownPlatformException">Si ningún ingestor reconoce la fuente.</exception>
    public IngestedPackage Ingest(IInspectionSource source)
    {
        var ingestor = Detect(source) ?? throw new UnknownPlatformException(source.DisplayName);
        return ingestor.Ingest(source);
    }

    /// <summary>
    /// Registro con los ingestores incluidos en la librería:
    /// X/Twitter, TikTok, Meta (Instagram y Facebook en HTML), Telegram y WhatsApp (≈90% del volumen).
    ///
    /// Orden = prioridad de detección: los ingestores con marcadores específicos van
    /// primero; WhatsApp (detector por formato de línea, el más laxo) queda de último
    /// como respaldo, para que no se quede con paquetes de otras plataformas.
    /// </summary>
    public static ExportIngestorRegistry CreateDefault()
    {
        var registry = new ExportIngestorRegistry();
        registry.Register(new TwitterDmIngestor());
        registry.Register(new TikTokTxtIngestor());
        registry.Register(new MetaInstagramHtmlIngestor());
        registry.Register(new MetaFacebookHtmlIngestor());
        registry.Register(new TelegramHtmlIngestor());
        registry.Register(new WhatsAppTxtIngestor());
        return registry;
    }
}
