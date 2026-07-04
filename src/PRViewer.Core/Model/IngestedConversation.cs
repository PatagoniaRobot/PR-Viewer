namespace PRViewer.Core.Model;

/// <summary>
/// Abstracción común de salida del núcleo de inspección.
/// El visor, el acta y el empaquetado (responsabilidad del consumidor)
/// trabajan sobre este modelo sin saber de qué plataforma provino el material.
/// </summary>
public sealed class IngestedConversation
{
    /// <summary>Plataforma de origen detectada por contenido.</summary>
    public required Platform Platform { get; init; }

    /// <summary>Identidades normalizadas de los participantes de la conversación.</summary>
    public required IReadOnlyList<string> Participants { get; init; }

    /// <summary>Primera y última fecha detectadas.</summary>
    public required DateRange DateRange { get; init; }

    /// <summary>Cantidad total de mensajes (incluye mensajes de sistema).</summary>
    public required int MessageCount { get; init; }

    /// <summary>Adjuntos referenciados, con nombre, tipo, tamaño y hash.</summary>
    public required IReadOnlyList<AttachmentInfo> Attachments { get; init; }

    /// <summary>Mensajes normalizados, en el orden en que aparecen en el export.</summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
}
