using OculusGoAirMouse.Models;

namespace OculusGoAirMouse.Services;

public sealed class ControllerRuntime : IControllerRuntime
{
    private readonly EngineWorker engine;
    private readonly ControllerConnection connection = new();
    public EngineSnapshot Snapshot => engine.Snapshot;
    public bool Connected => connection.Connected;
    public event Action<string>? Progress;
    public event Action<Exception>? Faulted;
    public event Action? Disconnected;
    public event Action<bool>? LinkChanged;

    public ControllerRuntime(IMouseOutput output)
    {
        engine = new(output);
        connection.Frame += engine.PostFrame;
        connection.Progress += text => Progress?.Invoke(text);
        connection.Disconnected += () => Disconnected?.Invoke();
        engine.Faulted += error => Faulted?.Invoke(error);
        connection.LinkInterrupted += () => OnLinkChanged(false);
        connection.LinkRecovered += () => OnLinkChanged(true);
    }
    private void OnLinkChanged(bool recovered)
    {
        // Preserve the independent input safety path, even when the view is minimized.
        Task safety = recovered ? engine.StartSessionAsync() : engine.StopSessionAsync();
        _ = safety.ContinueWith(task =>
        {
            if (task.IsFaulted) InputDiagnostics.Write("link safety error=" + task.Exception!.GetBaseException().Message);
        }, TaskScheduler.Default);
        LinkChanged?.Invoke(recovered);
    }
    public Task ApplyAsync(Settings settings) => engine.ApplyAsync(settings);
    public Task ToggleAsync() => engine.ToggleAsync();
    public Task CalibrateAsync() => engine.CalibrateAsync();
    public async Task ConnectAsync(Settings settings)
    {
        await engine.StartSessionAsync();
        try { await connection.ConnectAsync(settings.Copy()); }
        catch { await engine.StopSessionAsync(); throw; }
    }
    public async Task DisconnectAsync(string reason)
    {
        InputDiagnostics.Write("app stop reason=" + reason);
        var release = engine.StopSessionAsync();
        connection.Disconnect();
        await release;
    }
    public async ValueTask DisposeAsync()
    {
        try { await DisconnectAsync("程序关闭"); }
        finally { await engine.DisposeAsync(); }
    }
}
