namespace PRViewer.Core.Model;

/// <summary>
/// Rango temporal detectado en la conversación: primera y última fecha.
/// Ambos extremos pueden ser nulos si el material no contiene fechas parseables.
/// </summary>
public readonly record struct DateRange(DateTime? First, DateTime? Last)
{
    /// <summary>Indica si se detectó al menos una fecha.</summary>
    public bool HasValue => First.HasValue;
}
