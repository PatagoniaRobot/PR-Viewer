# PR-Viewer

**Visor forense de solo lectura.** PR-Viewer nunca escribe, modifica, extrae sin control ni reescribe el material que inspecciona. Lee en streaming y muestra el contenido tal como llegó. Esta garantía es el corazón forense del componente y condiciona toda decisión de diseño.

Componente abierto de la familia **Patagonia Robot** · Licencia Apache 2.0

## Qué es

PR-Viewer es un visor forense de propósito general. Su primer caso de uso es la inspección de paquetes de exportación de plataformas de mensajería y redes sociales (WhatsApp, Meta, Telegram, Snapchat) antes de su ingesta forense: validar visualmente, sin alterar un byte, que el paquete recibido sirve (que no esté vacío, roto o incompleto) antes de hashear, empaquetar y labrar acta.

## Arquitectura

- **Capa 1 — `PRViewer.Core`** (este repositorio, disponible): núcleo de inspección reutilizable, sin UI. Abre el contenedor (ZIP, carpeta o archivo suelto) en solo lectura, lista las entradas, detecta la plataforma de origen **por contenido** (nunca por extensión) y normaliza a una abstracción común agnóstica de plataforma (`IngestedConversation`).
- **Capa 2 — Visor WPF** (pendiente): consume la Capa 1 y la presenta (árbol de entradas, preview de texto, JSON navegable, multimedia con miniaturas). Sin guardar, sin exportar, sin modificar.

### Patrón de ingesta

Registry + un `IExportIngestor` por plataforma. Nuevas plataformas se enchufan sin tocar el núcleo.

| Ingestor | Entrada | Estado |
|---|---|---|
| `WhatsAppTxtIngestor` | `_chat.txt` + media (ZIP de la app) | ✅ Implementado |
| `MetaJsonIngestor` | «Descargar mi información» (IG / Facebook) | Pendiente (esperando muestras reales) |
| `TelegramJsonIngestor` | Export de Telegram | Pendiente (esperando muestras reales) |
| `SnapchatIngestor` | Export de Snapchat | Pendiente (esperando muestras reales) |

## Uso

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
2. La lectura es en streaming, bajo demanda; no se extrae el contenido completo a disco.
3. No existe en ningún punto del código una ruta que reescriba, renombre o modifique el material de origen.
4. Hay tests que lo verifican: la ingesta completa de un paquete deja su hash SHA-256 y su fecha de modificación intactos (`ReadOnlyInvariantTests`).

## Alcance

Fuera del alcance de esta librería (responsabilidad de la app consumidora): cálculo de hash del paquete, empaquetado cifrado, generación de contraseñas, actas y subida a la nube. PR-Viewer trabaja sobre el export ya generado; nunca sobre el dispositivo.

## Stack

.NET 8 · sin dependencias externas · comentarios en español, identificadores en inglés.

```
dotnet build
dotnet test
```

## Licencia

Apache 2.0 — ver [LICENSE](LICENSE).
