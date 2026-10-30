using System.Diagnostics;
using System.IO;

namespace OculusGoAirMouse.Services;

public static class InputDiagnostics
{
    private static readonly DiagnosticWriter writer = new(FilePath);
    private static readonly CancellationTokenSource lifetime = new();
    private static readonly Task healthTask = Task.Run(HealthLoopAsync);
    private static readonly object shutdownGate = new();
    private static Task? shutdown;
    private static int stopping;
    private static long received, processed, lastReceived, lastProcessed, lastUi, captureUntil;
    private static int receivedButtons = -1, processedButtons = -1;
    public static string FilePath => Path.Combine(AppContext.GetData("OculusGoAirMouse.DiagnosticsDirectory") as string ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OculusGoAirMouse"), "input-diagnostics.log");
    public static bool IsStopped => shutdown?.IsCompletedSuccessfully == true;
    private static double Age(long time) => time == 0 ? -1 : Stopwatch.GetElapsedTime(time).TotalMilliseconds;
    private static async Task HealthLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
                if (Stopwatch.GetTimestamp() < Volatile.Read(ref captureUntil))
                    Write($"health received={Interlocked.Read(ref received)} processed={Interlocked.Read(ref processed)} receiveAgeMs={Age(Interlocked.Read(ref lastReceived)):F0} processAgeMs={Age(Interlocked.Read(ref lastProcessed)):F0} uiAgeMs={Age(Interlocked.Read(ref lastUi)):F0}");
        }
        catch (OperationCanceledException) { }
    }
    public static void Write(string message)
    {
        if (Volatile.Read(ref stopping) == 0) writer.Write($"{DateTimeOffset.Now:HH:mm:ss.fff} thread={Environment.CurrentManagedThreadId} {message}");
    }
    public static void Capture(string message)
    {
        Interlocked.Exchange(ref captureUntil, Stopwatch.GetTimestamp() + 10 * Stopwatch.Frequency);
        Write(message);
    }
    public static void UiTick() => Interlocked.Exchange(ref lastUi, Stopwatch.GetTimestamp());
    public static void Frame(byte[] data, bool gyro, bool isProcessed)
    {
        if (isProcessed) { Interlocked.Increment(ref processed); Interlocked.Exchange(ref lastProcessed, Stopwatch.GetTimestamp()); }
        else { Interlocked.Increment(ref received); Interlocked.Exchange(ref lastReceived, Stopwatch.GetTimestamp()); }
        if (gyro || data.Length != 20) return;
        int buttons = data[19] & 7;
        int before = isProcessed ? Interlocked.Exchange(ref processedButtons, buttons) : Interlocked.Exchange(ref receivedButtons, buttons);
        if (before != buttons) Write($"{(isProcessed ? "processed" : "received")} buttons={buttons}");
    }
    public static Task ShutdownAsync()
    {
        lock (shutdownGate) return shutdown ??= ShutdownCoreAsync();
    }
    private static async Task ShutdownCoreAsync()
    {
        Write("diagnostics shutdown complete");
        Interlocked.Exchange(ref stopping, 1);
        lifetime.Cancel();
        await healthTask.ConfigureAwait(false);
        await writer.DisposeAsync().ConfigureAwait(false);
        lifetime.Dispose();
    }
}
