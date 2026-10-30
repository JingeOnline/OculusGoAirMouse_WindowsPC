using OculusGoAirMouse.Models;

namespace OculusGoAirMouse.Services;

public interface IControllerRuntime : IAsyncDisposable
{
    EngineSnapshot Snapshot { get; }
    bool Connected { get; }
    event Action<string>? Progress;
    event Action<Exception>? Faulted;
    event Action? Disconnected;
    event Action<bool>? LinkChanged;
    Task ApplyAsync(Settings settings);
    Task ConnectAsync(Settings settings);
    Task DisconnectAsync(string reason);
    Task ToggleAsync();
    Task CalibrateAsync();
}

public interface ISettingsStore
{
    string FilePath { get; }
    Settings Load();
    void Save(Settings settings);
    bool Exists { get; }
}

public sealed class JsonSettingsStore : ISettingsStore
{
    public string FilePath => SettingsStore.FilePath;
    public bool Exists => System.IO.File.Exists(FilePath);
    public Settings Load() => SettingsStore.Load();
    public void Save(Settings settings) => SettingsStore.Save(settings);
}

public interface IUiScheduler
{
    void Post(Action action);
    IDisposable Every(TimeSpan interval, Action action);
}
