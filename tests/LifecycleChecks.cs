using System.Collections.Concurrent;
using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;

static class LifecycleChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        var mouse = new RecordingMouse(); var connection = new FakeConnection();
        var runtime = new ControllerRuntime(mouse, connection);
        await runtime.ApplyAsync(new Settings { AutoCalibrateOnConnect = true, CalibrationDurationSeconds = .1 });
        await runtime.ConnectAsync(new Settings { AutoCalibrateOnConnect = true });
        Check(connection.Subscribers == 5 && runtime.Connected, "Runtime event wiring");
        connection.Emit(new byte[20], true);
        await Task.Delay(120);
        for (int i = 0; i < 10; i++) connection.Emit(new byte[20], true);
        await runtime.ToggleAsync();
        var down = new byte[20]; down[19] = 4; connection.Emit(down, false);
        Check(mouse.Down.Wait(1000), "Drag not started");
        Task first = runtime.DisposeAsync().AsTask(), second = runtime.DisposeAsync().AsTask();
        Check(ReferenceEquals(first, second), "Dispose not shared between callers");
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        Check(mouse.Up.IsSet && !runtime.InputThreadAlive, "Shutdown left button pressed or input thread alive");
        Check(connection.Subscribers == 0 && connection.NotificationStops == 1 && connection.Disconnects == 1, "Shutdown retained callbacks or notifications");
        Check(!runtime.Connected && !runtime.Snapshot.Enabled, "Disposed runtime still active");
        int events = mouse.Calls;
        connection.Emit(down, false); await Task.Delay(30);
        Check(mouse.Calls == events, "Native callback injected input after shutdown");
        try { await runtime.ToggleAsync(); throw new Exception("Disposed runtime accepted command"); }
        catch (ObjectDisposedException) { count++; }

        connection = new FakeConnection { BlockConnect = true };
        runtime = new(new RecordingMouse(), connection);
        Task pending = runtime.ConnectAsync(new());
        await connection.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        try { await pending; throw new Exception("In-flight connect was not cancelled"); }
        catch (OperationCanceledException) { count++; }
        Check(!runtime.InputThreadAlive && connection.Subscribers == 0 && connection.Disconnects == 1, "Closing during connect leaked resources");

        connection = new FakeConnection { BlockConnect = true };
        runtime = new(new RecordingMouse(), connection);
        pending = runtime.ConnectAsync(new());
        await connection.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await runtime.DisconnectAsync("cancel").WaitAsync(TimeSpan.FromSeconds(3));
        try { await pending; throw new Exception("Manual cancellation did not cancel connect"); }
        catch (OperationCanceledException) { count++; }
        connection.BlockConnect = false; await runtime.ConnectAsync(new());
        Check(runtime.Connected && runtime.InputThreadAlive, "Runtime could not reconnect after cancellation");
        await runtime.DisposeAsync();

        foreach (bool failureInNotification in new[] { true, false })
        {
            connection = new FakeConnection { FailNotifications = failureInNotification, FailDisconnect = !failureInNotification };
            runtime = new(new RecordingMouse(), connection);
            try { await runtime.DisposeAsync(); throw new Exception("Expected simulated cleanup failure"); }
            catch (InvalidOperationException) { count++; }
            Check(!runtime.InputThreadAlive && connection.Subscribers == 0 && connection.Disconnects == 1, "Cleanup failure skipped input-thread release");
        }

        connection = new FakeConnection { NotificationGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        runtime = new(new RecordingMouse(), connection);
        await runtime.ConnectAsync(new());
        Task manual = runtime.DisconnectAsync("manual");
        Task closing = runtime.DisposeAsync().AsTask();
        Check(connection.NotificationStops == 1 && !closing.IsCompleted, "Closing did not await existing disconnect");
        connection.NotificationGate.SetResult();
        await Task.WhenAll(manual, closing).WaitAsync(TimeSpan.FromSeconds(3));
        Check(connection.Disconnects == 1 && !runtime.InputThreadAlive, "Concurrent shutdown repeated native cleanup");

        foreach (bool enabled in new[] { true, false })
        {
            connection = new FakeConnection(); runtime = new(new RecordingMouse(), connection);
            var settings = new Settings { AutoCalibrateOnConnect = true, CalibrationDurationSeconds = .1, AutoReconnect = true };
            await runtime.ApplyAsync(settings); await runtime.ConnectAsync(settings);
            connection.Emit(new byte[20], true);
            await Task.Delay(120);
            for (int i = 0; i < 10; i++) connection.Emit(new byte[20], true);
            await runtime.CalibrateAsync();
            connection.Emit(new byte[20], true);
            await Task.Delay(120);
            for (int i = 0; i < 10; i++) connection.Emit(new byte[20], true);
            if (enabled) await runtime.ToggleAsync(); else await runtime.CalibrateAsync();
            connection.Lose();
            await WaitUntil(() => connection.Connects == 2 && runtime.Connected);
            await runtime.CalibrateAsync();
            Check(runtime.Snapshot.Enabled == enabled && !runtime.Snapshot.Calibrated, "Reconnect lost mouse state or skipped calibration");
            connection.Emit(new byte[20], true);
            await Task.Delay(120);
            for (int i = 0; i < 10; i++) connection.Emit(new byte[20], true);
            await WaitUntil(() => runtime.Snapshot.Calibrated);
            Check(runtime.Snapshot.Enabled == enabled, "Recalibration changed restored mouse state");
            connection.BlockConnect = true; connection.Lose();
            await WaitUntil(() => connection.Connects == 3);
            await runtime.DisconnectAsync("manual").WaitAsync(TimeSpan.FromSeconds(3));
            Check(!runtime.Connected && !runtime.Snapshot.Enabled, "Manual stop did not cancel retry");
            await runtime.DisposeAsync();
        }
        connection = new FakeConnection(); runtime = new(new RecordingMouse(), connection);
        await runtime.ConnectAsync(new Settings { AutoReconnect = false });
        int lost = 0; runtime.Disconnected += () => lost++;
        connection.Lose();
        Check(lost == 1 && connection.Connects == 1, "Disabled auto reconnect still retried");
        await runtime.DisposeAsync();

        connection = new FakeConnection(); runtime = new(new RecordingMouse(), connection);
        await runtime.ConnectAsync(new()); connection.FailConnects = 1;
        connection.Lose(); connection.Lose();
        await WaitUntil(() => connection.Connects >= 3 && runtime.Connected);
        Check(connection.Connects == 3, "Failed retry did not repeat or duplicate loss started competing loops");
        connection.BlockConnect = true; connection.Lose();
        await WaitUntil(() => connection.Connects == 4);
        await runtime.ApplyAsync(new Settings { AutoReconnect = false }).WaitAsync(TimeSpan.FromSeconds(3));
        Check(!runtime.Connected, "Disabling auto reconnect did not cancel pending scan");
        await runtime.DisposeAsync();
        connection = new FakeConnection(); runtime = new(new RecordingMouse(), connection);
        await runtime.ConnectAsync(new()); connection.BlockConnect = true; connection.Lose();
        await WaitUntil(() => connection.Connects == 2);
        await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Check(!runtime.InputThreadAlive && connection.Subscribers == 0, "Shutdown during reconnect leaked resources");

        connection = new FakeConnection(); runtime = new(new RecordingMouse(), connection);
        var manualSettings = new Settings { CalibrationDurationSeconds = .1 };
        Check(!manualSettings.AutoCalibrateOnConnect, "Automatic calibration must default to false");
        await runtime.ApplyAsync(manualSettings); await runtime.ConnectAsync(manualSettings);
        connection.Emit(new byte[20], true); await Task.Delay(150); connection.Emit(new byte[20], true);
        await Task.Delay(60);
        Check(!runtime.Snapshot.Calibrated && !runtime.Snapshot.IsCalibrating && runtime.Snapshot.CalibrationProgress == 0, "Disabled connection calibration still consumed frames");
        Check(!runtime.Snapshot.Enabled, "Manual session enabled mouse before calibration");
        await runtime.CalibrateAsync();
        Check(runtime.Snapshot.IsCalibrating, "Manual calibration did not start while auto calibration disabled");
        connection.Emit(new byte[20], true); await Task.Delay(120); connection.Emit(new byte[20], true);
        await WaitUntil(() => runtime.Snapshot.Calibrated);
        await runtime.ToggleAsync(); connection.Lose();
        await WaitUntil(() => connection.Connects == 2 && runtime.Connected);
        Check(runtime.Snapshot.Enabled && runtime.Snapshot.Calibrated && !runtime.Snapshot.IsCalibrating, "Reconnect without auto calibration did not retain completed bias and mouse state");
        await runtime.DisposeAsync();

        connection = new FakeConnection { FailConnects = 2 }; runtime = new(new RecordingMouse(), connection);
        await runtime.ConnectAsync(new());
        Check(connection.Connects == 3 && runtime.Connected && connection.Disconnects == 2, "Initial connection did not clean up and retry three times");
        await runtime.DisposeAsync();
        connection = new FakeConnection { FailConnects = 5 }; runtime = new(new RecordingMouse(), connection);
        try { await runtime.ConnectAsync(new()); throw new Exception("Expected retries exhausted"); }
        catch (TimeoutException) { count++; }
        Check(connection.Connects == 3 && !runtime.Connected, "Initial connection retries were not bounded");
        await runtime.DisposeAsync();
        connection = new FakeConnection { FailConnects = 1 }; runtime = new(new RecordingMouse(), connection);
        var retryWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Progress += text => { if (text.Contains("2 秒后重试")) retryWaiting.TrySetResult(); };
        Task connecting = runtime.ConnectAsync(new()); await retryWaiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await runtime.DisconnectAsync("cancel between attempts");
        try { await connecting; throw new Exception("Cancelled retry unexpectedly succeeded"); }
        catch (OperationCanceledException) { count++; }
        Check(connection.Connects == 1, "Cancellation did not stop retry delay"); await runtime.DisposeAsync();

        foreach (bool enabled in new[] { true, false })
        {
            connection = new FakeConnection(); mouse = new RecordingMouse(); runtime = new(mouse, connection);
            await runtime.ConnectAsync(new());
            if (enabled) await runtime.ToggleAsync();
            var pressed = new byte[20]; pressed[19] = 4;
            if (enabled) { connection.Emit(pressed, false); Check(mouse.Down.Wait(1000), "Sleep test drag not started"); }
            await runtime.SuspendAsync(); await runtime.SuspendAsync();
            Check(!runtime.Connected && !runtime.Snapshot.Enabled && (!enabled || mouse.Up.IsSet), "Suspend did not stop input/release drag");
            Check(connection.Disconnects == 1, "Duplicate suspend repeated teardown");
            await runtime.ResumeAsync(); await runtime.ResumeAsync();
            await WaitUntil(() => connection.Connects == 2 && runtime.Connected);
            Check(runtime.Snapshot.Enabled == enabled, "Resume changed pre-sleep mouse state");
            await runtime.DisposeAsync();
        }
        foreach (bool autoReconnect in new[] { true, false })
        {
            connection = new FakeConnection(); runtime = new(new RecordingMouse(), connection);
            await runtime.ConnectAsync(new Settings { AutoReconnect = autoReconnect });
            await runtime.SuspendAsync();
            if (autoReconnect) await runtime.DisconnectAsync("manual stop during sleep");
            await runtime.ResumeAsync(); await Task.Delay(30);
            Check(connection.Connects == 1 && !runtime.Connected, "Manual stop/disabled setting was ignored after resume");
            await runtime.DisposeAsync();
        }
        connection = new FakeConnection(); runtime = new(new RecordingMouse(), connection);
        await runtime.ConnectAsync(new()); await runtime.SuspendAsync(); await runtime.ResumeAsync();
        await runtime.DisconnectAsync("cancel wake delay");
        Check(connection.Connects == 1 && !runtime.Snapshot.Enabled, "Wake delay could not be cancelled"); await runtime.DisposeAsync();
        connection = new FakeConnection { BlockConnect = true }; runtime = new(new RecordingMouse(), connection);
        connecting = runtime.ConnectAsync(new()); await connection.Started.Task;
        await runtime.SuspendAsync();
        try { await connecting; throw new Exception("Suspend failed to cancel scan"); }
        catch (OperationCanceledException) { count++; }
        connection.BlockConnect = false; await runtime.ResumeAsync(); await WaitUntil(() => runtime.Connected);
        Check(connection.Connects == 2, "Resume did not recover in-flight pre-sleep connection"); await runtime.DisposeAsync();
        connection = new FakeConnection(); runtime = new(new RecordingMouse(), connection);
        await runtime.ConnectAsync(new()); await runtime.SuspendAsync(); await runtime.ResumeAsync();
        await runtime.DisposeAsync();
        Check(!runtime.InputThreadAlive && connection.Subscribers == 0 && connection.Connects == 1, "Exit during wake delay leaked a retry or input thread");

        connection = new FakeConnection(); mouse = new RecordingMouse(); runtime = new(mouse, connection);
        await runtime.ConnectAsync(new()); await runtime.ToggleAsync(); await runtime.SuspendAsync();
        bool outputEarly = false;
        var subscribedPacket = new byte[20]; subscribedPacket[19] = 1;
        connection.DuringConnect = () =>
        {
            outputEarly = runtime.Snapshot.Enabled;
            connection.Emit(subscribedPacket, false);
        };
        int beforeReconnect = mouse.Calls;
        await runtime.ResumeAsync(); await WaitUntil(() => runtime.Connected && runtime.Snapshot.Enabled);
        await Task.Delay(50);
        Check(!outputEarly && mouse.Calls == beforeReconnect, "Reconnection emitted input before subscription completed");
        await runtime.DisposeAsync();

        string path = Path.Combine(AppContext.BaseDirectory, "scratch", "diagnostic-close.log");
        var writer = new DiagnosticWriter(path);
        writer.Write("first"); writer.Write("last");
        await writer.DisposeAsync(); await writer.DisposeAsync();
        Check(writer.IsCompleted && !writer.Write("late"), "Diagnostic writer still accepting data after disposal");
        Check(File.ReadAllLines(path).SequenceEqual(new[] { "first", "last" }), "Diagnostic buffer not flushed");
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) count++;
        await Task.WhenAll(InputDiagnostics.ShutdownAsync(), InputDiagnostics.ShutdownAsync());
        Check(InputDiagnostics.IsStopped, "Diagnostic timer/tasks still running after shutdown");
        return count;
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
    private sealed class RecordingMouse : IMouseOutput
    {
        public ManualResetEventSlim Down { get; } = new();
        public ManualResetEventSlim Up { get; } = new();
        public int Calls;
        public void Send(uint flags, int dx = 0, int dy = 0, int data = 0)
        {
            Interlocked.Increment(ref Calls);
            if (flags == 2) Down.Set(); if (flags == 4) Up.Set();
        }
        public void Center() => Interlocked.Increment(ref Calls);
    }
    private sealed class FakeConnection : IControllerConnection
    {
        public bool Connected { get; private set; }
        public bool BlockConnect, FailNotifications, FailDisconnect;
        public int NotificationStops, Disconnects, Connects, FailConnects;
        public Action? DuringConnect;
        public void Lose() { Connected = false; LinkInterrupted?.Invoke(); Disconnected?.Invoke(); }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? NotificationGate;
        public event Action<string>? Progress;
        public event Action<byte[], bool>? Frame;
        public event Action? Disconnected;
        public event Action? LinkInterrupted;
        public event Action? LinkRecovered;
        public int Subscribers => (Progress?.GetInvocationList().Length ?? 0) + (Frame?.GetInvocationList().Length ?? 0)
            + (Disconnected?.GetInvocationList().Length ?? 0) + (LinkInterrupted?.GetInvocationList().Length ?? 0) + (LinkRecovered?.GetInvocationList().Length ?? 0);
        public async Task ConnectAsync(Settings settings, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Connects);
            Started.TrySetResult();
            if (FailConnects > 0) { FailConnects--; throw new TimeoutException("simulated scan failure"); }
            if (BlockConnect) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); DuringConnect?.Invoke(); Connected = true;
        }
        public void Emit(byte[] data, bool gyro) => Frame?.Invoke(data, gyro);
        public void CancelPending() { }
        public Task StopNotificationsAsync()
        {
            NotificationStops++;
            if (FailNotifications) throw new InvalidOperationException("notification stop");
            return NotificationGate?.Task ?? Task.CompletedTask;
        }
        public void Disconnect()
        {
            Disconnects++; Connected = false;
            if (FailDisconnect) throw new InvalidOperationException("native dispose");
        }
    }
}
