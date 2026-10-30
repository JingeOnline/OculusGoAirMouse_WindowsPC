using System.Buffers.Binary;
using System.Collections.Concurrent;
using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;

static class DesktopChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        var mouse = new DesktopMouse(); var worker = new EngineWorker(mouse);
        await worker.ApplyAsync(new Settings()); await worker.StartSessionAsync(); await worker.ToggleAsync();
        int faults = 0; worker.Faulted += _ => Interlocked.Increment(ref faults);
        var motion = new byte[20];
        for (int i = 0; i < 9; i += 3) BinaryPrimitives.WriteInt16LittleEndian(motion.AsSpan(2 + i * 2), 9000);
        byte[] Button(byte bits) { var packet = new byte[20]; packet[19] = bits; return packet; }
        worker.PostFrame(Button(4), false);
        await Until(() => mouse.Events.Contains(2u));
        mouse.Available = false;
        await Until(() => worker.Snapshot.Status.Contains("等待解锁"));
        Check(worker.Snapshot.Enabled && faults == 0, "Unavailable desktop disabled mouse or caused fatal failure");
        int calls = mouse.Events.Count;
        worker.PostFrame(motion, true); worker.PostFrame(Button(1), false); await Task.Delay(100);
        Check(mouse.Events.Count == calls && mouse.Centers == 0, "Paused desktop injected motion/click/centering");
        Check(mouse.PendingUp, "Blocked desktop lost pending button release");
        mouse.Available = true;
        await Until(() => !worker.Snapshot.Status.Contains("等待解锁"));
        Check(mouse.Flushed == 1 && faults == 0 && worker.Snapshot.Enabled, "Desktop recovery did not flush release and preserve enable");
        calls = mouse.Events.Count;
        worker.PostFrame(Button(4), false); await Task.Delay(40);
        Check(mouse.Events.Count == calls, "Held button was replayed after desktop recovery");
        worker.PostFrame(Button(0), false); worker.PostFrame(motion, true);
        await Until(() => mouse.Events.Contains(1u));
        Check(faults == 0, "Desktop recovery became fatal");
        mouse.RaceFailure = true; worker.PostFrame(motion, true);
        await Until(() => worker.Snapshot.Status.Contains("等待解锁"));
        Check(worker.Snapshot.Enabled && faults == 0, "Desktop-switch race stopped connection");
        mouse.Available = true;
        await Until(() => !worker.Snapshot.Status.Contains("等待解锁"));
        Check(worker.Snapshot.Enabled, "Race recovery lost mouse switch");
        mouse.Available = false; await Until(() => worker.Snapshot.Status.Contains("等待解锁"));
        await worker.DisposeAsync(); Check(!worker.IsRunning && faults == 0, "Shutdown on unavailable desktop leaked worker");
        return count;
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
    private sealed class DesktopMouse : IMouseOutput
    {
        public volatile bool Available = true, RaceFailure, PendingUp;
        public bool IsInputAvailable => Available;
        public ConcurrentQueue<uint> Events { get; } = new();
        public int Flushed, Centers;
        public void FlushPendingReleases() { if (PendingUp) { Events.Enqueue(4); PendingUp = false; Flushed++; } }
        public void Send(uint flags, int dx = 0, int dy = 0, int data = 0)
        {
            if (RaceFailure) { RaceFailure = false; Available = false; throw new InputDesktopUnavailableException(); }
            if (!Available) { if (flags == 4) { PendingUp = true; return; } throw new InputDesktopUnavailableException(); }
            Events.Enqueue(flags);
        }
        public void Center() => Centers++;
    }
}
