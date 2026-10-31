namespace OculusGoAirMouse.Services;

public sealed class PairingRequiredException(string deviceName) : InvalidOperationException(
    $"控制器“{deviceName}”尚未配对。请先在 Windows“设置 → 蓝牙和设备 → 添加设备 → 蓝牙”中完成配对，再点击“搜索并连接”。")
{
    public static void EnsurePaired(bool paired, string deviceName)
    {
        if (!paired) throw new PairingRequiredException(deviceName);
    }
}

public static class ConnectionFailure
{
    public static string Describe(Exception error)
    {
        if (unchecked((uint)error.HResult) == 0x8065000F)
            return "蓝牙链路加密不足（0x8065000F）。请先在 Windows 蓝牙设置中完成配对；若已配对，请移除设备后重新配对。";
        return string.IsNullOrWhiteSpace(error.Message)
            ? $"{error.GetType().Name}（错误码 0x{error.HResult:X8}），详情见 input-diagnostics.log。"
            : error.Message;
    }
}
