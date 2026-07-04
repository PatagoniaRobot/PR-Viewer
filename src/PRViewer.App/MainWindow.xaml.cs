using System.ComponentModel;
using System.Windows;
using PRViewer.App.ViewModels;

namespace PRViewer.App;

/// <summary>Ventana principal del visor. MVVM sin DI: el view model se crea acá.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    /// <summary>
    /// TreeView no permite enlazar SelectedItem en dos vías; este es el único
    /// puente de code-behind hacia el view model.
    /// </summary>
    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => _viewModel.SelectedNode = e.NewValue as EntryNodeViewModel;

    /// <summary>Abre el diálogo del informe y delega la generación en el view model.</summary>
    private async void OnGenerateReport(object sender, RoutedEventArgs e)
    {
        var dialog = new ReportDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        await _viewModel.GenerateReportAsync(
            dialog.CaseInfo, dialog.DestinationDirectory, dialog.GenerateHtml, dialog.GenerateTxt);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        _viewModel.Dispose();
    }
}
