using OculusGoAirMouse.Models;

namespace OculusGoAirMouse.Services;

public sealed class ControllerRuntime : IControllerRuntime
{
    private readonly EngineWorker engine;
    private readonly IControllerConnection connection;
    private readonly object lifecycle = new();
    private Task? activeConnection, disconnection, disposal;
    private CancellationTokenSource? connectionCancellation;
    private volatile bool disposing, acceptNativeFrames;
    private Settings currentSettings = new();
    private CancellationTokenSource? retryCancellation;
    private Task? retryTask;
    private bool interrupted, resumeMouse, manuallyStopped;
    private bool sleeping, wakeWanted, wakeMouse;
    private Task suspension = Task.CompletedTask;
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
    private void OnFrame(byte[] data, bool gyro) { if (!disposing && acceptNativeFrames) engine.PostFrame(data, gyro); }
    private void OnProgress(string text) { if (!disposing) Progress?.Invoke(text); }
    private void OnDisconnected()
    {
        lock (lifecycle)
        {
            if (disposing || sleeping || manuallyStopped || connection.Connected) return;
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
                        pending = activeConnection = ConnectCoreAsync(currentSettings.Copy(), attempt, resumeMouse, maxAttempts: 1);
                    }
                    await pending.ConfigureAwait(false);
                    interrupted = false;
                    LinkChanged?.Invoke(true);
                    OnProgress(currentSettings.AutoCalibrateOnConnect ? "自动重连成功，正在静置校准；完成后恢复原鼠标状态。" : "自动重连成功，保留原鼠标状态；未自动执行校准。");
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
            if (sleeping || manuallyStopped || retryTask is { IsCompleted: false }) return;
            if (!recovered && !interrupted) { resumeMouse = Snapshot.Enabled; interrupted = true; }
        }
        Task safety = recovered ? engine.StartSessionAsync(currentSettings.AutoReconnect && resumeMouse, currentSettings.AutoCalibrateOnConnect) : engine.StopSessionAsync();
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
            if (sleeping) throw new InvalidOperationException("系统正在睡眠，等待恢复后连接。");
            manuallyStopped = false; interrupted = false; currentSettings = settings.Copy();
            if (disconnection is { IsCompleted: false }) throw new InvalidOperationException("正在释放上一次连接。");
            if (activeConnection is { IsCompleted: false }) throw new InvalidOperationException("已有连接操作正在进行。");
            var cancellation = connectionCancellation = new CancellationTokenSource();
            return activeConnection = ConnectCoreAsync(settings.Copy(), cancellation);
        }
    }
    private async Task ConnectCoreAsync(Settings settings, CancellationTokenSource cancellation, bool restoreMouse = false, int maxAttempts = 3)
    {
        try
        {
            acceptNativeFrames = false;
            await engine.StartSessionAsync(false, settings.AutoCalibrateOnConnect);
            cancellation.Token.ThrowIfCancellationRequested();
            ThrowIfDisposing();
            for (int attempt = 1; ; attempt++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    OnProgress($"连接尝试 {attempt}/{maxAttempts}");
                    await connection.ConnectAsync(settings, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    await engine.RestoreMouseAsync(restoreMouse);
                    acceptNativeFrames = true;
                    break;
                }
                catch (Exception) when (cancellation.IsCancellationRequested) { throw new OperationCanceledException(cancellation.Token); }
                catch (Exception ex)
                {
                    try { await connection.StopNotificationsAsync().ConfigureAwait(false); }
                    finally { connection.Disconnect(); }
                    InputDiagnostics.Write($"connect attempt={attempt} error={ex}");
                    if (attempt >= maxAttempts) throw;
                    OnProgress($"连接失败：{ex.Message}；2 秒后重试（{attempt + 1}/{maxAttempts}）。");
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token).ConfigureAwait(false);
                }
            }
        }
        catch { acceptNativeFrames = false; await engine.StopSessionAsync(); throw; }
        finally
        {
            lock (lifecycle) { if (ReferenceEquals(connectionCancellation, cancellation)) connectionCancellation = null; }
            cancellation.Dispose();
        }
    }
    public Task DisconnectAsync(string reason)
    {
        lock (lifecycle) { ThrowIfDisposing(); wakeWanted = false; }
        return StopAndDisconnectAsync(reason);
    }
    private async Task StopAndDisconnectAsync(string reason)
    {
        await StopRetryAsync().ConfigureAwait(false);
        await RequestDisconnect(reason).ConfigureAwait(false);
    }
    public Task SuspendAsync()
    {
        lock (lifecycle)
        {
            if (disposing || sleeping) return suspension;
            sleeping = true;
            wakeWanted = !manuallyStopped && (connection.Connected || activeConnection is { IsCompleted: false } || retryTask is { IsCompleted: false });
            wakeMouse = interrupted ? resumeMouse : Snapshot.Enabled;
            OnProgress("系统进入睡眠，已暂停控制并释放连接。");
            return suspension = StopAndDisconnectAsync("系统睡眠");
        }
    }
    public async Task ResumeAsync()
    {
        Task pending;
        lock (lifecycle) { if (disposing || !sleeping) return; pending = suspension; }
        try { await pending.ConfigureAwait(false); }
        catch (Exception ex) { InputDiagnostics.Write("suspend cleanup error=" + ex.Message); }
        lock (lifecycle)
        {
            if (disposing || !sleeping) return;
            sleeping = false;
            if (!wakeWanted || !currentSettings.AutoReconnect) { wakeWanted = false; return; }
            wakeWanted = false; manuallyStopped = false;
            interrupted = true; resumeMouse = wakeMouse;
            OnProgress("系统已唤醒，准备恢复控制器连接。");
            var cancellation = retryCancellation = new CancellationTokenSource();
            retryTask = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { lock (lifecycle) { if (ReferenceEquals(retryCancellation, cancellation)) retryCancellation = null; } cancellation.Dispose(); return; }
                await RetryAsync(cancellation).ConfigureAwait(false);
            });
            LinkChanged?.Invoke(false);
        }
    }
    private Task RequestDisconnect(string reason)
    {
        lock (lifecycle)
            return disconnection is { IsCompleted: false } ? disconnection : disconnection = DisconnectCoreAsync(reason);
    }
    private async Task DisconnectCoreAsync(string reason)
    {
        acceptNativeFrames = false;
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
