namespace PRViewer.App.ViewModels;

/// <summary>
/// Estado global de visibilidad del contenido multimedia.
///
/// Decisión de diseño (casos sensibles en la DAFI): el material visual arranca
/// OCULTO. El perito decide cuándo revelarlo, con este interruptor global o
/// con el revelado individual de cada elemento. Ninguna superficie de la UI
/// renderiza píxeles del material sin esa acción explícita.
/// </summary>
public sealed class MediaVisibilityState : ViewModelBase
{
    private bool _showMedia;

    /// <summary>Interruptor global «mostrar multimedia». Por defecto, apagado.</summary>
    public bool ShowMedia
    {
        get => _showMedia;
        set => SetProperty(ref _showMedia, value);
    }
}
