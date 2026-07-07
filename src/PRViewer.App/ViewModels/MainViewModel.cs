using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;
using PRViewer.App.Services;
using PRViewer.App.ViewModels.Previews;
using PRViewer.Core.Extraction;
using PRViewer.Core.Ingestion;
using PRViewer.Core.Model;
using PRViewer.Core.Reporting;
using PRViewer.Core.Sources;

namespace PRViewer.App.ViewModels;

/// <summary>
/// View model principal del visor. Orquesta la apertura de la fuente (siempre
/// en solo lectura, vía Capa 1), la ingesta, el árbol de entradas, la galería
/// multimedia y el preview de la entrada seleccionada.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly ExportIngestorRegistry _registry = ExportIngestorRegistry.CreateDefault();

    private IInspectionSource? _source;
    private IngestedPackage? _conversation;
    private SourceEntry? _chatEntry;
    private ChatPreviewViewModel? _chatPreview;
    private ThreadItemViewModel? _selectedThread;
    private Dictionary<string, AttachmentInfo>? _attachmentsByName;

    private SourceEntry? _extractableEntry;
    private string _statusText = "Abrí un paquete de exportación para inspeccionarlo. El material nunca se modifica.";
    private string? _sourcePath;
    private string _packageSha256 = "—";
    private object? _selectedPreview;
    private EntryNodeViewModel? _selectedNode;
    private AttachmentItemViewModel? _selectedAttachment;
    private bool _isLoading;

    public MainViewModel()
    {
        OpenFileCommand = new RelayCommand(_ => OpenFile());
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());
    }

    public MediaVisibilityState MediaVisibility { get; } = new();

    public RelayCommand OpenFileCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public IReadOnlyList<EntryNodeViewModel> RootNodes { get; private set; } = Array.Empty<EntryNodeViewModel>();
    public IReadOnlyList<AttachmentItemViewModel> Attachments { get; private set; } = Array.Empty<AttachmentItemViewModel>();
    public IReadOnlyList<ThreadItemViewModel> Threads { get; private set; } = Array.Empty<ThreadItemViewModel>();

    /// <summary>Hay más de un hilo: se muestra la pestaña de conversaciones.</summary>
    public bool HasThreads => Threads.Count > 0;

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string? SourcePath { get => _sourcePath; private set => SetProperty(ref _sourcePath, value); }
    public string PackageSha256 { get => _packageSha256; private set => SetProperty(ref _packageSha256, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

    public object? SelectedPreview
    {
        get => _selectedPreview;
        private set
        {
            if (ReferenceEquals(_selectedPreview, value))
                return;

            // El preview saliente puede tener recursos vivos (p. ej. audio sonando).
            (_selectedPreview as IDisposable)?.Dispose();
            _selectedPreview = value;
            RaisePropertyChanged();
        }
    }

    public bool HasConversation => _conversation is not null;
    public bool HasSource => _source is not null;
    public bool CanGenerateReport => _conversation is not null && _source is not null;
    public bool CanExtractSelected => _extractableEntry is not null && _source is not null;

    /// <summary>Nombre de la entrada seleccionada para extraer (para la UI y la confirmación).</summary>
    public string? ExtractableEntryName => _extractableEntry?.Name;

    // Resumen de la conversación para la franja superior.
    public string PlatformText => _conversation?.Platform.ToString() ?? "—";
    public string ParticipantsText => _conversation is { } c ? string.Join(", ", c.Participants) : "—";
    public string DateRangeText => _conversation is { DateRange.HasValue: true } c
        ? $"{c.DateRange.First:dd/MM/yyyy} → {c.DateRange.Last:dd/MM/yyyy}"
        : "—";
    public string MessageCountText => _conversation?.MessageCount.ToString() ?? "—";
    public string ThreadCountText => _conversation?.ThreadCount.ToString() ?? "—";
    public string AttachmentSummaryText
    {
        get
        {
            if (_conversation is not { } c)
                return "—";
            var present = c.Attachments.Count(a => a.IsPresent);
            var missing = c.Attachments.Count - present;
            return missing == 0
                ? $"{present} presentes"
                : $"{present} presentes, {missing} AUSENTES";
        }
    }
    public bool HasMissingAttachments => _conversation is { } c && c.Attachments.Any(a => !a.IsPresent);

    /// <summary>Nodo seleccionado en el árbol; dispara el preview correspondiente.</summary>
    public EntryNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value))
                return;

            SetExtractableEntry(value?.Entry);
            if (value?.Entry is { } entry)
                SelectedPreview = CreatePreview(entry);
        }
    }

    /// <summary>Adjunto seleccionado en la galería; comparte el panel de preview.</summary>
    public AttachmentItemViewModel? SelectedAttachment
    {
        get => _selectedAttachment;
        set
        {
            if (SetProperty(ref _selectedAttachment, value) && value is not null)
            {
                SetExtractableEntry(value.Entry);
                SelectedPreview = value.Entry is { } entry
                    ? CreatePreview(entry)
                    : new MissingAttachmentPreviewViewModel(value.Attachment);
            }
        }
    }

    /// <summary>Hilo seleccionado en la pestaña «Conversaciones»; muestra sus mensajes parseados.</summary>
    public ThreadItemViewModel? SelectedThread
    {
        get => _selectedThread;
        set
        {
            if (SetProperty(ref _selectedThread, value) && value is not null)
            {
                // Un hilo no es una entrada del paquete: no hay extracción directa desde acá.
                SetExtractableEntry(null);
                SelectedPreview = new ChatPreviewViewModel(value.Thread.Messages);
            }
        }
    }

    /// <summary>Apertura directa (argumento de línea de comandos).</summary>
    public async void LoadPathFireAndForget(string path)
    {
        try
        {
            await LoadSourceAsync(path);
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo abrir «{path}»: {ex.Message}";
        }
    }

    private async void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Abrir paquete de exportación (solo lectura)",
            Filter = "Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await LoadSourceAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo abrir «{dialog.FileName}»: {ex.Message}";
        }
    }

    private async void OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Abrir carpeta de exportación (solo lectura)" };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await LoadSourceAsync(dialog.FolderName);
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo abrir «{dialog.FolderName}»: {ex.Message}";
        }
    }

    private async Task LoadSourceAsync(string path)
    {
        IsLoading = true;
        StatusText = $"Abriendo «{Path.GetFileName(path)}» en solo lectura…";
        ResetState();

        try
        {
            // Apertura e ingesta fuera del hilo de UI; toda la lectura pasa por la Capa 1.
            var (source, conversation) = await Task.Run(() =>
            {
                var opened = InspectionSource.Open(path);
                IngestedPackage? ingested = null;
                var ingestor = _registry.Detect(opened);
                if (ingestor is not null)
                    ingested = ingestor.Ingest(opened);
                return (opened, ingested);
            });

            _source = source;
            _conversation = conversation;
            SourcePath = path;

            BuildViewState();

            StatusText = conversation is null
                ? "Plataforma no reconocida: se muestra el árbol de entradas igual (inspección genérica)."
                : $"Conversación de {conversation.Platform} reconocida. El material permanece intacto.";

            if (File.Exists(path))
                _ = ComputePackageHashAsync(path);
            else
                PackageSha256 = "(carpeta: hash por archivo en cada entrada)";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetExtractableEntry(SourceEntry? entry)
    {
        _extractableEntry = entry;
        RaisePropertyChanged(nameof(CanExtractSelected));
        RaisePropertyChanged(nameof(ExtractableEntryName));
    }

    private void ResetState()
    {
        SelectedPreview = null;
        _selectedNode = null;
        _selectedAttachment = null;
        _selectedThread = null;
        _extractableEntry = null;
        _chatPreview = null;
        _chatEntry = null;
        _attachmentsByName = null;
        _conversation = null;
        PackageSha256 = "—";
        _source?.Dispose();
        _source = null;
        RootNodes = Array.Empty<EntryNodeViewModel>();
        Attachments = Array.Empty<AttachmentItemViewModel>();
        Threads = Array.Empty<ThreadItemViewModel>();
        RaiseAllStateChanged();
    }

    private void BuildViewState()
    {
        if (_source is null)
            return;

        RootNodes = EntryNodeViewModel.BuildTree(_source.Entries);

        if (_conversation is { } conversation)
        {
            Threads = conversation.Threads.Select(t => new ThreadItemViewModel(t)).ToList();

            _attachmentsByName = new Dictionary<string, AttachmentInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var attachment in conversation.Attachments)
                _attachmentsByName.TryAdd(attachment.Name, attachment);

            var entriesByName = new Dictionary<string, SourceEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _source.Entries)
                entriesByName.TryAdd(entry.Name, entry);

            Attachments = conversation.Attachments
                .Select(a => new AttachmentItemViewModel(
                    _source, a,
                    entriesByName.TryGetValue(a.Name, out var entry) ? entry : null,
                    MediaVisibility))
                .ToList();

            // Entrada del chat: el .txt que la ingesta reconoció (heurística coherente
            // con el ingestor: prioriza _chat.txt, luego el primer .txt).
            _chatEntry = _source.Entries
                .Where(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Name.Equals("_chat.txt", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }

        RaiseAllStateChanged();
    }

    private void RaiseAllStateChanged()
    {
        RaisePropertyChanged(nameof(RootNodes));
        RaisePropertyChanged(nameof(Attachments));
        RaisePropertyChanged(nameof(Threads));
        RaisePropertyChanged(nameof(HasThreads));
        RaisePropertyChanged(nameof(ThreadCountText));
        RaisePropertyChanged(nameof(HasConversation));
        RaisePropertyChanged(nameof(HasSource));
        RaisePropertyChanged(nameof(CanGenerateReport));
        RaisePropertyChanged(nameof(CanExtractSelected));
        RaisePropertyChanged(nameof(ExtractableEntryName));
        RaisePropertyChanged(nameof(PlatformText));
        RaisePropertyChanged(nameof(ParticipantsText));
        RaisePropertyChanged(nameof(DateRangeText));
        RaisePropertyChanged(nameof(MessageCountText));
        RaisePropertyChanged(nameof(AttachmentSummaryText));
        RaisePropertyChanged(nameof(HasMissingAttachments));
        RaisePropertyChanged(nameof(SelectedNode));
        RaisePropertyChanged(nameof(SelectedAttachment));
    }

    private object CreatePreview(SourceEntry entry)
    {
        if (_source is null)
            throw new InvalidOperationException("No hay fuente abierta.");

        // El chat reconocido se muestra parseado (con pestaña de texto crudo).
        if (_conversation is { } conversation && _chatEntry is { } chatEntry
            && entry.Path.Equals(chatEntry.Path, StringComparison.OrdinalIgnoreCase))
        {
            return _chatPreview ??= new ChatPreviewViewModel(conversation.Messages, _source, chatEntry);
        }

        var knownSha256 = _attachmentsByName is { } map && map.TryGetValue(entry.Name, out var attachment)
            ? attachment.Sha256
            : null;

        return FileKindResolver.Resolve(entry.Name) switch
        {
            FileKind.Json => new JsonPreviewViewModel(_source, entry),
            FileKind.Text => new TextPreviewViewModel(_source, entry),
            FileKind.Image => new ImagePreviewViewModel(_source, entry, MediaVisibility, knownSha256),
            FileKind.Pdf => new PdfPreviewViewModel(_source, entry, MediaVisibility, knownSha256),
            FileKind.Audio => new AudioPreviewViewModel(_source, entry, knownSha256),
            FileKind.Docx => new DocxPreviewViewModel(_source, entry, knownSha256),
            _ => new FileInfoPreviewViewModel(_source, entry, knownSha256),
        };
    }

    /// <summary>
    /// Genera el informe técnico de inspección vía Capa 1. Solo lectura sobre el
    /// paquete: la única escritura es el informe, en el destino elegido por el perito.
    /// </summary>
    public async Task GenerateReportAsync(ReportCaseInfo? caseInfo, string destinationDirectory,
        bool generateHtml, bool generateTxt)
    {
        if (_source is not { } source || _conversation is not { } conversation || _sourcePath is not { } path)
        {
            StatusText = "No hay una conversación reconocida para informar.";
            return;
        }

        IsLoading = true;
        StatusText = "Generando informe de inspección…";

        try
        {
            var result = await Task.Run(() =>
                InspectionReportGenerator.Generate(new InspectionReportRequest
                {
                    Package = BuildPackageIdentity(source, path),
                    Conversation = conversation,
                    Source = source,
                    DestinationDirectory = destinationDirectory,
                    CaseInfo = caseInfo,
                    GenerateHtml = generateHtml,
                    GenerateTxt = generateTxt,
                }));

            var generated = new[] { result.HtmlPath, result.TxtPath }
                .Where(p => p is not null)
                .Select(p => Path.GetFileName(p)!);
            StatusText = $"✓ Informe generado en «{destinationDirectory}»: {string.Join(", ", generated)}. El paquete permanece intacto.";
        }
        catch (Exception ex)
        {
            StatusText = $"✗ No se pudo generar el informe: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Extrae la entrada seleccionada como operación controlada (Enmienda E1.3.b):
    /// copia verificada por SHA-256 contra el paquete y constancia automática.
    /// </summary>
    public async Task ExtractSelectedEntryAsync(string destinationDirectory)
    {
        if (_source is not { } source || _extractableEntry is not { } entry || _sourcePath is not { } path)
        {
            StatusText = "No hay una entrada seleccionada para extraer.";
            return;
        }

        // Hashes que la ingesta ya observó, para la doble verificación.
        Dictionary<string, string>? knownHashes = null;
        if (_attachmentsByName is { } map)
        {
            knownHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, attachment) in map)
            {
                if (attachment.Sha256 is { } sha256)
                    knownHashes[name] = sha256;
            }
        }

        IsLoading = true;
        StatusText = $"Extrayendo «{entry.Name}» con verificación de hash…";

        try
        {
            var result = await Task.Run(() => ControlledExtractionService.Extract(new ExtractionRequest
            {
                Package = BuildPackageIdentity(source, path),
                Source = source,
                Entries = new[] { entry },
                DestinationDirectory = destinationDirectory,
                KnownHashes = knownHashes,
            }));

            var record = result.Entries[0];
            StatusText = record.Verified
                ? $"✓ «{record.ExportedAs}» extraída y VERIFICADA por SHA-256 en «{destinationDirectory}». " +
                  $"Constancia: {Path.GetFileName(result.ManifestPath)}. El paquete permanece intacto."
                : $"✗ Extracción FALLIDA de «{entry.Name}»: {record.Error} " +
                  $"Constancia del intento: {Path.GetFileName(result.ManifestPath)}.";
        }
        catch (Exception ex)
        {
            StatusText = $"✗ No se pudo extraer: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Identificación del paquete con hash fresco del contenedor (solo lectura).
    /// Corre en el thread pool; las carpetas no tienen hash único.
    /// </summary>
    private static PackageIdentity BuildPackageIdentity(IInspectionSource source, string path)
    {
        string? packageSha256 = null;
        long? sizeBytes = null;
        DateTime? lastModifiedUtc = null;
        if (File.Exists(path))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            packageSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var info = new FileInfo(path);
            sizeBytes = info.Length;
            lastModifiedUtc = info.LastWriteTimeUtc;
        }

        var kind = source switch
        {
            ZipInspectionSource => PackageKind.Zip,
            FolderInspectionSource => PackageKind.Folder,
            _ => PackageKind.File,
        };

        return new PackageIdentity(source.DisplayName, path, kind, packageSha256, sizeBytes, lastModifiedUtc);
    }

    private async Task ComputePackageHashAsync(string path)
    {
        try
        {
            var hash = await Task.Run(() =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            });
            PackageSha256 = hash;
        }
        catch (IOException)
        {
            PackageSha256 = "no disponible";
        }
    }

    public void Dispose()
    {
        SelectedPreview = null; // libera recursos del preview (p. ej. audio)
        _source?.Dispose();
        _source = null;
    }
}
