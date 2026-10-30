namespace OculusGoAirMouse.Services;

// A transient Windows status notification must not immediately destroy the GATT
// session that is responsible for maintaining/re-establishing the connection.
public sealed class ConnectionLossMonitor(
    Func<bool> isConnected, Action interrupted, Action recovered, Action confirmed,
    TimeSpan gracePeriod) : IDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource? pending;
    private bool disposed, lost;

    public void Notify(bool connected)
    {
        lock (gate)
        {
            if (disposed) return;
            if (connected)
            {
                CancelPending();
                if (lost) { lost = false; recovered(); }
            }
            else if (!lost)
            {
                lost = true;
                var cts = pending = new CancellationTokenSource();
                interrupted(); // Subscribers only enqueue work; never wait for UI.
                _ = ConfirmAsync(cts);
            }
        }
    }
    private async Task ConfirmAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(gracePeriod, cts.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (disposed || !ReferenceEquals(pending, cts)) return;
                pending = null;
                if (isConnected()) { lost = false; recovered(); }
                else confirmed();
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally { cts.Dispose(); }
    }
    private void CancelPending() { pending?.Cancel(); pending = null; }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; CancelPending(); }
    }
}
