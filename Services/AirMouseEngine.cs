using OculusGoAirMouse.Models;
using System.Buffers.Binary;
using System.Diagnostics;

namespace OculusGoAirMouse.Services;

public sealed class AirMouseEngine(IMouseOutput output)
{
    private Settings settings = new();
    private readonly double[] bias = new double[3], sum = new double[3], filtered = new double[3];
    private int sampleCount;
    private double lastTime, lastSensor, remainingX, remainingY, scrollX, scrollY;
    private int? touchX, touchY;
    private bool left, right, oculus;
    public bool Enabled { get; private set; }
    public bool Calibrated { get; private set; }
    public string Status => Calibrated ? (Enabled ? "鼠标控制已开启" : "校准完成，可以开启鼠标") : $"静止校准：{sampleCount}/{settings.CalibrationSamples}";
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    public void Apply(Settings value) { Disable(); settings = value.Copy(); Calibrate(); }
    public void Calibrate()
    {
        Disable(); Calibrated = false; sampleCount = 0;
        Array.Clear(bias); Array.Clear(sum); Array.Clear(filtered); lastTime = 0;
    }
    public void Enable() { if (!Calibrated) throw new InvalidOperationException("请先完成静止校准。"); Enabled = true; ResetMotion(); }
    public void Disable() { Enabled = false; ResetMotion(); Release(); }
    private void ResetMotion() { remainingX = remainingY = 0; ResetScroll(); }
    private void ResetScroll() { touchX = touchY = null; scrollX = scrollY = 0; }
    public void Release()
    {
        ResetScroll();
        output.CancelPendingClick();
        try { if (left) { output.Send(4); left = false; } }
        finally { if (right) { output.Send(16); right = false; } }
    }
    public void CheckTimeout() { if (lastSensor > 0 && Now - lastSensor > settings.ButtonTimeout) { Release(); oculus = false; } }
    public void Sensor(byte[] data)
    {
        if (data.Length != 20) return;
        var now = Now;
        if (lastSensor > 0 && now - lastSensor > settings.ButtonTimeout) Release();
        lastSensor = now;
        byte buttons = data[19];
        bool home = (buttons & 1) != 0;
        if (home && !oculus) { output.Center(); remainingX = remainingY = 0; }
        oculus = home;
        if (!Enabled || !Calibrated) { Release(); return; }
        if ((buttons & 16) == 0) ResetScroll();
        else
        {
            int x = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14)), y = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(16));
            if (touchX.HasValue && touchY.HasValue)
            {
                scrollX += (x - touchX.Value) * (settings.ScrollHorizontalInvert ? -1 : 1);
                scrollY += (y - touchY.Value) * (settings.ScrollVerticalInvert ? -1 : 1);
                int nx = (int)(scrollX / settings.ScrollUnitsPerNotch), ny = (int)(scrollY / settings.ScrollUnitsPerNotch);
                if (ny != 0) { output.Send(0x800, data: ny * 120); scrollY -= ny * settings.ScrollUnitsPerNotch; }
                if (nx != 0) { output.Send(0x1000, data: nx * 120); scrollX -= nx * settings.ScrollUnitsPerNotch; }
            }
            touchX = x; touchY = y;
        }
        bool trigger = (buttons & 4) != 0, back = (buttons & 2) != 0;
        if (trigger != left) { output.Send(trigger ? 2u : 4u); left = trigger; }
        if (back != right) { output.Send(back ? 8u : 16u); right = back; }
    }
    public void Gyro(byte[] data)
    {
        if (data.Length != 20) return;
        var values = new short[9];
        for (int i = 0; i < 9; i++) values[i] = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(2 + i * 2));
        if (values[0] == -10922 && values[1] == -10922 && values[2] == -10922) return;
        var gyro = new double[3];
        for (int i = 0; i < 3; i++) gyro[i] = (values[i] + values[i + 3] + values[i + 6]) / 3.0;
        if (!Calibrated)
        {
            for (int i = 0; i < 3; i++) sum[i] += gyro[i];
            if (++sampleCount >= settings.CalibrationSamples) { for (int i = 0; i < 3; i++) bias[i] = sum[i] / sampleCount; Calibrated = true; lastTime = Now; }
            return;
        }
        for (int i = 0; i < 3; i++)
        {
            double corrected = gyro[i] - bias[i];
            if (Math.Abs(corrected) < settings.DeadZone) corrected = 0;
            filtered[i] = settings.Smoothing * corrected + (1 - settings.Smoothing) * filtered[i];
            if (Math.Abs(filtered[i]) < settings.DeadZone) filtered[i] = 0;
        }
        double now = Now, dt = Math.Clamp(now - lastTime, .001, .03); lastTime = now;
        if (!Enabled) { ResetMotion(); return; }
        remainingX += filtered[settings.XAxis] * (settings.XInvert ? -1 : 1) * dt * settings.SensitivityX;
        remainingY += filtered[settings.YAxis] * (settings.YInvert ? -1 : 1) * dt * settings.SensitivityY;
        int dx = (int)remainingX, dy = (int)remainingY; remainingX -= dx; remainingY -= dy;
        dx = Math.Clamp(dx, -settings.MaxMove, settings.MaxMove); dy = Math.Clamp(dy, -settings.MaxMove, settings.MaxMove);
        if (dx != 0 || dy != 0) output.Send(1, dx, dy);
    }
}
