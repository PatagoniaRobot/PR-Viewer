namespace PRViewer.Core.Model;

/// <summary>
/// Adjunto referenciado por la conversación, con los datos necesarios
/// para el acta y el empaquetado del consumidor.
/// </summary>
/// <param name="Name">Nombre del archivo tal como aparece en el export.</param>
/// <param name="ContentType">Tipo de contenido estimado (MIME), solo informativo para el visor.</param>
/// <param name="Size">Tamaño en bytes, o null si el archivo no está presente en el paquete.</param>
/// <param name="Sha256">Hash SHA-256 en hexadecimal minúscula, o null si el archivo no está presente.</param>
/// <param name="IsPresent">
/// Indica si el archivo referenciado existe realmente dentro del paquete.
/// Un adjunto mencionado en el chat pero ausente es señal de export incompleto,
/// exactamente lo que el perito necesita ver antes de labrar acta.
/// </param>
public sealed record AttachmentInfo(
    string Name,
    string ContentType,
    long? Size,
    string? Sha256,
    bool IsPresent);
