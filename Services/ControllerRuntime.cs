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
    private Settings currentSettings = new();
    private CancellationTokenSource? retryCancellation;
    private Task? retryTask;
    private bool interrupted, resumeMouse, manuallyStopped;
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
    private void OnDisconnected()
    {
        lock (lifecycle)
        {
            if (disposing || manuallyStopped || connection.Connected) return;
            if (!currentSettings.AutoReconnect) { Disconnected?.Invoke(); return; }
            if (retryTask is { IsCompleted: false }) return;
            if (!interrupted) resumeMouse = Snapshot.Enabled;
            var cancellation = retryCancellation = new();
            retryTask = Task.Run(() => RetryAsync(cancellation));
        }
    }
    private async Task RetryAsync(CancellationTokenSource cancellation)
    {
        try
        {
            LinkChanged?.Invoke(false);
            await RequestDisconnect("蓝牙丢失，准备自动重连").ConfigureAwait(false);
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                OnProgress("自动重连：正在重新搜索控制器，请按 Trigger 唤醒…");
                try
                {
                    Task pending;
                    lock (lifecycle)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        var attempt = connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                        pending = activeConnection = ConnectCoreAsync(currentSettings.Copy(), attempt, resumeMouse);
                    }
                    await pending.ConfigureAwait(false);
                    interrupted = false;
                    LinkChanged?.Invoke(true);
                    OnProgress("自动重连成功，正在静置校准；完成后恢复原鼠标状态。");
                    return;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception ex) { OnProgress("自动重连未成功：" + ex.Message + "；2 秒后重试。"); }
                await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { OnFaulted(ex); }
        finally
        {
            lock (lifecycle) { if (ReferenceEquals(retryCancellation, cancellation)) retryCancellation = null; }
            cancellation.Dispose();
        }
    }
    private Task StopRetryAsync()
    {
        lock (lifecycle)
        {
            manuallyStopped = true;
            retryCancellation?.Cancel();
            connectionCancellation?.Cancel();
            return retryTask ?? Task.CompletedTask;
        }
    }
    private void OnFaulted(Exception error) { if (!disposing) Faulted?.Invoke(error); }
    private void OnInterrupted() => OnLinkChanged(false);
    private void OnRecovered() => OnLinkChanged(true);
    private void OnLinkChanged(bool recovered)
    {
        if (disposing) return;
        lock (lifecycle)
        {
            if (manuallyStopped || retryTask is { IsCompleted: false }) return;
            if (!recovered && !interrupted) { resumeMouse = Snapshot.Enabled; interrupted = true; }
        }
        Task safety = recovered ? engine.StartSessionAsync(currentSettings.AutoReconnect && resumeMouse) : engine.StopSessionAsync();
        if (recovered) interrupted = false;
        _ = safety.ContinueWith(task =>
        {
            if (task.IsFaulted) InputDiagnostics.Write("link safety error=" + task.Exception!.GetBaseException().Message);
        }, TaskScheduler.Default);
        LinkChanged?.Invoke(recovered);
    }
    private void ThrowIfDisposing() { ObjectDisposedException.ThrowIf(disposing, this); }
    public Task ApplyAsync(Settings settings)
    {
        ThrowIfDisposing(); settings.Validate();
        lock (lifecycle) currentSettings = settings.Copy();
        return ApplyCoreAsync(settings);
    }
    private async Task ApplyCoreAsync(Settings settings)
    {
        if (!settings.AutoReconnect && retryTask is { IsCompleted: false })
        {
            await StopRetryAsync().ConfigureAwait(false);
            await RequestDisconnect("已关闭自动重连").ConfigureAwait(false);
            Disconnected?.Invoke();
        }
        await engine.ApplyAsync(settings).ConfigureAwait(false);
    }
    public Task ToggleAsync() { ThrowIfDisposing(); return engine.ToggleAsync(); }
    public Task CalibrateAsync() { ThrowIfDisposing(); return engine.CalibrateAsync(); }
    public Task ConnectAsync(Settings settings)
    {
        lock (lifecycle)
        {
            ThrowIfDisposing();
            if (retryTask is { IsCompleted: false }) throw new InvalidOperationException("正在自动重连。");
            manuallyStopped = false; interrupted = false; currentSettings = settings.Copy();
            if (disconnection is { IsCompleted: false }) throw new InvalidOperationException("正在释放上一次连接。");
            if (activeConnection is { IsCompleted: false }) throw new InvalidOperationException("已有连接操作正在进行。");
            var cancellation = connectionCancellation = new CancellationTokenSource();
            return activeConnection = ConnectCoreAsync(settings.Copy(), cancellation);
        }
    }
    private async Task ConnectCoreAsync(Settings settings, CancellationTokenSource cancellation, bool restoreMouse = false)
    {
        try
        {
            await engine.StartSessionAsync(restoreMouse);
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
        ThrowIfDisposing(); return StopAndDisconnectAsync(reason);
    }
    private async Task StopAndDisconnectAsync(string reason)
    {
        await StopRetryAsync().ConfigureAwait(false);
        await RequestDisconnect(reason).ConfigureAwait(false);
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
        try { await StopAndDisconnectAsync("程序关闭").ConfigureAwait(false); }
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
