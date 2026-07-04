using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PRViewer.Core.Reporting;

namespace PRViewer.App;

/// <summary>
/// Diálogo de configuración del informe de inspección. Junta los datos del
/// caso (todos opcionales), el destino y los formatos; la generación queda
/// a cargo del view model principal vía Capa 1.
/// </summary>
public partial class ReportDialog : Window
{
    // Formatos de fecha aceptados en el campo de recepción, día-primero (locale argentino).
    private static readonly string[] DateFormats = { "yyyy-MM-dd", "d/M/yyyy", "d-M-yyyy" };

    public ReportDialog()
    {
        InitializeComponent();
        DestinationBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    /// <summary>Datos del caso cargados, o null si el perito no completó ninguno.</summary>
    public ReportCaseInfo? CaseInfo { get; private set; }

    public string DestinationDirectory { get; private set; } = "";
    public bool GenerateHtml { get; private set; }
    public bool GenerateTxt { get; private set; }

    private void OnBrowseDestination(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Carpeta destino del informe" };
        if (dialog.ShowDialog() == true)
            DestinationBox.Text = dialog.FolderName;
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        var destination = DestinationBox.Text.Trim();
        if (!Directory.Exists(destination))
        {
            ShowValidation("La carpeta destino no existe. Elegila con «Examinar…».");
            return;
        }

        if (HtmlCheck.IsChecked != true && TxtCheck.IsChecked != true)
        {
            ShowValidation("Elegí al menos un formato de informe.");
            return;
        }

        DateTime? receivedDate = null;
        var rawDate = ReceivedDateBox.Text.Trim();
        if (rawDate.Length > 0)
        {
            if (!DateTime.TryParseExact(rawDate, DateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                ShowValidation("La fecha de recepción no se entiende. Usá el formato AAAA-MM-DD (o DD/MM/AAAA).");
                return;
            }
            receivedDate = parsed;
        }

        var caseInfo = new ReportCaseInfo
        {
            CaseNumber = CaseNumberBox.Text.Trim(),
            CaseName = CaseNameBox.Text.Trim(),
            CourtName = CourtNameBox.Text.Trim(),
            ProsecutorOffice = ProsecutorOfficeBox.Text.Trim(),
            CaseFileNumber = CaseFileNumberBox.Text.Trim(),
            ExaminerName = ExaminerNameBox.Text.Trim(),
            ExaminerBadge = ExaminerBadgeBox.Text.Trim(),
            ExaminerDivision = ExaminerDivisionBox.Text.Trim(),
            ExaminerRole = ExaminerRoleBox.Text.Trim(),
            ReceivedFrom = ReceivedFromBox.Text.Trim(),
            ReceivedDate = receivedDate,
            ReceivedActNumber = ReceivedActNumberBox.Text.Trim(),
        };

        CaseInfo = caseInfo.HasAnyValue ? caseInfo : null;
        DestinationDirectory = destination;
        GenerateHtml = HtmlCheck.IsChecked == true;
        GenerateTxt = TxtCheck.IsChecked == true;
        DialogResult = true;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }
}
