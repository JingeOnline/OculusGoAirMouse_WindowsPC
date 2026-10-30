using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;
using OculusGoAirMouse.ViewModels;

static class ViewModelChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        var runtime = new FakeRuntime(); var store = new FakeStore(); var ui = new FakeUi();
        var vm = new MainViewModel(runtime, store, ui);
        Check(vm.ConnectCommand.CanExecute(null) && !vm.DisconnectCommand.CanExecute(null) && !vm.ToggleMouseCommand.CanExecute(null), "Initial command availability");
        Check(vm.Settings.Fields.Count == 20 && vm.Settings.Fields[1].TextValue == 1.75.ToString(), "Bound settings migration");
        Check(!vm.Settings.Fields[18].BooleanValue, "Auto calibration checkbox must default off");
        await vm.InitializeAsync();
        Check(runtime.Applied!.DeadZone == 65 && runtime.ApplyCount == 1, "Initialization applies saved settings");
        await vm.InitializeAsync(); Check(runtime.ApplyCount == 1, "Duplicate initialization");
        var changed = new HashSet<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");
        await vm.ConnectCommand.ExecuteAsync(null); ui.Drain();
        runtime.EmitProgress("连接中"); ui.Drain();
        Check(vm.ConnectionStatus == "连接中" && changed.Contains("ConnectionStatus"), "Observable status binding");
        Check(vm.IsConnected && !vm.ConnectCommand.CanExecute(null) && vm.DisconnectCommand.CanExecute(null), "Connected command availability");
        Check(vm.ToggleMouseCommand.CanExecute(null), "Uncalibrated connection must allow toggling mouse");
        runtime.Snapshot = new(false, true, "已校准"); ui.Tick();
        Check(vm.ToggleMouseCommand.CanExecute(null) && vm.CalibrateCommand.CanExecute(null), "Calibrated command availability");
        await vm.ToggleMouseCommand.ExecuteAsync(null);
        Check(runtime.Snapshot.Enabled && vm.MouseButtonText == "关闭鼠标", "Toggle command binding");
        await vm.CalibrateCommand.ExecuteAsync(null);
        Check(!vm.IsCalibrated && runtime.Snapshot.Enabled && vm.ToggleMouseCommand.CanExecute(null), "Recalibration must preserve enable and permit stopping");
        vm.Settings.Fields[1].TextValue = "2.25";
        vm.Settings.Fields[18].BooleanValue = true;
        vm.Settings.Fields[10].TextValue = .2.ToString();
        await vm.Settings.SaveCommand.ExecuteAsync(null);
        Check(store.Value.SensitivityX == 2.25 && runtime.Applied!.SensitivityX == 2.25, "Save/apply ViewModel command");
        Check(store.Value.CalibrationDurationSeconds == .2 && runtime.Applied!.CalibrationDurationSeconds == .2, "Duration not saved/applied from settings page");
        Check(store.Value.AutoCalibrateOnConnect && runtime.Applied!.AutoCalibrateOnConnect, "Auto calibration setting was not saved/applied");
        int saves = store.Saves;
        vm.Settings.Fields[10].TextValue = .09.ToString();
        await vm.Settings.SaveCommand.ExecuteAsync(null);
        Check(store.Saves == saves && vm.Settings.Message.Contains("0.1–3"), "Settings page accepted out-of-range duration");
        vm.Settings.ReloadCommand.Execute(null);
        vm.Settings.Fields[3].TextValue = "invalid";
        await vm.Settings.SaveCommand.ExecuteAsync(null);
        Check(store.Saves == saves && vm.Settings.Message.Contains("陀螺仪死区"), "Invalid draft changed saved settings");
        vm.Settings.ReloadCommand.Execute(null);
        Check(vm.Settings.Fields[3].TextValue == "65" && vm.Settings.Fields[1].TextValue == "2.25", "Reload draft does not reset saved settings");
        vm.Settings.Fields[3].TextValue = "-1";
        await vm.Settings.SaveCommand.ExecuteAsync(null);
        Check(store.Saves == saves && vm.Settings.Message.Contains("保存失败"), "Range validation bypassed");
        vm.Settings.ReloadCommand.Execute(null);
        store.FailSave = true; vm.Settings.Fields[1].TextValue = "3";
        await vm.Settings.SaveCommand.ExecuteAsync(null);
        Check(runtime.Applied!.SensitivityX == 2.25 && vm.Settings.Message.Contains("disk"), "Failed persistence changed running settings");
        store.FailSave = false;
        runtime.Connected = false; runtime.EmitLink(false); ui.Drain();
        Check(vm.IsRecovering && !vm.ConnectCommand.CanExecute(null) && vm.DisconnectCommand.CanExecute(null), "Recovery prematurely permits new connection");
        runtime.Connected = true; runtime.EmitLink(true); ui.Drain();
        Check(!vm.IsRecovering && vm.IsConnected && vm.ConnectionStatus.Contains("恢复"), "Recovery status binding");
        runtime.EmitDisconnected(); ui.Drain();
        Check(runtime.Disconnects == 0, "Stale disconnection event tore down recovered session");
        runtime.Connected = false; runtime.EmitDisconnected(); ui.Drain();
        Check(runtime.Disconnects == 1 && vm.ConnectCommand.CanExecute(null), "Confirmed disconnect command state");
        await vm.ConnectCommand.ExecuteAsync(null);
        Check(runtime.LastConnectionSettings!.SensitivityX == 2.25, "Reconnect used stale settings");
        await vm.DisconnectCommand.ExecuteAsync(null);
        Check(!vm.IsConnected && vm.ConnectionStatus == "已断开", "Manual disconnect command");
        runtime.ThrowOnConnect = true;
        await vm.ConnectCommand.ExecuteAsync(null);
        Check(vm.ConnectionStatus.Contains("测试错误") && !vm.IsConnecting && vm.ConnectCommand.CanExecute(null), "Connection exception left commands disabled");
        runtime.EmitProgress("stale progress"); ui.Drain();
        Check(vm.ConnectionStatus.Contains("测试错误"), "Stale progress overwrote connection error");
        runtime.EmitFault(new Exception("测试输入异常")); ui.Drain();
        Check(vm.ConnectionStatus.Contains("测试输入异常") && !vm.IsConnected, "Input failure did not stop runtime");
        await vm.ShutdownAsync(); await vm.ShutdownAsync();
        Check(runtime.Disposals == 1 && ui.Disposed && !vm.Settings.SaveCommand.CanExecute(null) && !vm.ConnectCommand.CanExecute(null), "Shutdown cleanup and command states");
        string previous = vm.ConnectionStatus; runtime.EmitProgress("late"); ui.Drain();
        Check(vm.ConnectionStatus == previous, "Disposed ViewModel retained callbacks");
        var badStore = new FakeStore { FailLoad = true };
        var badVm = new MainViewModel(new FakeRuntime(), badStore, new FakeUi());
        Check(badStore.Saves == 0 && badVm.Settings.Message.Contains("未覆盖"), "Corrupt configuration overwritten during construction");
        await badVm.ShutdownAsync();
        Check(new Settings().AutoConnectOnStartup, "Startup connection must default on");
        foreach (bool enabled in new[] { true, false })
        {
            var startupRuntime = new FakeRuntime();
            var startupStore = new FakeStore { Value = new Settings { AutoConnectOnStartup = enabled } };
            var startupVm = new MainViewModel(startupRuntime, startupStore, new FakeUi());
            await startupVm.InitializeAsync(); await startupVm.InitializeAsync();
            Check(startupRuntime.Connects == (enabled ? 1 : 0) && startupRuntime.Toggles == (enabled ? 1 : 0), "Startup option did not control exactly one connect/enable sequence");
            Check(startupVm.IsMouseEnabled == enabled && startupRuntime.Snapshot.Enabled == enabled, "Startup mouse state not reflected in UI");
            await startupVm.ShutdownAsync();
        }
        var failedRuntime = new FakeRuntime { ThrowOnConnect = true };
        var failedVm = new MainViewModel(failedRuntime, new FakeStore { Value = new Settings() }, new FakeUi());
        await failedVm.InitializeAsync();
        Check(failedRuntime.Toggles == 0 && failedVm.ConnectCommand.CanExecute(null), "Startup failure enabled mouse or prevented manual retry");
        await failedVm.ShutdownAsync();
        foreach (bool closing in new[] { true, false })
        {
            var gatedRuntime = new FakeRuntime { ConnectGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var gatedVm = new MainViewModel(gatedRuntime, new FakeStore { Value = new Settings() }, new FakeUi());
            Task startup = gatedVm.InitializeAsync();
            if (closing) await gatedVm.ShutdownAsync(); else await gatedVm.DisconnectCommand.ExecuteAsync(null);
            gatedRuntime.ConnectGate.SetResult(); await startup;
            Check(gatedRuntime.Toggles == 0, "Cancelled/closing startup enabled mouse after delayed connect");
            await gatedVm.ShutdownAsync();
        }
        return count;
    }
    private sealed class FakeStore : ISettingsStore
    {
        public Settings Value = new() { AutoConnectOnStartup = false }; public int Saves; public bool FailSave, FailLoad;
        public string FilePath => "test-settings.json";
        public bool Exists => true;
        public Settings Load() => FailLoad ? throw new Exception("corrupt") : Value.Copy();
        public void Save(Settings settings) { if (FailSave) throw new Exception("disk"); Saves++; Value = settings.Copy(); }
    }
    private sealed class FakeUi : IUiScheduler
    {
        private readonly Queue<Action> pending = new(); private Action? tick;
        public bool Disposed;
        public void Post(Action action) => pending.Enqueue(action);
        public void Drain() { while (pending.TryDequeue(out var action)) action(); }
        public void Tick() => tick?.Invoke();
        public IDisposable Every(TimeSpan interval, Action action) { tick = action; return new Registration(() => { Disposed = true; tick = null; }); }
        private sealed class Registration(Action stop) : IDisposable { public void Dispose() => stop(); }
    }
    private sealed class FakeRuntime : IControllerRuntime
    {
        public EngineSnapshot Snapshot { get; set; } = new(false, false, "校准中");
        public bool Connected { get; set; }
        public Settings? Applied, LastConnectionSettings;
        public int ApplyCount, Disconnects, Disposals, Connects, Toggles;
        public TaskCompletionSource? ConnectGate;
        public bool ThrowOnConnect;
        public event Action<string>? Progress;
        public event Action<Exception>? Faulted;
        public event Action? Disconnected;
        public event Action<bool>? LinkChanged;
        public void EmitProgress(string text) => Progress?.Invoke(text);
        public void EmitLink(bool value) => LinkChanged?.Invoke(value);
        public void EmitDisconnected() => Disconnected?.Invoke();
        public void EmitFault(Exception error) => Faulted?.Invoke(error);
        public Task ApplyAsync(Settings settings) { Applied = settings.Copy(); ApplyCount++; Snapshot = new(false, false, "校准中"); return Task.CompletedTask; }
        public async Task ConnectAsync(Settings settings) { Connects++; if (ThrowOnConnect) throw new Exception("测试错误"); LastConnectionSettings = settings.Copy(); if (ConnectGate != null) await ConnectGate.Task; Connected = true; }
        public Task DisconnectAsync(string reason) { Connected = false; Disconnects++; Snapshot = new(false, false, "已停止"); return Task.CompletedTask; }
        public Task ToggleAsync() { Toggles++; Snapshot = Snapshot with { Enabled = !Snapshot.Enabled }; return Task.CompletedTask; }
        public Task CalibrateAsync() { Snapshot = new(Snapshot.Enabled, false, "校准中"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Connected = false; Disposals++; Faulted = null; return ValueTask.CompletedTask; }
    }
}
