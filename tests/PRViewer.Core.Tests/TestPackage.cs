using System.IO.Compression;
using System.Text;

namespace PRViewer.Core.Tests;

/// <summary>
/// Ayudante para armar paquetes de prueba (ZIP o carpeta) en un directorio
/// temporal propio que se elimina al disponer.
/// </summary>
public sealed class TestPackage : IDisposable
{
    public string RootPath { get; }

    public TestPackage()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "prviewer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    /// <summary>Crea un ZIP con las entradas indicadas (nombre → contenido).</summary>
    public string CreateZip(string zipName, IReadOnlyDictionary<string, byte[]> entries)
    {
        var zipPath = Path.Combine(RootPath, zipName);
        using var stream = new FileStream(zipPath, FileMode.CreateNew);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var entryStream = entry.Open();
            entryStream.Write(content);
        }

        return zipPath;
    }

    /// <summary>Crea una carpeta con los archivos indicados (nombre → contenido).</summary>
    public string CreateFolder(string folderName, IReadOnlyDictionary<string, byte[]> files)
    {
        var folderPath = Path.Combine(RootPath, folderName);
        Directory.CreateDirectory(folderPath);
        foreach (var (name, content) in files)
        {
            var filePath = Path.Combine(folderPath, name);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllBytes(filePath, content);
        }

        return folderPath;
    }

    /// <summary>Crea un archivo suelto con el contenido indicado.</summary>
    public string CreateFile(string fileName, byte[] content)
    {
        var filePath = Path.Combine(RootPath, fileName);
        File.WriteAllBytes(filePath, content);
        return filePath;
    }

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    public void Dispose()
    {
        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch (IOException)
        {
            // Limpieza best-effort: el SO purga el temp igualmente.
        }
    }
}
