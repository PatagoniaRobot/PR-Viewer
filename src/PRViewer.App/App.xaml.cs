using System.Windows;
using PRViewer.App.ViewModels;

namespace PRViewer.App;

/// <summary>Punto de entrada del visor (Capa 2).</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        window.Show();

        // Apertura directa: PRViewer.App.exe "ruta\al\paquete.zip"
        if (e.Args.Length > 0 && window.DataContext is MainViewModel viewModel)
            viewModel.LoadPathFireAndForget(e.Args[0]);
    }
}
