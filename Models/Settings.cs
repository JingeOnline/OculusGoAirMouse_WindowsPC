
namespace OculusGoAirMouse.Models;

public sealed class Settings
{
    public bool AutoConnectOnStartup { get; set; } = true;
    public bool AutoCalibrateOnConnect { get; set; } = false;
    public bool AutoReconnect { get; set; } = true;
    public string DeviceName { get; set; } = "OMVR-V190";
    public double SensitivityX { get; set; } = 1.75;
    public double SensitivityY { get; set; } = 1.25;
    public double DeadZone { get; set; } = 65;
    public int XAxis { get; set; }
    public int YAxis { get; set; } = 2;
    public bool XInvert { get; set; } = true;
    public bool YInvert { get; set; }
    public double Smoothing { get; set; } = 0.8;
    public int MaxMove { get; set; } = 100;
    public double CalibrationDurationSeconds { get; set; } = 1.25;
    public int ClickPressLockMilliseconds { get; set; } = 100;
    public int ClickReleaseLockMilliseconds { get; set; } = 100;
    public double ButtonTimeout { get; set; } = 3;
    public double ScrollUnitsPerNotch { get; set; } = 30;
    public bool ScrollVerticalInvert { get; set; }
    public bool ScrollHorizontalInvert { get; set; } = true;
    public int ScanTimeoutSeconds { get; set; } = 15;
    public int ConnectTimeoutSeconds { get; set; } = 30;

    public void Validate()
    {
        static bool Range(double v, double min, double max) => double.IsFinite(v) && v >= min && v <= max;
        if (ClickPressLockMilliseconds is < 0 or > 2000 || ClickReleaseLockMilliseconds is < 0 or > 2000) throw new ArgumentException("点击光标锁定时长范围为 0–2000 毫秒，0 表示关闭。");
        if (string.IsNullOrWhiteSpace(DeviceName)) throw new ArgumentException("控制器名称不能为空。");
        if (!Range(SensitivityX, 0.001, 100) || !Range(SensitivityY, 0.001, 100)) throw new ArgumentException("灵敏度范围为 0.001–100。");
        if (!Range(DeadZone, 0, 32767)) throw new ArgumentException("死区范围为 0–32767。");
        if (XAxis is < 0 or > 2 || YAxis is < 0 or > 2) throw new ArgumentException("轴映射只能为 0、1、2。");
        if (!Range(Smoothing, 0.001, 1)) throw new ArgumentException("光标平滑系数范围为 0.001–1。");
        if (MaxMove is < 1 or > 2000) throw new ArgumentException("最大位移范围 1–2000。");
        if (!Range(CalibrationDurationSeconds, 0.1, 3)) throw new ArgumentException("校准时长范围为 0.1–3 秒。");
        if (!Range(ButtonTimeout, 0.25, 60) || !Range(ScrollUnitsPerNotch, 1, 1000)) throw new ArgumentException("按键超时范围 0.25–60 秒，滚动阈值范围 1–1000。");
        if (ScanTimeoutSeconds is < 3 or > 120 || ConnectTimeoutSeconds is < 5 or > 120) throw new ArgumentException("扫描超时范围 3–120 秒，连接超时范围 5–120 秒。");
    }
    public Settings Copy() => (Settings)MemberwiseClone();
}
