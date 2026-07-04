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

    /// <summary>Instagram vía «Descargar mi información» de Meta (JSON).</summary>
    MetaInstagram,

    /// <summary>Facebook vía «Descargar mi información» de Meta (JSON).</summary>
    MetaFacebook,

    /// <summary>Export de Telegram Desktop (JSON o HTML).</summary>
    Telegram,

    /// <summary>Export de Snapchat (JSON, esquema propio).</summary>
    Snapchat,
}
