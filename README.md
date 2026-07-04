# PR-Viewer

**Visor forense de solo lectura.** PR-Viewer nunca escribe, modifica, extrae sin control ni reescribe el material que inspecciona. Lee en streaming y muestra el contenido tal como llegó. Esta garantía es el corazón forense del componente y condiciona toda decisión de diseño.

Componente abierto de la familia **Patagonia Robot** · Licencia Apache 2.0

## Qué es

PR-Viewer es un visor forense de propósito general. Su primer caso de uso es la inspección de paquetes de exportación de plataformas de mensajería y redes sociales (WhatsApp, Meta, Telegram, Snapchat) antes de su ingesta forense: validar visualmente, sin alterar un byte, que el paquete recibido sirve (que no esté vacío, roto o incompleto) antes de hashear, empaquetar y labrar acta.

## Arquitectura

- **Capa 1 — `PRViewer.Core`**: núcleo de inspección reutilizable, sin UI y sin dependencias externas. Abre el contenedor (ZIP, carpeta o archivo suelto) en solo lectura, lista las entradas, detecta la plataforma de origen **por contenido** (nunca por extensión) y normaliza a una abstracción común agnóstica de plataforma (`IngestedConversation`). Incluye además el generador de informes de inspección y la extracción controlada (ver «Operaciones de salida»).
- **Capa 2 — `PRViewer.App`**: visor WPF que consume la Capa 1. Árbol de entradas, galería multimedia, preview por tipo: chat parseado (con pestaña de texto crudo), JSON navegable, imágenes decodificadas en memoria, PDF renderizado en memoria con la API del propio Windows, texto de `.docx` vía BCL, y reproducción de notas de voz Ogg/Opus con motor propio sobre `waveOut` — **todo procesado íntegramente en memoria, jamás por archivo temporal**. Soporta apertura por línea de comandos: `PRViewer.App.exe "ruta\al\paquete.zip"`.

### Privacidad en el visor

Pensado para material sensible: todo contenido visual (imágenes, miniaturas, PDF) arranca **oculto** con candado y se revela solo por decisión explícita del perito (toggle global «Mostrar multimedia» o botón por elemento). El audio se reproduce únicamente por acción explícita.

### Patrón de ingesta

Registry + un `IExportIngestor` por plataforma. Nuevas plataformas se enchufan sin tocar el núcleo.

| Ingestor | Entrada | Estado |
|---|---|---|
| `WhatsAppTxtIngestor` | `_chat.txt` + media (ZIP de la app) | ✅ Implementado (validado con exports reales Android) |
| `MetaJsonIngestor` | «Descargar tu información» (IG / Facebook / Threads) | Pendiente (esperando muestras reales) |
| `TelegramJsonIngestor` | Export de Telegram Desktop | Pendiente (esperando muestras reales) |
| `SnapchatIngestor` | Export de Snapchat | Pendiente (esperando muestras reales) |

## Uso de la Capa 1

```csharp
using PRViewer.Core.Ingestion;
using PRViewer.Core.Sources;

// Abre ZIP, carpeta o archivo suelto — siempre en solo lectura.
using var source = InspectionSource.Open(@"C:\ruta\al\export.zip");

// Detecta la plataforma por contenido y normaliza.
var registry = ExportIngestorRegistry.CreateDefault();
var conversation = registry.Ingest(source);

Console.WriteLine($"Plataforma:    {conversation.Platform}");
Console.WriteLine($"Participantes: {string.Join(", ", conversation.Participants)}");
Console.WriteLine($"Mensajes:      {conversation.MessageCount}");
Console.WriteLine($"Rango:         {conversation.DateRange.First} — {conversation.DateRange.Last}");

foreach (var attachment in conversation.Attachments)
{
    // IsPresent == false ⇒ adjunto referenciado en el chat pero ausente del paquete
    // (señal de export incompleto, visible antes de labrar acta).
    Console.WriteLine($"{attachment.Name}  {attachment.Size}  {attachment.Sha256}  presente={attachment.IsPresent}");
}
```

## El invariante de solo lectura

Se trata como invariante de arquitectura, no como detalle de UI:

1. La apertura del contenedor es siempre en modo lectura (`FileAccess.Read`); nunca se abre con permisos de escritura.
2. La lectura es en streaming, bajo demanda; el contenido (imágenes, PDF, audio) se procesa en memoria, nunca por archivo temporal.
3. No existe en ningún punto del código una ruta que reescriba, renombre o modifique el material de origen.
4. Hay tests que lo verifican: la ingesta, el informe y la extracción dejan el hash SHA-256 y la fecha de modificación del paquete intactos (`ReadOnlyInvariantTests`, `InspectionReportGeneratorTests`, `ControlledExtractionTests`).

## Operaciones de salida (controladas)

El invariante protege el material de origen; sobre él, PR-Viewer produce exactamente dos salidas nuevas, ambas regidas por el documento maestro (Enmienda Nº 1):

- **Informe técnico de inspección** (`PRViewer.Core.Reporting`): HTML autocontenido (imprimible a PDF desde el navegador) y/o texto plano. Contiene exclusivamente **metadatos, estadísticas y hashes** — nunca el contenido de los mensajes ni material multimedia (la transcripción corresponde al acta de la aplicación consumidora). Datos del caso opcionales, destino elegido por el operador, jamás sobrescribe.
- **Extracción controlada** (`PRViewer.Core.Extraction`): copia entradas del paquete a un destino elegido por el operador, con **verificación SHA-256 de la copia contra la entrada dentro del paquete** (y contra el hash observado en la ingesta); toda discrepancia descarta la copia. Cada extracción genera una **constancia automática** con hashes, destino y timestamp UTC. Nunca sobrescribe y rechaza destinos dentro del material inspeccionado.

## Alcance

Fuera del alcance de esta librería (responsabilidad de la app consumidora): hash pericial de recepción, empaquetado cifrado, generación de contraseñas, actas y subida a la nube. PR-Viewer trabaja sobre el export ya generado; nunca sobre el dispositivo.

## Stack

.NET 8 · comentarios en español, identificadores en inglés.

- **`PRViewer.Core` (Capa 1): cero dependencias externas.** Solo BCL.
- **`PRViewer.App` (Capa 2, WPF)**: BCL + APIs del propio Windows (`Windows.Data.Pdf`, `waveOut`), y dos paquetes para la decodificación de audio Opus: [Concentus](https://github.com/lostromb/concentus) (BSD-3-Clause) y [Concentus.Oggfile](https://github.com/lostromb/concentus.oggfile) (MIT). Ver atribución abajo.

```
dotnet build
dotnet test
```

## Atribución de terceros

La reproducción de notas de voz usa **Concentus** © Logan Stromberg y colaboradores (port de los códecs Opus/SILK/CELT, licencia BSD-3-Clause) y **Concentus.Oggfile** © Logan Stromberg (lectura de contenedor Ogg, licencia MIT). Ambas licencias son compatibles con Apache 2.0 y sus textos se conservan en los paquetes NuGet correspondientes. El resto del proyecto no tiene dependencias externas.

## Licencia

Apache 2.0 — ver [LICENSE](LICENSE).
