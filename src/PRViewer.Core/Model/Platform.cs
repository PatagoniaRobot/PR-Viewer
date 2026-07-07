namespace PRViewer.Core.Model;

/// <summary>
/// Plataforma de origen del paquete de exportación.
/// Se detecta siempre por contenido, nunca por extensión de archivo.
/// </summary>
public enum Platform
{
    /// <summary>Plataforma no reconocida por ningún ingestor registrado.</summary>
    Unknown = 0,

    /// <summary>Export de WhatsApp («Exportar chat»: _chat.txt + media).</summary>
    WhatsApp,

    /// <summary>Instagram vía «Descargar tu información» de Meta (HTML o JSON, elegible al descargar).</summary>
    MetaInstagram,

    /// <summary>Facebook/Messenger vía «Descargar tu información» de Meta (HTML o JSON, elegible al descargar).</summary>
    MetaFacebook,

    /// <summary>Export de X/Twitter («Descargar un archivo de tus datos»: data/direct-messages.js).</summary>
    TwitterX,

    /// <summary>Export de TikTok («Descargar tus datos»: TXT o JSON, elegible al descargar).</summary>
    TikTok,

    /// <summary>Export de Telegram Desktop (JSON o HTML).</summary>
    Telegram,

    /// <summary>Export de Snapchat (JSON, esquema propio).</summary>
    Snapchat,
}
