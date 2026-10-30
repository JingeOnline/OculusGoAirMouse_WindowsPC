using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;

static class ConnectionChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        bool connected = true;
        int interrupted = 0, recovered = 0, confirmed = 0;
        var confirmation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var monitor = new ConnectionLossMonitor(() => Volatile.Read(ref connected),
            () => Interlocked.Increment(ref interrupted),
            () => { Interlocked.Increment(ref recovered); restoration.TrySetResult(); },
            () => { Interlocked.Increment(ref confirmed); confirmation.TrySetResult(); }, TimeSpan.FromMilliseconds(70)))
        {
            monitor.Notify(true);
            Check(interrupted == 0 && recovered == 0 && confirmed == 0, "Stable connection emitted loss");
            connected = false; monitor.Notify(false); monitor.Notify(false);
            Check(interrupted == 1 && confirmed == 0, "Transient loss tore down connection immediately");
            connected = true; monitor.Notify(true);
            await Task.Delay(180);
            Check(recovered == 1 && confirmed == 0, "Recovered connection was torn down by stale timeout");
            connected = false; monitor.Notify(false);
            await confirmation.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(interrupted == 2 && confirmed == 1, "Persistent loss not confirmed");
            monitor.Notify(false);
            Check(confirmed == 1, "Duplicate disconnect confirmation");
        }
        interrupted = recovered = confirmed = 0;
        connected = false;
        restoration = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var monitor = new ConnectionLossMonitor(() => Volatile.Read(ref connected),
            () => Interlocked.Increment(ref interrupted),
            () => { Interlocked.Increment(ref recovered); restoration.TrySetResult(); },
            () => Interlocked.Increment(ref confirmed), TimeSpan.FromMilliseconds(70)))
        {
            monitor.Notify(false); connected = true; // Windows misses the connected event.
            await restoration.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(recovered == 1 && confirmed == 0, "Final status recheck not used");
        }
        interrupted = recovered = confirmed = 0; connected = false;
        var disposed = new ConnectionLossMonitor(() => connected, () => interrupted++, () => recovered++, () => confirmed++, TimeSpan.FromMilliseconds(70));
        disposed.Notify(false); disposed.Dispose();
        await Task.Delay(180);
        disposed.Notify(true); disposed.Notify(false);
        Check(interrupted == 1 && recovered == 0 && confirmed == 0, "Disposed session emitted stale events");
        return count;
    }
}
