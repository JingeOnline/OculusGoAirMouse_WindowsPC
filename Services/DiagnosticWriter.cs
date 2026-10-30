using System.IO;
using System.Threading.Channels;

namespace OculusGoAirMouse.Services;

public sealed class DiagnosticWriter : IAsyncDisposable
{
    private readonly Channel<string> lines = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Task writerTask;
    public bool IsCompleted => writerTask.IsCompleted;
    public DiagnosticWriter(string path) => writerTask = Task.Run(async () =>
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using var writer = new StreamWriter(path, append: false) { AutoFlush = true };
            int characters = 0;
            await foreach (string line in lines.Reader.ReadAllAsync().ConfigureAwait(false))
                if (characters < 1_000_000) { await writer.WriteLineAsync(line).ConfigureAwait(false); characters += line.Length + 2; }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    });
    public bool Write(string line) => lines.Writer.TryWrite(line);
    public ValueTask DisposeAsync() { lines.Writer.TryComplete(); return new(writerTask); }
}
