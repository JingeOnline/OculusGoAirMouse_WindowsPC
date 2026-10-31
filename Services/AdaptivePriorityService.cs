using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OculusGoAirMouse.Services;

// Hysteresis avoids switching priorities repeatedly around a single threshold.
public sealed class CpuPriorityPolicy
{
    private int busySamples, quietSamples;
    public bool Boosted { get; private set; }
    public bool Update(double? load, bool active)
    {
        if (!active) { busySamples = quietSamples = 0; return Boosted = false; }
        if (!load.HasValue || !double.IsFinite(load.Value) || load is < 0 or > 1)
        { busySamples = quietSamples = 0; return Boosted; }
        busySamples = load >= .8 ? busySamples + 1 : 0;
        quietSamples = load <= .6 ? quietSamples + 1 : 0;
        if (busySamples >= 2) Boosted = true;
        if (quietSamples >= 5) Boosted = false;
        return Boosted;
    }
}

public interface IPriorityPlatform : IDisposable
{
    double? ReadCpuLoad();
    ProcessPriorityClass Priority { get; set; }
}

public sealed class AdaptivePriorityService : IAsyncDisposable
{
    private readonly IPriorityPlatform platform;
    private readonly ProcessPriorityClass original;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task monitor;
    private readonly object gate = new();
    private Task? disposal;
    public AdaptivePriorityService(Func<bool> inputActive, IPriorityPlatform? platform = null, TimeSpan? interval = null)
    {
        this.platform = platform ?? new WindowsPriorityPlatform();
        try { original = this.platform.Priority; }
        catch { this.platform.Dispose(); cancellation.Dispose(); throw; }
        monitor = RunAsync(inputActive, interval ?? TimeSpan.FromSeconds(1));
    }
    private async Task RunAsync(Func<bool> active, TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval);
        var policy = new CpuPriorityPolicy();
        int errors = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    double? load = platform.ReadCpuLoad();
                    bool boosted = policy.Update(load, active());
                    // Do not lower a priority explicitly inherited from the launcher.
                    var target = boosted && original is ProcessPriorityClass.Normal or ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Idle
                        ? ProcessPriorityClass.AboveNormal : original;
                    if (platform.Priority != target)
                    {
                        platform.Priority = target;
                        InputDiagnostics.Write($"CPU priority={target} load={load:P0}");
                    }
                    errors = 0;
                }
                catch (Exception ex)
                {
                    if (errors++ % 30 == 0) InputDiagnostics.Write("CPU priority monitor error=" + ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            try { platform.Priority = original; }
            catch (Exception ex) { InputDiagnostics.Write("CPU priority restore error=" + ex.Message); }
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (gate) return new(disposal ??= StopAsync());
    }
    private async Task StopAsync()
    {
        cancellation.Cancel();
        try { await monitor.ConfigureAwait(false); }
        finally { platform.Dispose(); cancellation.Dispose(); }
    }
}

public sealed class WindowsPriorityPlatform : IPriorityPlatform
{
    private readonly Process process = Process.GetCurrentProcess();
    private ulong lastIdle, lastKernel, lastUser;
    private bool sampled;
    public ProcessPriorityClass Priority { get => process.PriorityClass; set => process.PriorityClass = value; }
    public double? ReadCpuLoad()
    {
        if (!GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        { sampled = false; return null; }
        double? load = null;
        if (sampled && idle >= lastIdle && kernel >= lastKernel && user >= lastUser)
        {
            double total = (double)(kernel - lastKernel) + (user - lastUser);
            if (total > 0) load = Math.Clamp(1 - (idle - lastIdle) / total, 0, 1);
        }
        lastIdle = idle; lastKernel = kernel; lastUser = user; sampled = true;
        return load;
    }
    public void Dispose() => process.Dispose();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
