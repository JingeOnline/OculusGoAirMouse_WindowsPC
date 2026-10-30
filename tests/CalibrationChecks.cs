using System.Buffers.Binary;
using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;

static class CalibrationChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        double now = 100;
        var mouse = new RecordingMouse();
        var engine = new AirMouseEngine(mouse, () => now);
        engine.Apply(new Settings { CalibrationDurationSeconds = .1 });
        void Samples(int number) { engine.Gyro(new byte[20]); for (int i = 0; i < number; i++) { now += .01; engine.Gyro(new byte[20]); } }
        void Buttons(byte buttons) { var data = new byte[20]; data[19] = buttons; engine.Sensor(data); }
        Samples(10); engine.Enable(); Buttons(4); engine.Calibrate();
        Check(engine.Enabled && !engine.Calibrated && mouse.Flags.Last() == 4, "Manual recalibration lost enable or retained drag");
        Buttons(0); mouse.Flags.Clear(); Samples(5);
        Check(!engine.Calibrated && mouse.Flags.Count == 0 && Math.Abs(engine.CalibrationProgress - .5) < .001, "Calibration did not pause output");
        Samples(5); Check(engine.Calibrated && engine.Enabled, "Manual calibration did not resume enabled state");
        var motion = new byte[20];
        for (int i = 0; i < 9; i += 3) BinaryPrimitives.WriteInt16LittleEndian(motion.AsSpan(2 + i * 2), 7000);
        now += .01; engine.Gyro(motion);
        Check(mouse.Flags.Contains(1), "Movement did not resume after calibration");
        engine.Disable(); engine.Calibrate(); Samples(10);
        Check(engine.Calibrated && !engine.Enabled, "Calibration enabled previously disabled mouse");
        engine.Enable(); engine.Calibrate(); engine.Disable(); Samples(10);
        Check(engine.Calibrated && !engine.Enabled, "Calibration completion overrode manual disable");

        engine.Enable(); int centers = mouse.Centers; Buttons(1);
        Check(mouse.Centers == centers, "Home press centered before distinguishing long press");
        now += .4; Buttons(0);
        Check(mouse.Centers == centers + 1 && engine.Calibrated, "Short Home press did not center");

        now += .1; Buttons(1); now += .9; Buttons(1);
        Check(engine.Calibrated, "Long press fired too early");
        now += .2; Buttons(1);
        Check(!engine.Calibrated && engine.Enabled && mouse.Centers == centers + 1, "Long press did not calibrate or unexpectedly centered");
        Samples(4); Buttons(1);
        Check(Math.Abs(engine.CalibrationProgress - .4) < .001, "Held Home repeatedly restarted calibration");
        Samples(6); Buttons(1);
        Check(engine.Calibrated && engine.Enabled, "Long press calibration failed to resume");
        now += .2; Buttons(0);
        Check(mouse.Centers == centers + 1 && engine.Calibrated, "Long press release triggered short action");
        now += .1; Buttons(1); now += 1.1; Buttons(0);
        Check(!engine.Calibrated && engine.Enabled, "Long press missed when release is first threshold-crossing packet");
        Samples(10);

        engine.Disable(); now += .1; Buttons(1); now += 1.1; Buttons(1);
        Check(!engine.Calibrated && !engine.Enabled, "Long press while disabled changed enable state");
        Buttons(0); Samples(10);
        Check(engine.Calibrated && !engine.Enabled, "Disabled long calibration auto-enabled mouse");
        now += .1; Buttons(1); now += 3.1; engine.CheckTimeout(); Buttons(0);
        Check(engine.Calibrated && mouse.Centers == centers + 1, "Stale Home state triggered action after timeout");
        now += .1; Buttons(1); now += 3.1; Buttons(0);
        Check(engine.Calibrated && mouse.Centers == centers + 1, "Packet gap triggered stale gesture");
        now += .1; Buttons(1); now += .2; Buttons(0);
        Check(mouse.Centers == centers + 2, "Home did not recover after timeout");

        engine.Enable(); engine.Apply(new Settings { CalibrationDurationSeconds = .1 }); Samples(10);
        Check(!engine.Enabled, "Settings application unexpectedly restored mouse control");
        foreach (double duration in new[] { .1, .5, 1.25, 3.0 })
        {
            engine.Apply(new Settings { CalibrationDurationSeconds = duration });
            engine.Gyro(new byte[20]);
            int steps = (int)Math.Round(duration * 200);
            for (int i = 1; i < steps; i++) { now += .005; engine.Gyro(new byte[20]); }
            Check(!engine.Calibrated && engine.CalibrationProgress < 1, "Timed calibration finished early");
            now += .005; engine.Gyro(new byte[20]);
            Check(engine.Calibrated && engine.CalibrationProgress == 1, "Timed calibration missed configured duration");
        }
        engine.Apply(new Settings { CalibrationDurationSeconds = .1 });
        Samples(5); now += .5;
        engine.Gyro(new byte[1]);
        Check(!engine.Calibrated, "Missing data completed calibration");
        engine.Gyro(new byte[20]);
        Check(!engine.Calibrated && engine.CalibrationProgress == 0, "Data gap did not restart drift measurement");
        Samples(10); Check(engine.Calibrated, "Calibration failed after data resumed");
        foreach (double invalid in new[] { .099, 3.001, double.NaN, double.PositiveInfinity })
        {
            try { new Settings { CalibrationDurationSeconds = invalid }.Validate(); throw new Exception("Invalid duration accepted"); }
            catch (ArgumentException) { count++; }
        }
        string path = Path.Combine(AppContext.BaseDirectory, "scratch", "calibration-settings.json");
        SettingsStore.Save(new Settings { CalibrationDurationSeconds = .3 }, path);
        Check(SettingsStore.Load(path).CalibrationDurationSeconds == .3, "Duration did not roundtrip through JSON");
        File.WriteAllText(path, "{\"CalibrationSamples\":250,\"SensitivityX\":2.3}");
        var legacy = SettingsStore.Load(path);
        Check(legacy.CalibrationDurationSeconds == 1.25 && legacy.SensitivityX == 2.3, "Legacy settings lost existing values");
        return count;
    }
    private sealed class RecordingMouse : IMouseOutput
    {
        public List<uint> Flags { get; } = [];
        public int Centers;
        public void Send(uint flags, int dx = 0, int dy = 0, int data = 0) => Flags.Add(flags);
        public void Center() => Centers++;
    }
}
