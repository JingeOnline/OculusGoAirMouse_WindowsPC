using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Interop;

namespace OculusGoAirMouse;

public partial class MainWindow : Window
{
    private Settings settings = new();
    private readonly MouseOutput mouseOutput = new();
    private readonly EngineWorker engine;
    private readonly ControllerConnection connection = new();
    private readonly Dictionary<PropertyInfo, FrameworkElement> editors = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch elapsed = new();
    private bool connecting, closing, closeReady;
    public MainWindow()
    {
        engine = new(mouseOutput);
        InitializeComponent();
        Title = "Oculus Go Air Mouse · 0.2.2";
        SourceInitialized += (_, _) => { mouseOutput.SetMainWindowHandle(new WindowInteropHelper(this).Handle); mouseOutput.RefreshCaptionRegions(); };
        LocationChanged += (_, _) => mouseOutput.RefreshCaptionRegions();
        SizeChanged += (_, _) => mouseOutput.RefreshCaptionRegions();
        DpiChanged += (_, _) => mouseOutput.RefreshCaptionRegions();
        StateChanged += (_, _) => { mouseOutput.RefreshCaptionRegions(); InputDiagnostics.Capture("window state=" + WindowState); };
        mouseOutput.CaptionCommandPosted += command =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            string action = command switch { 0xF020 => "最小化", 0xF030 => "最大化", 0xF120 => "还原", _ => "关闭" };
            _ = Dispatcher.BeginInvoke(new Action(() => Log("控制器标题栏兼容处理：" + action)), DispatcherPriority.Background);
        };
        Log("版本 0.2.2：已启用蓝牙会话保持和短暂掉线确认。");
        InputDiagnostics.Write("start version=0.2.2");
        try { settings = SettingsStore.Load(); if (!System.IO.File.Exists(SettingsStore.FilePath)) SettingsStore.Save(settings); }
        catch (Exception ex) { SettingsMessage.Text = "配置读取失败，暂用迁移默认值。原配置未覆盖：" + ex.Message; }
        Loaded += async (_, _) =>
        {
            mouseOutput.RefreshCaptionRegions();
            try { await engine.ApplyAsync(settings); }
            catch (Exception ex) { Log(ex.Message); }
        };
        ConfigPath.Text = "配置文件：" + SettingsStore.FilePath;
        BuildEditors();
        connection.Progress += message => { ConnectionStatus.Text = message; Log(message); };
        connection.Frame += engine.PostFrame;
        engine.Faulted += ex => PostStop("鼠标控制已停止：" + ex.Message);
        connection.Disconnected += () => PostStop("控制器已断开，请唤醒后重新连接。", () => !connection.Connected);
        connection.LinkInterrupted += () => HandleLinkChange(false);
        connection.LinkRecovered += () => HandleLinkChange(true);
        timer.Tick += (_, _) =>
        {
            InputDiagnostics.UiTick();
            var snapshot = engine.Snapshot;
            EngineStatus.Text = connecting ? $"正在等待蓝牙响应，已用时 {elapsed.Elapsed.TotalSeconds:F0} 秒…" : connection.Connected ? snapshot.Status : "鼠标控制已停止";
            MouseButton.IsEnabled = connection.Connected && snapshot.Calibrated && !connecting;
            CalibrateButton.IsEnabled = connection.Connected && !connecting;
            MouseButton.Content = snapshot.Enabled ? "关闭鼠标" : "开启鼠标";
        };
        timer.Start();
    }
    private void Log(string message) { LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {message}\n"); LogBox.ScrollToEnd(); }
    private void HandleLinkChange(bool recovered)
    {
        // This runs on the Bluetooth callback thread, so safety does not depend
        // on whether the window is minimized or currently pumping UI messages.
        Task safety = recovered ? engine.StartSessionAsync() : engine.StopSessionAsync();
        _ = safety.ContinueWith(task =>
        {
            if (task.IsFaulted) InputDiagnostics.Write("link safety error=" + task.Exception!.GetBaseException().Message);
        }, TaskScheduler.Default);
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (closing) return;
            string message = recovered ? "蓝牙已恢复，请静置重新校准，再开启鼠标。" : "蓝牙短暂中断，已释放按键，正在等待恢复…";
            ConnectionStatus.Text = message; Log(message);
        }), DispatcherPriority.Background);
    }
    private void PostStop(string message, Func<bool>? stillValid = null)
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (closing || (stillValid != null && !stillValid())) return;
            await StopAsync(message); ConnectionStatus.Text = message; Log(message);
        }), DispatcherPriority.Background);
    }
    private async void ConnectClick(object sender, RoutedEventArgs e)
    {
        connecting = true; ConnectButton.IsEnabled = false; DisconnectButton.IsEnabled = true; elapsed.Restart();
        try { await engine.StartSessionAsync(); await connection.ConnectAsync(settings.Copy()); }
        catch (TimeoutException) { ConnectionStatus.Text = "未发现控制器，请唤醒后重试。"; Log(ConnectionStatus.Text); }
        catch (OperationCanceledException) { ConnectionStatus.Text = closing ? "已关闭" : "连接已取消或超时，请唤醒控制器后重试。"; Log(ConnectionStatus.Text); }
        catch (Exception ex) { ConnectionStatus.Text = "连接失败：" + ex.Message; Log(ConnectionStatus.Text); }
        finally
        {
            if (!connection.Connected && !closing) await engine.StopSessionAsync();
            connecting = false; elapsed.Stop(); ConnectButton.IsEnabled = !connection.Connected; DisconnectButton.IsEnabled = connection.Connected;
        }
    }
    private async Task StopAsync(string reason = "用户主动断开")
    {
        InputDiagnostics.Write("app stop reason=" + reason);
        var release = engine.StopSessionAsync();
        connection.Disconnect();
        try { await release; } catch (Exception ex) { Log("释放按键失败：" + ex.Message); }
        ConnectButton.IsEnabled = !connecting; DisconnectButton.IsEnabled = false;
    }
    private async void DisconnectClick(object sender, RoutedEventArgs e) { await StopAsync(); ConnectionStatus.Text = "已断开"; }
    private async void MouseClick(object sender, RoutedEventArgs e)
    {
        try { await engine.ToggleAsync(); }
        catch (Exception ex) { Log(ex.Message); }
    }
    private async void CalibrateClick(object sender, RoutedEventArgs e) { try { await engine.CalibrateAsync(); } catch (Exception ex) { Log(ex.Message); } }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeReady) return;
        e.Cancel = true;
        if (closing) return;
        closing = true; timer.Stop();
        try { await StopAsync("窗口关闭"); await engine.DisposeAsync(); }
        catch (Exception ex) { Log(ex.Message); }
        closeReady = true; Close();
    }
    private void BuildEditors()
    {
        (string Property, string Label)[] fields = [
            ("DeviceName", "控制器名称"), ("SensitivityX", "水平灵敏度"), ("SensitivityY", "垂直灵敏度"),
            ("DeadZone", "陀螺仪死区"), ("XAxis", "水平轴映射（0=X，1=Y，2=Z）"), ("YAxis", "垂直轴映射（0=X，1=Y，2=Z）"),
            ("XInvert", "水平光标反转"), ("YInvert", "垂直光标反转"), ("Smoothing", "光标平滑系数（越大响应越快）"),
            ("MaxMove", "单次最大光标位移（像素）"), ("CalibrationSamples", "校准样本数"), ("ButtonTimeout", "按键数据超时（秒）"),
            ("ScrollUnitsPerNotch", "滚动阈值（越小滚动越快）"), ("ScrollVerticalInvert", "垂直滚动反转"), ("ScrollHorizontalInvert", "水平滚动反转"),
            ("ScanTimeoutSeconds", "扫描超时（秒）"), ("ConnectTimeoutSeconds", "连接超时（秒）")];
        SettingsPanel.Children.Clear(); editors.Clear();
        foreach (var field in fields)
        {
            var property = typeof(Settings).GetProperty(field.Property)!;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            row.Children.Add(new TextBlock { Text = field.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0) });
            FrameworkElement editor = property.PropertyType == typeof(bool)
                ? new CheckBox { IsChecked = (bool)property.GetValue(settings)!, VerticalAlignment = VerticalAlignment.Center }
                : new TextBox { Text = Convert.ToString(property.GetValue(settings), CultureInfo.CurrentCulture) };
            Grid.SetColumn(editor, 1); row.Children.Add(editor); editors.Add(property, editor); SettingsPanel.Children.Add(row);
        }
    }
    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var candidate = settings.Copy();
            foreach (var (property, editor) in editors)
            {
                object value;
                if (editor is CheckBox check) value = check.IsChecked == true;
                else
                {
                    var text = ((TextBox)editor).Text.Trim();
                    if (property.PropertyType == typeof(string)) value = text;
                    else if (property.PropertyType == typeof(int)) value = int.Parse(text, CultureInfo.CurrentCulture);
                    else value = double.Parse(text, NumberStyles.Float, CultureInfo.CurrentCulture);
                }
                property.SetValue(candidate, value);
            }
            candidate.Validate(); SettingsStore.Save(candidate); settings = candidate; await engine.ApplyAsync(settings);
            SettingsMessage.Text = "已保存并应用。鼠标已暂停；连接时请静置完成重新校准。"; Log("设置已保存。");
        }
        catch (Exception ex) { SettingsMessage.Text = "保存失败：" + ex.Message; }
    }
    private void ReloadClick(object sender, RoutedEventArgs e) { BuildEditors(); SettingsMessage.Text = "已恢复当前生效的设置。"; }
}
