namespace PRViewer.Core.Model;

/// <summary>
/// Abstracción común de salida del núcleo de inspección: un paquete de exportación
/// normalizado, compuesto por uno o más hilos de conversación.
///
/// El visor, el acta y el empaquetado (responsabilidad del consumidor) trabajan
/// sobre este modelo sin saber de qué plataforma provino el material. Además de
/// los hilos, expone agregados de paquete (participantes, rango temporal, mensajes
/// y adjuntos de todos los hilos) para la franja de resumen y el informe.
/// </summary>
public sealed class IngestedPackage
{
    /// <summary>Plataforma de origen detectada por contenido.</summary>
    public required Platform Platform { get; init; }

    /// <summary>Hilos de conversación del paquete, en el orden en que aparecen en el export.</summary>
    public required IReadOnlyList<ConversationThread> Threads { get; init; }

    /// <summary>Cantidad de hilos del paquete.</summary>
    public int ThreadCount => Threads.Count;

    // ── Agregados de paquete (superficie compatible con visor e informe) ──

    private IReadOnlyList<ChatMessage>? _messages;
    /// <summary>Todos los mensajes del paquete, concatenados hilo por hilo.</summary>
    public IReadOnlyList<ChatMessage> Messages
        => _messages ??= Threads.SelectMany(t => t.Messages).ToList();

    /// <summary>Cantidad total de mensajes del paquete (incluye mensajes de sistema).</summary>
    public int MessageCount => Threads.Sum(t => t.MessageCount);

    private IReadOnlyList<string>? _participants;
    /// <summary>Participantes distintos de todos los hilos, en orden de aparición.</summary>
    public IReadOnlyList<string> Participants
        => _participants ??= Threads.SelectMany(t => t.Participants).Distinct().ToList();

    private IReadOnlyList<AttachmentInfo>? _attachments;
    /// <summary>
    /// Adjuntos de todos los hilos, deduplicados por nombre (el primero gana).
    /// Los exports multi-hilo usan identificadores de archivo únicos, así que la
    /// colisión de nombres entre hilos es improbable; la deduplicación evita, aun así,
    /// contar dos veces un mismo archivo referenciado desde varios hilos.
    /// </summary>
    public IReadOnlyList<AttachmentInfo> Attachments
        => _attachments ??= DeduplicateAttachments();

    private DateRange? _dateRange;
    /// <summary>Rango temporal global: primera y última fecha de todo el paquete.</summary>
    public DateRange DateRange => _dateRange ??= ComputeDateRange();

    private IReadOnlyList<AttachmentInfo> DeduplicateAttachments()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AttachmentInfo>();
        foreach (var thread in Threads)
        {
            foreach (var attachment in thread.Attachments)
            {
                if (seen.Add(attachment.Name))
                    result.Add(attachment);
            }
        }

        return result;
    }

    private DateRange ComputeDateRange()
    {
        DateTime? first = null;
        DateTime? last = null;
        foreach (var thread in Threads)
        {
            if (thread.DateRange.First is { } f && (first is null || f < first))
                first = f;
            if (thread.DateRange.Last is { } l && (last is null || l > last))
                last = l;
        }

        return new DateRange(first, last);
    }
}
