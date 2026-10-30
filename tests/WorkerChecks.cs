using System.Collections.Concurrent;
using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;

static class WorkerChecks
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool result, string message) { if (!result) throw new Exception(message); checks++; }
        var mouse = new RecordingMouse();
        var worker = new EngineWorker(mouse);
        var settings = new Settings { CalibrationSamples = 10, ButtonTimeout = .25 };
        await worker.ApplyAsync(settings);
        await worker.StartSessionAsync();
        for (int i = 0; i < 10; i++) worker.PostFrame(new byte[20], true);
        await worker.ToggleAsync();
        Check(worker.Snapshot.Enabled && worker.Snapshot.Calibrated, "Worker calibration ordering");

        byte[] Buttons(byte value) { var bytes = new byte[20]; bytes[19] = value; return bytes; }
        worker.PostFrame(Buttons(4), false);
        Check(mouse.Down.Wait(1000), "Background mouse-down not delivered");
        _ = Task.Run(async () => { await Task.Delay(40); worker.PostFrame(Buttons(0), false); });
        // No dispatcher or synchronization-context pumping on the calling thread.
        // This represents a title-bar tracking loop starving WPF callbacks.
        Thread.Sleep(180);
        Check(mouse.Up.IsSet, "Mouse-up depended on the blocked caller/UI thread");
        Check(mouse.Threads.Distinct().Count() == 1, "Native mouse calls not serialized");
        Check(!mouse.Threads.Contains(Environment.CurrentManagedThreadId), "Input ran on caller thread");

        mouse.Down.Reset(); mouse.Up.Reset(); worker.PostFrame(Buttons(4), false);
        Check(mouse.Down.Wait(1000), "Second mouse-down not delivered");
        Thread.Sleep(450);
        Check(mouse.Up.IsSet, "Watchdog depended on the UI thread");

        mouse.Down.Reset(); mouse.Up.Reset(); worker.PostFrame(Buttons(4), false);
        Check(mouse.Down.Wait(1000), "Third mouse-down not delivered");
        await worker.StopSessionAsync();
        Check(mouse.Up.IsSet && !worker.Snapshot.Enabled, "Disconnect did not release drag");
        int count = mouse.Threads.Count;
        worker.PostFrame(Buttons(1), false);
        await worker.ApplyAsync(settings);
        Check(mouse.Threads.Count == count && mouse.Centers == 0, "Stopped session accepted stale frames");

        await worker.DisposeAsync();
        Check(!worker.IsRunning, "Dispose returned before input thread exited");
        worker.PostFrame(Buttons(4), false);
        Check(mouse.Threads.Count == count, "Disposed worker accepted frames");
        return checks;
    }

    private sealed class RecordingMouse : IMouseOutput
    {
        public ConcurrentQueue<int> Threads { get; } = new();
        public ManualResetEventSlim Down { get; } = new();
        public ManualResetEventSlim Up { get; } = new();
        public int Centers;
        public void Send(uint flags, int dx = 0, int dy = 0, int data = 0)
        {
            Threads.Enqueue(Environment.CurrentManagedThreadId);
            if (flags == 2) Down.Set();
            if (flags == 4) Up.Set();
        }
        public void Center() { Threads.Enqueue(Environment.CurrentManagedThreadId); Centers++; }
    }
}
