using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OculusGoAirMouse;

public interface IMouseOutput
{
    void Send(uint flags, int dx = 0, int dy = 0, int data = 0);
    void Center();
    void CancelPendingClick() { }
}

public sealed class MouseOutput : IMouseOutput
{
    private readonly NativeCaptionWindow captionWindow = new();
    private readonly CaptionClickRouter captionClicks;
    public event Action<uint>? CaptionCommandPosted;
    public MouseOutput()
    {
        captionClicks = new(captionWindow);
        captionClicks.CommandPosted += command => CaptionCommandPosted?.Invoke(command);
    }
    public void SetMainWindowHandle(nint handle) => captionWindow.SetHandle(handle);
    public void RefreshCaptionRegions() => captionWindow.RefreshRegions();
    public void CancelPendingClick() => captionClicks.Cancel();
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public MouseInput Mouse; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    public void Send(uint flags, int dx = 0, int dy = 0, int data = 0)
    {
        if (captionClicks.TryHandle(flags)) return;
        var input = new Input { Mouse = new MouseInput { X = dx, Y = dy, Flags = flags, Data = unchecked((uint)data) } };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法发送鼠标输入，请检查目标窗口权限。");
    }
    public void Center()
    {
        if (!SetCursorPos(GetSystemMetrics(0) / 2, GetSystemMetrics(1) / 2)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
