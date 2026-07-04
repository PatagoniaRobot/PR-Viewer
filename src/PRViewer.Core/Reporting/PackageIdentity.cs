namespace PRViewer.Core.Reporting;

/// <summary>Tipo de contenedor del paquete inspeccionado.</summary>
public enum PackageKind
{
    /// <summary>Archivo ZIP (detectado por firma).</summary>
    Zip,

    /// <summary>Carpeta de archivos sueltos.</summary>
    Folder,

    /// <summary>Archivo suelto (p. ej. un _chat.txt sin media).</summary>
    File,
}

/// <summary>
/// Identificación del paquete inspeccionado, tal como fue recibido.
/// El hash y la fecha de modificación son del contenedor completo; para
/// carpetas no hay hash único (el informe lo documenta por entrada).
/// </summary>
/// <param name="DisplayName">Nombre para mostrar (nombre del ZIP, carpeta o archivo).</param>
/// <param name="FullPath">Ruta completa del paquete en el equipo del perito.</param>
/// <param name="Kind">Tipo de contenedor.</param>
/// <param name="Sha256">SHA-256 hexadecimal del contenedor, o null si es carpeta.</param>
/// <param name="SizeBytes">Tamaño del contenedor en bytes, o null si es carpeta.</param>
/// <param name="LastModifiedUtc">Fecha de última modificación del contenedor (UTC), si se conoce.</param>
public sealed record PackageIdentity(
    string DisplayName,
    string FullPath,
    PackageKind Kind,
    string? Sha256,
    long? SizeBytes,
    DateTime? LastModifiedUtc);
