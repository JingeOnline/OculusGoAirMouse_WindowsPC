using System.Diagnostics;
using System.IO;
using System.Threading.Channels;

namespace OculusGoAirMouse.Services;

// Bounded, asynchronous diagnostic output. The input thread never waits for disk
// or the UI; no packet contents, addresses or desktop activity are recorded.
public static class InputDiagnostics
{
    private static readonly Channel<string> lines = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private static long received, processed, lastReceived, lastProcessed, lastUi, captureUntil;
    private static int receivedButtons = -1, processedButtons = -1;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OculusGoAirMouse", "input-diagnostics.log");
    private static double Age(long time) => time == 0 ? -1 : Stopwatch.GetElapsedTime(time).TotalMilliseconds;
    static InputDiagnostics()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                await using var writer = new StreamWriter(FilePath, append: false) { AutoFlush = true };
                int characters = 0;
                await foreach (string line in lines.Reader.ReadAllAsync())
                    if (characters < 1_000_000) { await writer.WriteLineAsync(line); characters += line.Length + 2; }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        });
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync())
                if (Stopwatch.GetTimestamp() < Volatile.Read(ref captureUntil))
                    Write($"health received={Interlocked.Read(ref received)} processed={Interlocked.Read(ref processed)} receiveAgeMs={Age(Interlocked.Read(ref lastReceived)):F0} processAgeMs={Age(Interlocked.Read(ref lastProcessed)):F0} uiAgeMs={Age(Interlocked.Read(ref lastUi)):F0}");
        });
    }
    public static void Write(string message) => lines.Writer.TryWrite($"{DateTimeOffset.Now:HH:mm:ss.fff} thread={Environment.CurrentManagedThreadId} {message}");
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
}
