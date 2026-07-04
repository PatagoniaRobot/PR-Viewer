namespace PRViewer.Core.Model;

/// <summary>
/// Mensaje individual normalizado, agnóstico de plataforma.
/// Alimenta el preview de texto de la Capa 2.
/// </summary>
/// <param name="Timestamp">Fecha y hora del mensaje, o null si no pudo parsearse.</param>
/// <param name="Sender">Remitente normalizado, o null en mensajes de sistema.</param>
/// <param name="Text">Texto del mensaje tal como llegó (sin reescritura).</param>
/// <param name="AttachmentName">Nombre del adjunto referenciado, si el mensaje lo tiene.</param>
/// <param name="IsSystemMessage">Mensajes generados por la plataforma (cifrado, alta de grupo, etc.).</param>
public sealed record ChatMessage(
    DateTime? Timestamp,
    string? Sender,
    string Text,
    string? AttachmentName,
    bool IsSystemMessage);
