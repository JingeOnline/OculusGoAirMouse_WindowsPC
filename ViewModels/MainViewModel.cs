using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;

namespace OculusGoAirMouse.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IControllerRuntime runtime;
    private readonly IUiScheduler ui;
    private readonly Stopwatch elapsed = new();
    private Settings current;
    private IDisposable? ticker;
    private bool initialized;
    private int stopGeneration;
    private Task? shutdown;
    private readonly Queue<string> logLines = new();
    public string Title => "Oculus Go Air Mouse · 0.3.9";
    public SettingsViewModel Settings { get; }
    [ObservableProperty] private string connectionStatus = "未连接";
    [ObservableProperty] private string engineStatus = "连接后可点击重新校准或长按 Oculus 1 秒。";
    [ObservableProperty] private string mouseButtonText = "开启鼠标";
    [ObservableProperty] private string logText = "";
    [ObservableProperty] private bool isConnecting;
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private bool isCalibrated;
    [ObservableProperty] private bool isMouseEnabled;
    [ObservableProperty] private bool isRecovering;
    [ObservableProperty] private bool isClosing;

    public MainViewModel(IControllerRuntime runtime, ISettingsStore store, IUiScheduler ui)
    {
        this.runtime = runtime; this.ui = ui;
        string? loadError = null;
        try { current = store.Load(); if (!store.Exists) store.Save(current); }
        catch (Exception ex) { current = new(); loadError = "配置读取失败，暂用迁移默认值。原配置未覆盖：" + ex.Message; }
        Settings = new(current, store, ApplySettingsAsync);
        if (loadError != null) Settings.Message = loadError;
        runtime.Progress += OnProgress; runtime.Faulted += OnFault;
        runtime.Disconnected += OnDisconnected; runtime.LinkChanged += OnLinkChanged;
        Log("版本 0.3.9：支持可配置的按下与松开光标锁定。");
    }
    partial void OnIsConnectingChanged(bool value) => RefreshCommands();
    partial void OnIsConnectedChanged(bool value) => RefreshCommands();
    partial void OnIsCalibratedChanged(bool value) => RefreshCommands();
    partial void OnIsMouseEnabledChanged(bool value) => RefreshCommands();
    partial void OnIsRecoveringChanged(bool value) => RefreshCommands();
    partial void OnIsClosingChanged(bool value) => RefreshCommands();
    private bool CanConnect() => !IsClosing && !IsConnecting && !IsConnected && !IsRecovering;
    private bool CanDisconnect() => !IsClosing && (IsConnecting || IsConnected || IsRecovering);
    private bool CanToggle() => !IsClosing && !IsConnecting && IsConnected;
    private bool CanCalibrate() => !IsClosing && !IsConnecting && IsConnected;
    private void RefreshCommands()
    {
        ConnectCommand.NotifyCanExecuteChanged(); DisconnectCommand.NotifyCanExecuteChanged();
        ToggleMouseCommand.NotifyCanExecuteChanged(); CalibrateCommand.NotifyCanExecuteChanged();
    }
    public async Task InitializeAsync()
    {
        if (initialized) return;
        initialized = true;
        try { await runtime.ApplyAsync(current); }
        catch (Exception ex) { Log(ex.Message); }
        if (IsClosing) return;
        ticker = ui.Every(TimeSpan.FromMilliseconds(100), Pulse); Pulse();
        if (current.AutoConnectOnStartup && CanConnect()) await ConnectCoreAsync(autoEnableMouse: true);
    }
    private void Pulse()
    {
        if (IsClosing) return;
        InputDiagnostics.UiTick();
        var snapshot = runtime.Snapshot;
        IsConnected = runtime.Connected; IsCalibrated = snapshot.Calibrated; IsMouseEnabled = snapshot.Enabled;
        EngineStatus = IsConnecting ? $"正在等待蓝牙响应，已用时 {elapsed.Elapsed.TotalSeconds:F0} 秒…" : IsConnected ? snapshot.Status : "鼠标控制已停止";
        MouseButtonText = snapshot.Enabled ? "关闭鼠标" : "开启鼠标";
    }
    private void Log(string message)
    {
        logLines.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
        while (logLines.Count > 500) logLines.Dequeue();
        LogText = string.Join(Environment.NewLine, logLines);
    }
    private void OnProgress(string message) => ui.Post(() =>
    {
        if (!IsClosing && (IsConnecting || IsRecovering || runtime.Connected)) { ConnectionStatus = message; Log(message); }
    });
    private void OnFault(Exception error) => PostStop("鼠标控制已停止：" + error.Message, false);
    private void OnDisconnected() => PostStop("控制器已断开，请唤醒后重新连接。", true);
    private void PostStop(string message, bool checkConnection) => ui.Post(async () =>
    {
        if (IsClosing || (checkConnection && runtime.Connected)) return;
        await StopAsync(message); ConnectionStatus = message; Log(message);
    });
    private void OnLinkChanged(bool recovered) => ui.Post(() =>
    {
        if (IsClosing) return;
        IsRecovering = !recovered;
        ConnectionStatus = recovered ? "蓝牙已恢复。" : "蓝牙短暂中断，已释放按键，正在等待恢复…";
        Log(ConnectionStatus); Pulse();
    });
    public void NotifyCaptionCommand(uint command) => ui.Post(() =>
    {
        if (IsClosing) return;
        string action = command switch { 0xF020 => "最小化", 0xF030 => "最大化", 0xF120 => "还原", _ => "关闭" };
        Log("控制器标题栏兼容处理：" + action);
    });
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync() => ConnectCoreAsync();
    private async Task ConnectCoreAsync(bool autoEnableMouse = false)
    {
        IsConnecting = true; elapsed.Restart();
        int generation = stopGeneration;
        try
        {
            await runtime.ConnectAsync(current.Copy());
            if (autoEnableMouse && current.AutoConnectOnStartup && !IsClosing && generation == stopGeneration && runtime.Connected && !runtime.Snapshot.Enabled)
            {
                await runtime.ToggleAsync();
                Log("启动自动连接成功，已开启鼠标。");
            }
        }
        catch (TimeoutException) { ConnectionStatus = "未发现控制器，请唤醒后重试。"; Log(ConnectionStatus); }
        catch (OperationCanceledException) { ConnectionStatus = "连接已取消或超时，请唤醒控制器后重试。"; Log(ConnectionStatus); }
        catch (Exception ex) { ConnectionStatus = "连接失败：" + ex.Message; Log(ConnectionStatus); }
        finally { IsConnecting = false; elapsed.Stop(); Pulse(); }
    }
    private async Task StopAsync(string reason)
    {
        stopGeneration++;
        try { await runtime.DisconnectAsync(reason); }
        catch (Exception ex) { Log("释放连接或按键失败：" + ex.Message); }
        IsRecovering = false; Pulse();
    }
    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync() { await StopAsync("用户主动断开"); ConnectionStatus = "已断开"; }
    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleMouseAsync()
    {
        try { await runtime.ToggleAsync(); Pulse(); }
        catch (Exception ex) { Log(ex.Message); }
    }
    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task CalibrateAsync()
    {
        try { await runtime.CalibrateAsync(); Pulse(); }
        catch (Exception ex) { Log(ex.Message); }
    }
    private async Task ApplySettingsAsync(Settings candidate)
    {
        current = candidate.Copy(); await runtime.ApplyAsync(current); Pulse(); Log("设置已保存。");
    }
    public Task ShutdownAsync() => shutdown ??= ShutdownCoreAsync();
    private async Task ShutdownCoreAsync()
    {
        IsClosing = true; stopGeneration++; Settings.IsAvailable = false;
        elapsed.Stop();
        ticker?.Dispose(); ticker = null;
        runtime.Progress -= OnProgress; runtime.Faulted -= OnFault;
        runtime.Disconnected -= OnDisconnected; runtime.LinkChanged -= OnLinkChanged;
        try { await runtime.DisposeAsync(); }
        catch (Exception ex) { Log("退出清理失败：" + ex.Message); }
    }
}
