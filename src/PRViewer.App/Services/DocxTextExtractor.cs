using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using PRViewer.Core.Sources;

namespace PRViewer.App.Services;

/// <summary>
/// Extrae el texto plano de un .docx ÍNTEGRAMENTE EN MEMORIA y sin dependencias:
/// un .docx es un ZIP con word/document.xml adentro; se lee con la BCL.
/// Es un preview de texto (sin diseño fiel), suficiente para confirmar que el
/// documento es el que pide el oficio. El archivo original no se toca.
/// </summary>
public static class DocxTextExtractor
{
    /// <summary>Devuelve el texto del documento, o null si la entrada no es un .docx legible.</summary>
    public static string? TryExtract(IInspectionSource source, SourceEntry entry, int maxChars)
    {
        try
        {
            using var input = source.OpenRead(entry);
            var buffer = new MemoryStream();
            input.CopyTo(buffer);
            buffer.Position = 0;

            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: false);
            var documentEntry = archive.GetEntry("word/document.xml");
            if (documentEntry is null)
                return null;

            var text = new StringBuilder();
            using var xmlStream = documentEntry.Open();
            using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings { IgnoreComments = true });

            // Se recorre por nombre local para no depender de prefijos de namespace:
            // w:t = texto, w:p = párrafo, w:br / w:cr = salto, w:tab = tabulación.
            while (reader.Read() && text.Length < maxChars)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.LocalName)
                    {
                        case "t":
                            text.Append(reader.ReadElementContentAsString());
                            break;
                        case "br":
                        case "cr":
                            text.AppendLine();
                            break;
                        case "tab":
                            text.Append('\t');
                            break;
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "p")
                {
                    text.AppendLine();
                }
            }

            if (text.Length >= maxChars)
                text.AppendLine().Append("… (truncado para el preview; el archivo original está intacto)");

            return text.ToString();
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException)
        {
            // ZIP corrupto o XML ilegible: no es un error del visor.
            return null;
        }
    }
}
