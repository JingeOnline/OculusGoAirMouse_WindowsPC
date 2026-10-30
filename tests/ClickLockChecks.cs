using System.Buffers.Binary;
using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;

static class ClickLockChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string text) { if (!value) throw new Exception(text); count++; }
        foreach (byte button in new byte[] { 4, 2 })
        {
            double now = 100;
            var mouse = new Mouse(); var engine = new AirMouseEngine(mouse, () => now);
            var settings = new Settings { Smoothing = .1, DeadZone = 0 };
            engine.Apply(settings); engine.Enable();
            var motion = new byte[20];
            for (int i = 0; i < 9; i += 3) BinaryPrimitives.WriteInt16LittleEndian(motion.AsSpan(2 + 2 * i), 9000);
            void Frame(double seconds, bool moving = true) { now += seconds; engine.Gyro(moving ? motion : new byte[20]); }
            void Button(byte value) { var packet = new byte[20]; packet[19] = value; engine.Sensor(packet); }
            Frame(.01); Check(mouse.Flags.Contains(1), "Normal movement suppressed before press"); mouse.Flags.Clear();
            Button(button);
            Check(mouse.Flags.SequenceEqual(new[] { button == 4 ? 2u : 8u }), "Lock delayed button down");
            Frame(.099); Check(!mouse.Flags.Contains(1), "Press lock leaked gyro movement");
            Button(button); Frame(.002);
            Check(mouse.Flags.Contains(1), "Held packets extended lock or prevented dragging");
            mouse.Flags.Clear(); Button(0);
            Check(mouse.Flags.SequenceEqual(new[] { button == 4 ? 4u : 16u }), "Lock delayed button up");
            Frame(.099); Check(!mouse.Flags.Contains(1), "Release lock leaked movement");
            Button(0); Frame(.002, false);
            Check(!mouse.Flags.Contains(1), "Unlock replayed filtered motion or accumulated movement");
            Frame(.01); Check(mouse.Flags.Contains(1), "Movement did not resume after release lock");
            settings.ClickPressLockMilliseconds = 0; settings.ClickReleaseLockMilliseconds = 0;
            engine.Apply(settings); engine.Enable(); mouse.Flags.Clear(); Button(button); Frame(.001);
            Check(mouse.Flags.Contains(1), "Zero press duration did not disable lock");
            mouse.Flags.Clear(); Button(0); Frame(.001);
            Check(mouse.Flags.Contains(1), "Zero release duration did not disable lock");
            settings.ClickPressLockMilliseconds = 200; settings.ClickReleaseLockMilliseconds = 50;
            engine.Apply(settings); engine.Enable(); Button(button); Frame(.201); mouse.Flags.Clear();
            Button(0); Frame(.049); Check(!mouse.Flags.Contains(1), "Custom release duration ignored");
            Frame(.002); Check(mouse.Flags.Contains(1), "Custom release lock failed to expire");
        }
        foreach (int invalid in new[] { -1, 2001 })
        {
            try { new Settings { ClickPressLockMilliseconds = invalid }.Validate(); throw new Exception("Invalid press lock accepted"); }
            catch (ArgumentException) { count++; }
            try { new Settings { ClickReleaseLockMilliseconds = invalid }.Validate(); throw new Exception("Invalid release lock accepted"); }
            catch (ArgumentException) { count++; }
        }
        string path = Path.Combine(AppContext.BaseDirectory, "scratch", "click-lock.json");
        SettingsStore.Save(new Settings { ClickPressLockMilliseconds = 150, ClickReleaseLockMilliseconds = 80 }, path);
        var loaded = SettingsStore.Load(path);
        Check(loaded.ClickPressLockMilliseconds == 150 && loaded.ClickReleaseLockMilliseconds == 80, "Lock settings did not roundtrip");
        return count;
    }
    private sealed class Mouse : IMouseOutput
    {
        public List<uint> Flags { get; } = [];
        public void Send(uint flags, int dx = 0, int dy = 0, int data = 0) => Flags.Add(flags);
        public void Center() { }
    }
}
