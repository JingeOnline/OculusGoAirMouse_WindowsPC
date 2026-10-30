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
        await runtime.ApplyAsync(new Settings { CalibrationSamples = 10 });
        await runtime.ConnectAsync(new Settings());
        Check(connection.Subscribers == 5 && runtime.Connected, "Runtime event wiring");
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
        public int NotificationStops, Disconnects;
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
            Started.TrySetResult();
            if (BlockConnect) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); Connected = true;
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
