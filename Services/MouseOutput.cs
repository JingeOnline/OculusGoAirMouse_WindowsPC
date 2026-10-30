using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OculusGoAirMouse.Services;

public interface IMouseOutput
{
    bool IsInputAvailable => true;
    void FlushPendingReleases() { }
    void Send(uint flags, int dx = 0, int dy = 0, int data = 0);
    void Center();
    void CancelPendingClick() { }
}

public sealed class InputDesktopUnavailableException : Exception
{
    public InputDesktopUnavailableException() : base("输入桌面暂不可用，等待解锁后恢复。") { }
}

public sealed class MouseOutput : IMouseOutput
{
    private uint pendingReleases;
    public bool IsInputAvailable
    {
        get
        {
            nint input = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS
            if (input == 0) return false;
            try
            {
                var inputName = new StringBuilder(256); var ownName = new StringBuilder(256);
                return GetUserObjectInformation(input, 2, inputName, 512, out _)
                    && GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, ownName, 512, out _)
                    && string.Equals(inputName.ToString(), ownName.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseDesktop(input); }
        }
    }
    public void FlushPendingReleases()
    {
        if (pendingReleases == 0) return;
        uint flags = pendingReleases;
        pendingReleases = 0;
        try { Send(flags); }
        catch { pendingReleases |= flags; throw; }
    }
    [DllImport("user32.dll")] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint="GetUserObjectInformationW", CharSet=CharSet.Unicode)] private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder value, uint size, out uint needed);
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
        if (!IsInputAvailable)
        {
            pendingReleases |= flags & (4u | 16u);
            if ((flags & ~(4u | 16u)) == 0) return;
            throw new InputDesktopUnavailableException();
        }
        if (captionClicks.TryHandle(flags)) return;
        var input = new Input { Mouse = new MouseInput { X = dx, Y = dy, Flags = flags, Data = unchecked((uint)data) } };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
        {
            int error = Marshal.GetLastWin32Error();
            pendingReleases |= flags & (4u | 16u);
            if (!IsInputAvailable) throw new InputDesktopUnavailableException();
            throw new Win32Exception(error, "无法发送鼠标输入，请检查目标窗口权限。");
        }
    }
    public void Center()
    {
        if (!IsInputAvailable) throw new InputDesktopUnavailableException();
        if (!SetCursorPos(GetSystemMetrics(0) / 2, GetSystemMetrics(1) / 2))
        {
            int error = Marshal.GetLastWin32Error();
            if (!IsInputAvailable) throw new InputDesktopUnavailableException();
            throw new Win32Exception(error);
        }
    }
}
