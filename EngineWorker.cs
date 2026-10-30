using System.Collections.Concurrent;

namespace OculusGoAirMouse;

public sealed record EngineSnapshot(bool Enabled, bool Calibrated, string Status);

// The UI must never be responsible for delivering mouse-up. All engine access,
// native input calls and the watchdog are serialized on this independent thread.
public sealed class EngineWorker : IAsyncDisposable
{
    private readonly BlockingCollection<Action> queue = new();
    private readonly AirMouseEngine engine;
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object lifecycle = new();
    private bool acceptingFrames, stopping;
    private int generation, queuedFrames, overflow;
    private EngineSnapshot snapshot = new(false, false, "未连接");
    public EngineSnapshot Snapshot => Volatile.Read(ref snapshot);
    public event Action<Exception>? Faulted;

    public EngineWorker(IMouseOutput output)
    {
        engine = new(output);
        new Thread(Run) { IsBackground = true, Name = "Oculus mouse input" }.Start();
    }

    public void PostFrame(byte[] data, bool gyro)
    {
        lock (lifecycle)
        {
            if (stopping || !acceptingFrames) return;
            if (Interlocked.Increment(ref queuedFrames) > 512)
            {
                Interlocked.Decrement(ref queuedFrames);
                Interlocked.Exchange(ref overflow, 1);
                acceptingFrames = false;
                generation++;
                return;
            }
            int current = generation;
            queue.Add(() =>
            {
                Interlocked.Decrement(ref queuedFrames);
                if (current != Volatile.Read(ref generation)) return;
                InputDiagnostics.Frame(data, gyro, true);
                if (gyro) engine.Gyro(data); else engine.Sensor(data);
            });
        }
    }

    private Task Command(Action action, bool invalidate = false, bool? allowFrames = null)
    {
        lock (lifecycle)
        {
            if (stopping) return Task.FromException(new ObjectDisposedException(nameof(EngineWorker)));
            if (invalidate) generation++;
            if (allowFrames.HasValue) acceptingFrames = allowFrames.Value;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Add(() =>
            {
                try { action(); Publish(); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); throw; }
            });
            return completion.Task;
        }
    }

    public Task ApplyAsync(Settings settings)
    {
        var copy = settings.Copy(); copy.Validate();
        return Command(() => engine.Apply(copy), invalidate: true);
    }
    public Task CalibrateAsync() => Command(engine.Calibrate, invalidate: true);
    public Task StartSessionAsync() => Command(engine.Calibrate, invalidate: true, allowFrames: true);
    public Task StopSessionAsync() => Command(engine.Disable, invalidate: true, allowFrames: false);
    public Task ToggleAsync() => Command(() => { if (engine.Enabled) engine.Disable(); else engine.Enable(); });

    private void Publish() => Volatile.Write(ref snapshot, new EngineSnapshot(engine.Enabled, engine.Calibrated, engine.Status));
    private void Fail(Exception error)
    {
        InputDiagnostics.Capture("input failure=" + error.Message);
        lock (lifecycle) { acceptingFrames = false; generation++; }
        try { engine.Disable(); }
        catch (Exception releaseError) { error = new AggregateException(error, releaseError); }
        Publish();
        // Subscribers must report asynchronously; never synchronously call the UI.
        try { Faulted?.Invoke(error); } catch { /* Reporting cannot stop the input thread. */ }
    }
    private void Run()
    {
        try
        {
            while (!queue.IsCompleted)
            {
                try
                {
                    if (Interlocked.Exchange(ref overflow, 0) != 0) Fail(new InvalidOperationException("控制器数据积压，已停止鼠标控制。请重新连接。"));
                    if (queue.TryTake(out var action, 50)) action();
                    engine.CheckTimeout();
                    Publish();
                }
                catch (Exception ex) { Fail(ex); }
            }
        }
        finally
        {
            try { engine.Disable(); stopped.TrySetResult(); }
            catch (Exception ex) { stopped.TrySetException(ex); }
            queue.Dispose();
        }
    }
    public async ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            if (!stopping)
            {
                stopping = true; acceptingFrames = false; generation++;
                queue.CompleteAdding();
            }
        }
        await stopped.Task.ConfigureAwait(false);
    }
}
