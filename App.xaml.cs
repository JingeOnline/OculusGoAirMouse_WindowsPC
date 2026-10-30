using System.Windows;
using OculusGoAirMouse.Services;
using OculusGoAirMouse.ViewModels;
namespace OculusGoAirMouse;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var output = new MouseOutput();
        var viewModel = new MainViewModel(new ControllerRuntime(output, new ControllerConnection()), new JsonSettingsStore(), new WpfUiScheduler(Dispatcher));
        MainWindow = new MainWindow(viewModel, output);
        InputDiagnostics.Write("start version=0.3.3 MVVM");
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        // Only background I/O remains here; shutdown never posts to the UI.
        try { InputDiagnostics.ShutdownAsync().GetAwaiter().GetResult(); }
        finally { base.OnExit(e); }
    }
}
