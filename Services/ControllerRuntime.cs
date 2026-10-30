using OculusGoAirMouse.Models;

namespace OculusGoAirMouse.Services;

public sealed class ControllerRuntime : IControllerRuntime
{
    private readonly EngineWorker engine;
    private readonly IControllerConnection connection;
    private readonly object lifecycle = new();
    private Task? activeConnection, disconnection, disposal;
    private CancellationTokenSource? connectionCancellation;
    private volatile bool disposing;
    public EngineSnapshot Snapshot => engine.Snapshot;
    public bool Connected => !disposing && connection.Connected;
    public bool InputThreadAlive => engine.IsRunning;
    public event Action<string>? Progress;
    public event Action<Exception>? Faulted;
    public event Action? Disconnected;
    public event Action<bool>? LinkChanged;

    public ControllerRuntime(IMouseOutput output, IControllerConnection connection)
    {
        this.connection = connection;
        engine = new(output);
        connection.Frame += OnFrame;
        connection.Progress += OnProgress;
        connection.Disconnected += OnDisconnected;
        connection.LinkInterrupted += OnInterrupted;
        connection.LinkRecovered += OnRecovered;
        engine.Faulted += OnFaulted;
    }
    private void OnFrame(byte[] data, bool gyro) { if (!disposing) engine.PostFrame(data, gyro); }
    private void OnProgress(string text) { if (!disposing) Progress?.Invoke(text); }
    private void OnDisconnected() { if (!disposing) Disconnected?.Invoke(); }
    private void OnFaulted(Exception error) { if (!disposing) Faulted?.Invoke(error); }
    private void OnInterrupted() => OnLinkChanged(false);
    private void OnRecovered() => OnLinkChanged(true);
    private void OnLinkChanged(bool recovered)
    {
        if (disposing) return;
        Task safety = recovered ? engine.StartSessionAsync() : engine.StopSessionAsync();
        _ = safety.ContinueWith(task =>
        {
            if (task.IsFaulted) InputDiagnostics.Write("link safety error=" + task.Exception!.GetBaseException().Message);
        }, TaskScheduler.Default);
        LinkChanged?.Invoke(recovered);
    }
    private void ThrowIfDisposing() { ObjectDisposedException.ThrowIf(disposing, this); }
    public Task ApplyAsync(Settings settings) { ThrowIfDisposing(); return engine.ApplyAsync(settings); }
    public Task ToggleAsync() { ThrowIfDisposing(); return engine.ToggleAsync(); }
    public Task CalibrateAsync() { ThrowIfDisposing(); return engine.CalibrateAsync(); }
    public Task ConnectAsync(Settings settings)
    {
        lock (lifecycle)
        {
            ThrowIfDisposing();
            if (disconnection is { IsCompleted: false }) throw new InvalidOperationException("正在释放上一次连接。");
            if (activeConnection is { IsCompleted: false }) throw new InvalidOperationException("已有连接操作正在进行。");
            var cancellation = connectionCancellation = new CancellationTokenSource();
            return activeConnection = ConnectCoreAsync(settings.Copy(), cancellation);
        }
    }
    private async Task ConnectCoreAsync(Settings settings, CancellationTokenSource cancellation)
    {
        try
        {
            await engine.StartSessionAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            ThrowIfDisposing();
            await connection.ConnectAsync(settings, cancellation.Token);
        }
        catch { await engine.StopSessionAsync(); throw; }
        finally
        {
            lock (lifecycle) { if (ReferenceEquals(connectionCancellation, cancellation)) connectionCancellation = null; }
            cancellation.Dispose();
        }
    }
    public Task DisconnectAsync(string reason)
    {
        lock (lifecycle) { ThrowIfDisposing(); return RequestDisconnect(reason); }
    }
    private Task RequestDisconnect(string reason)
    {
        lock (lifecycle)
            return disconnection is { IsCompleted: false } ? disconnection : disconnection = DisconnectCoreAsync(reason);
    }
    private async Task DisconnectCoreAsync(string reason)
    {
        InputDiagnostics.Write("app stop reason=" + reason);
        var release = engine.StopSessionAsync();
        try
        {
            lock (lifecycle) connectionCancellation?.Cancel();
            connection.CancelPending();
            var pending = activeConnection;
            if (pending != null)
            {
                try { await pending.ConfigureAwait(false); }
                catch (Exception ex) { InputDiagnostics.Write("connection operation ended=" + ex.GetType().Name); }
            }
            await connection.StopNotificationsAsync().ConfigureAwait(false);
        }
        finally
        {
            try { connection.Disconnect(); }
            finally { await release.ConfigureAwait(false); }
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            if (disposal == null)
            {
                disposing = true;
                connection.Frame -= OnFrame;
                connection.Progress -= OnProgress;
                connection.Disconnected -= OnDisconnected;
                connection.LinkInterrupted -= OnInterrupted;
                connection.LinkRecovered -= OnRecovered;
                engine.Faulted -= OnFaulted;
                disposal = DisposeCoreAsync();
            }
            return new(disposal);
        }
    }
    private async Task DisposeCoreAsync()
    {
        try { await RequestDisconnect("程序关闭").ConfigureAwait(false); }
        finally
        {
            try { await engine.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                Progress = null; Faulted = null; Disconnected = null; LinkChanged = null;
                InputDiagnostics.Write("runtime shutdown complete");
            }
        }
    }
}
