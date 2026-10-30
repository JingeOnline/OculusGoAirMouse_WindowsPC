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
        var viewModel = new MainViewModel(new ControllerRuntime(output), new JsonSettingsStore(), new WpfUiScheduler(Dispatcher));
        MainWindow = new MainWindow(viewModel, output);
        InputDiagnostics.Write("start version=0.3.0 MVVM");
        MainWindow.Show();
    }
}
