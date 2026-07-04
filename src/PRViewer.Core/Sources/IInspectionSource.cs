namespace PRViewer.Core.Sources;

/// <summary>
/// Fuente de inspección en solo lectura: ZIP, carpeta o archivo suelto.
///
/// INVARIANTE DE SOLO LECTURA (innegociable, a nivel de arquitectura):
/// ninguna implementación abre el material con permisos de escritura,
/// lo extrae a disco ni lo modifica de ninguna forma. La lectura es
/// en streaming, bajo demanda, entrada por entrada.
/// </summary>
public interface IInspectionSource : IDisposable
{
    /// <summary>Nombre para mostrar de la fuente (nombre del ZIP, carpeta o archivo).</summary>
    string DisplayName { get; }

    /// <summary>Listado plano de las entradas (solo archivos, no directorios).</summary>
    IReadOnlyList<SourceEntry> Entries { get; }

    /// <summary>
    /// Abre una entrada para lectura en streaming.
    /// El stream devuelto es de solo lectura; el llamador debe disponerlo.
    /// </summary>
    Stream OpenRead(SourceEntry entry);
}
