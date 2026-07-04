namespace PRViewer.Core.Ingestion;

/// <summary>
/// Ningún ingestor registrado reconoció el contenido de la fuente.
/// </summary>
public sealed class UnknownPlatformException : Exception
{
    public UnknownPlatformException(string sourceDisplayName)
        : base($"Ningún ingestor registrado reconoce el contenido de «{sourceDisplayName}».")
    {
    }
}
