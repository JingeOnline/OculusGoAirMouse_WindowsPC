using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using OculusGoAirMouse.Services;
using OculusGoAirMouse.ViewModels;

namespace OculusGoAirMouse;

// Only view responsibilities remain here: native geometry, scrolling and closing.
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private bool closing, closeReady;
    public MainWindow(MainViewModel viewModel, MouseOutput output)
    {
        this.viewModel = viewModel;
        InitializeComponent(); DataContext = viewModel;
        SourceInitialized += (_, _) => { output.SetMainWindowHandle(new WindowInteropHelper(this).Handle); output.RefreshCaptionRegions(); };
        LocationChanged += (_, _) => output.RefreshCaptionRegions();
        SizeChanged += (_, _) => output.RefreshCaptionRegions();
        DpiChanged += (_, _) => output.RefreshCaptionRegions();
        StateChanged += (_, _) => { output.RefreshCaptionRegions(); InputDiagnostics.Capture("window state=" + WindowState); };
        output.CaptionCommandPosted += viewModel.NotifyCaptionCommand;
        Loaded += async (_, _) => { output.RefreshCaptionRegions(); await viewModel.InitializeAsync(); };
        Closed += (_, _) => output.CaptionCommandPosted -= viewModel.NotifyCaptionCommand;
    }
    private void OnLogTextChanged(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeReady) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        await viewModel.ShutdownAsync();
        closeReady = true; Close();
    }
}
