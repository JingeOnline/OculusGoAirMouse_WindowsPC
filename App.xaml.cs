using System.Windows;
using Microsoft.Win32;
using OculusGoAirMouse.Services;
using OculusGoAirMouse.ViewModels;
namespace OculusGoAirMouse;
public partial class App : Application
{
    private WpfCalibrationOverlay? calibrationOverlay;
    private ControllerRuntime? controllerRuntime;
    private AdaptivePriorityService? priorityService;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var output = new MouseOutput();
        var runtime = controllerRuntime = new ControllerRuntime(output, new ControllerConnection());
        var viewModel = new MainViewModel(runtime, new JsonSettingsStore(), new WpfUiScheduler(Dispatcher));
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        calibrationOverlay = new WpfCalibrationOverlay(runtime, Dispatcher);
        MainWindow = new MainWindow(viewModel, output);
        InputDiagnostics.Write("start version=0.5.1 MVVM");
        try { priorityService = new AdaptivePriorityService(() => runtime.Connected && runtime.Snapshot.Enabled); }
        catch (Exception ex) { InputDiagnostics.Write("CPU priority monitor unavailable=" + ex.Message); }
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        MainWindow.Show();
    }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        var runtime = controllerRuntime;
        if (runtime == null) return;
        Task operation = e.Mode switch
        {
            PowerModes.Suspend => runtime.SuspendAsync(),
            PowerModes.Resume => runtime.ResumeAsync(),
            _ => Task.CompletedTask
        };
        _ = operation.ContinueWith(task =>
        {
            if (task.IsFaulted) InputDiagnostics.Write("power recovery error=" + task.Exception!.GetBaseException().Message);
        }, TaskScheduler.Default);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        controllerRuntime = null;
        calibrationOverlay?.Dispose();
        // Only background I/O remains here; shutdown never posts to the UI.
        try
        {
            try { priorityService?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            finally { InputDiagnostics.ShutdownAsync().GetAwaiter().GetResult(); }
        }
        finally { base.OnExit(e); }
    }
}
