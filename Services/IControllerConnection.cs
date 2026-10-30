using OculusGoAirMouse.Models;

namespace OculusGoAirMouse.Services;

public interface IControllerConnection
{
    bool Connected { get; }
    event Action<string>? Progress;
    event Action<byte[], bool>? Frame;
    event Action? Disconnected;
    event Action? LinkInterrupted;
    event Action? LinkRecovered;
    Task ConnectAsync(Settings settings, CancellationToken cancellationToken = default);
    void CancelPending();
    Task StopNotificationsAsync();
    void Disconnect();
}
