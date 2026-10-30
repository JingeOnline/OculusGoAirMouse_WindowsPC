using System.Windows;
using OculusGoAirMouse.Services;
using OculusGoAirMouse.ViewModels;
namespace OculusGoAirMouse;
public partial class App : Application
{
    private WpfCalibrationOverlay? calibrationOverlay;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var output = new MouseOutput();
        var runtime = new ControllerRuntime(output, new ControllerConnection());
        var viewModel = new MainViewModel(runtime, new JsonSettingsStore(), new WpfUiScheduler(Dispatcher));
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        calibrationOverlay = new WpfCalibrationOverlay(runtime, Dispatcher);
        MainWindow = new MainWindow(viewModel, output);
        InputDiagnostics.Write("start version=0.3.5 MVVM");
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        calibrationOverlay?.Dispose();
        // Only background I/O remains here; shutdown never posts to the UI.
        try { InputDiagnostics.ShutdownAsync().GetAwaiter().GetResult(); }
        finally { base.OnExit(e); }
    }
}
