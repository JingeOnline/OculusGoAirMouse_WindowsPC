using System.Windows.Threading;

namespace OculusGoAirMouse.Services;

public sealed class WpfUiScheduler(Dispatcher dispatcher) : IUiScheduler
{
    public void Post(Action action)
    {
        if (!dispatcher.HasShutdownStarted) _ = dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }
    public IDisposable Every(TimeSpan interval, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = interval };
        timer.Tick += OnTick;
        timer.Start();
        return new Registration(() => { timer.Stop(); timer.Tick -= OnTick; });
        void OnTick(object? sender, EventArgs args) => action();
    }
    private sealed class Registration(Action stop) : IDisposable { public void Dispose() => stop(); }
}
