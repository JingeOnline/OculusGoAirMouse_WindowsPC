using OculusGoAirMouse.Models;
using System.Buffers.Binary;
using System.Diagnostics;

namespace OculusGoAirMouse.Services;

public sealed class AirMouseEngine(IMouseOutput output, Func<double>? clock = null)
{
    public const double OculusLongPressSeconds = 1.0;
    private Settings settings = new();
    private readonly double[] bias = new double[3], sum = new double[3], filtered = new double[3];
    private int sampleCount;
    private double calibrationStarted, lastCalibrationSample;
    private double lastTime, lastSensor, remainingX, remainingY, scrollX, scrollY;
    private int? touchX, touchY;
    private bool left, right, oculus;
    private double oculusPressedAt, pointerLockedUntil;
    private bool oculusLongPressHandled;
    public bool Enabled { get; private set; }
    public bool Calibrated { get; private set; }
    public bool IsCalibrating { get; private set; }
    public double CalibrationProgress => Calibrated ? 1 : Math.Clamp((lastCalibrationSample - calibrationStarted) / settings.CalibrationDurationSeconds, 0, 1);
    public string Status => Calibrated ? (Enabled ? "鼠标控制已开启" : "校准完成，可以开启鼠标")
        : !IsCalibrating ? (Enabled ? "鼠标控制已开启（未校准）" : "鼠标已关闭（未校准），可直接开启或手动校准。") : $"静止校准：{CalibrationProgress:P0} / {settings.CalibrationDurationSeconds:0.##} 秒" + (Enabled ? "（完成后继续控制）" : "");
    private double Now => clock?.Invoke() ?? Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    public void Apply(Settings value)
    {
        Disable(); settings = value.Copy(); Calibrated = false;
        Array.Clear(bias); Array.Clear(filtered); lastTime = Now;
        if (settings.AutoCalibrateOnConnect) Calibrate(); else CancelCalibration();
    }
    internal void CancelCalibration()
    {
        IsCalibrating = false; sampleCount = 0;
        calibrationStarted = lastCalibrationSample = Now;
    }
    internal void StartSession(bool restoreMouse, bool autoCalibrate)
    {
        Disable(); RestoreEnabled(restoreMouse);
        if (autoCalibrate) Calibrate();
        else { CancelCalibration(); Array.Clear(filtered); lastTime = Now; }
    }
    public void Calibrate()
    {
        ResetMotion(); Release(); Calibrated = false; IsCalibrating = true; sampleCount = 0;
        calibrationStarted = lastCalibrationSample = Now;
        Array.Clear(bias); Array.Clear(sum); Array.Clear(filtered); lastTime = 0;
        InputDiagnostics.Write("calibration started; mouseEnabled=" + Enabled);
    }
    internal void RestoreEnabled(bool enabled) { Enabled = enabled; ResetMotion(); }
    public void Enable() { Enabled = true; ResetMotion(); lastTime = Now; }
    public void Disable() { Enabled = false; ResetMotion(); Release(); ResetOculusGesture(); }
    private void ResetMotion() { pointerLockedUntil = 0; remainingX = remainingY = 0; ResetScroll(); }
    private void LockPointer(int milliseconds, double now)
    {
        if (milliseconds == 0) return;
        pointerLockedUntil = Math.Max(pointerLockedUntil, now + milliseconds / 1000.0);
        remainingX = remainingY = 0; Array.Clear(filtered); lastTime = now;
    }
    private void ResetScroll() { touchX = touchY = null; scrollX = scrollY = 0; }
    public void Release()
    {
        ResetScroll();
        output.CancelPendingClick();
        if (left || right) LockPointer(settings.ClickReleaseLockMilliseconds, Now);
        try { if (left) { output.Send(4); left = false; } }
        finally { if (right) { output.Send(16); right = false; } }
    }
    private void ResetOculusGesture() { oculus = false; oculusPressedAt = 0; oculusLongPressHandled = false; }
    private void CheckOculusLongPress(double now)
    {
        if (!oculus || oculusLongPressHandled || now - oculusPressedAt < OculusLongPressSeconds) return;
        oculusLongPressHandled = true;
        Calibrate();
        InputDiagnostics.Write("Oculus long press: recalibration");
    }
    private void HandleOculus(bool pressed, double now)
    {
        if (!pressed) { ResetOculusGesture(); return; }
        if (!oculus)
        {
            oculus = true; oculusPressedAt = now; oculusLongPressHandled = false;
            output.Center(); remainingX = remainingY = 0;
        }
        CheckOculusLongPress(now);
    }
    public void CheckTimeout()
    {
        double now = Now;
        if (lastSensor > 0 && now - lastSensor > settings.ButtonTimeout)
        {
            Release(); ResetOculusGesture(); return;
        }
        // The input worker calls this even when no new button packet arrives.
        CheckOculusLongPress(now);
    }
    public void Sensor(byte[] data)
    {
        if (data.Length != 20) return;
        var now = Now;
        if (lastSensor > 0 && now - lastSensor > settings.ButtonTimeout) { Release(); ResetOculusGesture(); }
        lastSensor = now;
        byte buttons = data[19];
        HandleOculus((buttons & 1) != 0, now);
        if (!Enabled || IsCalibrating) { Release(); return; }
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
        if (trigger != left) { LockPointer(trigger ? settings.ClickPressLockMilliseconds : settings.ClickReleaseLockMilliseconds, now); output.Send(trigger ? 2u : 4u); left = trigger; }
        if (back != right) { LockPointer(back ? settings.ClickPressLockMilliseconds : settings.ClickReleaseLockMilliseconds, now); output.Send(back ? 8u : 16u); right = back; }
    }
    public void Gyro(byte[] data)
    {
        if (data.Length != 20) return;
        var values = new short[9];
        for (int i = 0; i < 9; i++) values[i] = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(2 + i * 2));
        if (values[0] == -10922 && values[1] == -10922 && values[2] == -10922) return;
        var gyro = new double[3];
        for (int i = 0; i < 3; i++) gyro[i] = (values[i] + values[i + 3] + values[i + 6]) / 3.0;
        if (IsCalibrating)
        {
            double timestamp = Now;
            // A gap in valid data must not count as time spent measuring drift.
            if (sampleCount == 0 || timestamp - lastCalibrationSample > 0.25)
            {
                calibrationStarted = timestamp; sampleCount = 0; Array.Clear(sum);
            }
            lastCalibrationSample = timestamp;
            for (int i = 0; i < 3; i++) sum[i] += gyro[i];
            sampleCount++;
            if (sampleCount >= 2 && timestamp - calibrationStarted + 1e-9 >= settings.CalibrationDurationSeconds)
            {
                for (int i = 0; i < 3; i++) bias[i] = sum[i] / sampleCount;
                Calibrated = true; IsCalibrating = false; lastTime = timestamp;
            }
            return;
        }
        double now = Now;
        if (now < pointerLockedUntil)
        {
            // Drop movement instead of accumulating a jump for the unlock frame.
            remainingX = remainingY = 0; Array.Clear(filtered); lastTime = now;
            return;
        }
        for (int i = 0; i < 3; i++)
        {
            double corrected = gyro[i] - bias[i];
            if (Math.Abs(corrected) < settings.DeadZone) corrected = 0;
            filtered[i] = settings.Smoothing * corrected + (1 - settings.Smoothing) * filtered[i];
            if (Math.Abs(filtered[i]) < settings.DeadZone) filtered[i] = 0;
        }
        double dt = Math.Clamp(now - lastTime, .001, .03); lastTime = now;
        if (!Enabled) { ResetMotion(); return; }
        remainingX += filtered[settings.XAxis] * (settings.XInvert ? -1 : 1) * dt * settings.SensitivityX;
        remainingY += filtered[settings.YAxis] * (settings.YInvert ? -1 : 1) * dt * settings.SensitivityY;
        int dx = (int)remainingX, dy = (int)remainingY; remainingX -= dx; remainingY -= dy;
        dx = Math.Clamp(dx, -settings.MaxMove, settings.MaxMove); dy = Math.Clamp(dy, -settings.MaxMove, settings.MaxMove);
        if (dx != 0 || dy != 0) output.Send(1, dx, dy);
    }
}
