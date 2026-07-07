namespace PRViewer.Core.Model;

/// <summary>
/// Un hilo de conversación individual dentro de un paquete de exportación.
///
/// WhatsApp exporta un único hilo por paquete; X, Meta (Instagram/Facebook) y
/// TikTok exportan muchos (uno por corresponsal o grupo). El hilo es la unidad
/// forense: cada uno se identifica, inspecciona y reporta por separado.
/// </summary>
public sealed class ConversationThread
{
    /// <summary>Nombre para mostrar del hilo (corresponsal, grupo o identificador del export).</summary>
    public required string Title { get; init; }

    /// <summary>Identidades normalizadas de los participantes del hilo, en orden de aparición.</summary>
    public required IReadOnlyList<string> Participants { get; init; }

    /// <summary>Primera y última fecha detectadas dentro del hilo.</summary>
    public required DateRange DateRange { get; init; }

    /// <summary>Adjuntos referenciados por el hilo, con nombre, tipo, tamaño y hash.</summary>
    public required IReadOnlyList<AttachmentInfo> Attachments { get; init; }

    /// <summary>Mensajes normalizados del hilo, en el orden en que aparecen en el export.</summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>Cantidad de mensajes del hilo (incluye mensajes de sistema).</summary>
    public int MessageCount => Messages.Count;
}
