using OculusGoAirMouse;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

int checks = 0;
void Check(bool result, string message) { if (!result) throw new Exception(message); checks++; }
var output = new FakeMouse();
var engine = new AirMouseEngine(output);
var config = new Settings();
config.Validate();
Check(config.SensitivityX == 1.75 && config.SensitivityY == 1.25 && config.DeadZone == 65, "Migrated movement values");
Check(config.ScrollUnitsPerNotch == 30 && config.ScrollHorizontalInvert && !config.ScrollVerticalInvert, "Migrated scroll values");
engine.Apply(config);
try { engine.Enable(); throw new Exception("Enable before calibration accepted"); } catch (InvalidOperationException) { checks++; }
for (int i = 0; i < 249; i++) engine.Gyro(new byte[20]);
Check(!engine.Calibrated, "Calibration completed too early");
engine.Gyro(new byte[20]); Check(engine.Calibrated, "Calibration not completed"); engine.Enable();
byte[] Packet(int x, int y, byte buttons = 16)
{
    var p = new byte[20]; BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(14), (ushort)x); BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(16), (ushort)y); p[19] = buttons; return p;
}
engine.Sensor(Packet(100, 100)); engine.Sensor(Packet(115, 115)); Check(output.Events.Count == 0, "Scroll below threshold");
engine.Sensor(Packet(130, 130)); Check(output.Events.SequenceEqual(new[] { (0x800u, 120), (0x1000u, -120) }), "Whole-notch both axes");
output.Events.Clear(); engine.Sensor(Packet(100, 100)); Check(output.Events.SequenceEqual(new[] { (0x800u, -120), (0x1000u, 120) }), "Reverse scroll");
engine.Sensor(Packet(0, 0, 0)); output.Events.Clear(); engine.Sensor(Packet(300, 300)); Check(output.Events.Count == 0, "Retouch jumps");
engine.Sensor(Packet(300, 300, 4)); engine.Sensor(Packet(300, 300, 0)); engine.Sensor(Packet(300, 300, 2)); engine.Disable();
Check(output.Events.SequenceEqual(new[] { (2u, 0), (4u, 0), (8u, 0), (16u, 0) }), "Buttons and disable release");
output.Events.Clear(); engine.Sensor(Packet(0, 0, 1)); engine.Sensor(Packet(0, 0, 1)); Check(output.Centers == 1, "Home edge detection");
engine.Sensor(Packet(0, 0, 0)); engine.Sensor(Packet(0, 0, 1)); Check(output.Centers == 2, "Home second press");
engine.Enable(); engine.Sensor(Packet(0, 0, 4)); engine.Calibrate(); Check(output.Events.Last() == (4u, 0) && !engine.Enabled && !engine.Calibrated, "Calibration releases drag");
engine.Gyro(new byte[1]); engine.Sensor(new byte[1]); checks++;
var invalid = config.Copy(); invalid.Smoothing = double.NaN;
try { invalid.Validate(); throw new Exception("NaN accepted"); } catch (ArgumentException) { checks++; }
invalid = config.Copy(); invalid.XAxis = 3;
try { invalid.Validate(); throw new Exception("Invalid axis accepted"); } catch (ArgumentException) { checks++; }
string folder = Path.Combine(AppContext.BaseDirectory, "scratch"), path = Path.Combine(folder, "settings.json");
SettingsStore.Save(config, path); var loaded = SettingsStore.Load(path); Check(loaded.SensitivityX == 1.75 && loaded.ScrollHorizontalInvert, "JSON roundtrip");
loaded.SensitivityX = 2.5; SettingsStore.Save(loaded, path); Check(SettingsStore.Load(path).SensitivityX == 2.5 && !File.Exists(path + ".tmp"), "Atomic update");
File.WriteAllText(path, "broken");
try { SettingsStore.Load(path); throw new Exception("Corrupt JSON accepted"); } catch (System.Text.Json.JsonException) { Check(File.ReadAllText(path) == "broken", "Corrupt file overwritten"); }
var input = typeof(MouseOutput).GetNestedType("Input", System.Reflection.BindingFlags.NonPublic)!;
Check(Marshal.SizeOf(input) == (IntPtr.Size == 8 ? 40 : 28), "Win32 INPUT layout");
checks += await WorkerChecks.RunAsync();
checks += CaptionChecks.Run();
checks += await ConnectionChecks.RunAsync();
Console.WriteLine($"PASS: {checks} migration, input-thread independence, watchdog, calibration, scrolling, button, configuration and native-layout checks.");

sealed class FakeMouse : IMouseOutput
{
    public List<(uint, int)> Events { get; } = [];
    public int Centers { get; private set; }
    public void Send(uint flags, int dx = 0, int dy = 0, int data = 0) => Events.Add((flags, data));
    public void Center() => Centers++;
}
