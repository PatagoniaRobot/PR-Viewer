using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;
using PRViewer.App.Services;
using PRViewer.App.ViewModels.Previews;
using PRViewer.Core.Ingestion;
using PRViewer.Core.Model;
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
    private IngestedConversation? _conversation;
    private SourceEntry? _chatEntry;
    private ChatPreviewViewModel? _chatPreview;
    private Dictionary<string, AttachmentInfo>? _attachmentsByName;

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

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string? SourcePath { get => _sourcePath; private set => SetProperty(ref _sourcePath, value); }
    public string PackageSha256 { get => _packageSha256; private set => SetProperty(ref _packageSha256, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

    public object? SelectedPreview { get => _selectedPreview; private set => SetProperty(ref _selectedPreview, value); }

    public bool HasConversation => _conversation is not null;
    public bool HasSource => _source is not null;

    // Resumen de la conversación para la franja superior.
    public string PlatformText => _conversation?.Platform.ToString() ?? "—";
    public string ParticipantsText => _conversation is { } c ? string.Join(", ", c.Participants) : "—";
    public string DateRangeText => _conversation is { DateRange.HasValue: true } c
        ? $"{c.DateRange.First:dd/MM/yyyy} → {c.DateRange.Last:dd/MM/yyyy}"
        : "—";
    public string MessageCountText => _conversation?.MessageCount.ToString() ?? "—";
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
            if (SetProperty(ref _selectedNode, value) && value?.Entry is { } entry)
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
                SelectedPreview = value.Entry is { } entry
                    ? CreatePreview(entry)
                    : new MissingAttachmentPreviewViewModel(value.Attachment);
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
                IngestedConversation? ingested = null;
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

    private void ResetState()
    {
        SelectedPreview = null;
        _selectedNode = null;
        _selectedAttachment = null;
        _chatPreview = null;
        _chatEntry = null;
        _attachmentsByName = null;
        _conversation = null;
        PackageSha256 = "—";
        _source?.Dispose();
        _source = null;
        RootNodes = Array.Empty<EntryNodeViewModel>();
        Attachments = Array.Empty<AttachmentItemViewModel>();
        RaiseAllStateChanged();
    }

    private void BuildViewState()
    {
        if (_source is null)
            return;

        RootNodes = EntryNodeViewModel.BuildTree(_source.Entries);

        if (_conversation is { } conversation)
        {
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
        RaisePropertyChanged(nameof(HasConversation));
        RaisePropertyChanged(nameof(HasSource));
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
            return _chatPreview ??= new ChatPreviewViewModel(_source, chatEntry, conversation.Messages);
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
            FileKind.Docx => new DocxPreviewViewModel(_source, entry, knownSha256),
            _ => new FileInfoPreviewViewModel(_source, entry, knownSha256),
        };
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
        _source?.Dispose();
        _source = null;
    }
}
