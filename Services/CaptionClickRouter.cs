using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OculusGoAirMouse.Services;

public interface ICaptionWindow
{
    int HitTest();
    bool IsMaximized { get; }
    void PostCommand(uint command);
}

// Own-window caption clicks bypass native mouse-down tracking entirely. A normal
// mouse-up commits the command; watchdog/disconnect releases must only cancel it.
public sealed class CaptionClickRouter(ICaptionWindow window)
{
    public event Action<uint>? CommandPosted;
    private int pending;
    private bool cancelled;
    public bool TryHandle(uint flags)
    {
        if (flags == 2)
        {
            pending = 0; cancelled = false;
            int hit = window.HitTest();
            if (hit is not (8 or 9 or 20)) return false;
            pending = hit;
            return true;
        }
        if (flags != 4) return false;
        if (cancelled) { cancelled = false; return true; }
        if (pending == 0) return false;
        int original = pending; pending = 0;
        if (window.HitTest() == original)
        {
            uint command = original switch
            {
                8 => 0xF020, // SC_MINIMIZE
                9 => window.IsMaximized ? 0xF120u : 0xF030u,
                _ => 0xF060 // SC_CLOSE
            };
            window.PostCommand(command);
            CommandPosted?.Invoke(command);
        }
        return true;
    }
    public void Cancel()
    {
        if (pending != 0) { pending = 0; cancelled = true; }
    }
}

public sealed class NativeCaptionWindow : ICaptionWindow
{
    private nint handle;
    private CaptionRegions regions = CaptionRegions.Empty;
    public void SetHandle(nint value) => Volatile.Write(ref handle, value);
    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
    {
        public bool Contains(int x, int y) => Right > Left && Bottom > Top && x >= Left && x < Right && y >= Top && y < Bottom;
    }
    public sealed record CaptionRegions(Rect Minimize, Rect Maximize, Rect Close)
    {
        public static readonly CaptionRegions Empty = new(default, default, default);
        public int HitTest(int x, int y) => Minimize.Contains(x, y) ? 8 : Maximize.Contains(x, y) ? 9 : Close.Contains(x, y) ? 20 : 0;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TitleBarInfo
    {
        public uint Size;
        public Rect Title;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public uint[] States;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public Rect[] Rectangles;
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint GetTitleBarInfo(nint hwnd, uint message, nuint wParam, ref TitleBarInfo info);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    // Called by the owning UI thread when the window moves, resizes or changes DPI.
    // The input thread reads an immutable snapshot and never asks the UI for geometry.
    public void RefreshRegions()
    {
        var hwnd = Volatile.Read(ref handle);
        if (hwnd == 0) return;
        if (GetWindowThreadProcessId(hwnd, out _) != GetCurrentThreadId()) throw new InvalidOperationException("标题栏区域必须由窗口线程更新。");
        if (IsIconic(hwnd)) { Volatile.Write(ref regions, CaptionRegions.Empty); return; }
        var info = new TitleBarInfo { Size = (uint)Marshal.SizeOf<TitleBarInfo>(), States = new uint[6], Rectangles = new Rect[6] };
        GetTitleBarInfo(hwnd, 0x33F /* WM_GETTITLEBARINFOEX */, 0, ref info);
        Rect Visible(int index) => (info.States[index] & 0x8001 /* INVISIBLE | UNAVAILABLE */) == 0 ? info.Rectangles[index] : default;
        var updated = new CaptionRegions(Visible(2), Visible(3), Visible(5));
        var previous = Volatile.Read(ref regions);
        Volatile.Write(ref regions, updated);
        if (updated != previous) InputDiagnostics.Write($"caption regions min={updated.Minimize} max={updated.Maximize} close={updated.Close}");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint hwnd, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    public bool IsMaximized => IsZoomed(Volatile.Read(ref handle));
    public int HitTest()
    {
        var hwnd = Volatile.Read(ref handle);
        if (hwnd == 0 || !GetCursorPos(out var point)) return 0;
        int region = Volatile.Read(ref regions).HitTest(point.X, point.Y);
        if (region != 0 && GetForegroundWindow() == hwnd)
        {
            InputDiagnostics.Capture($"caption geometry hit={region}");
            return region;
        }
        if (GetAncestor(WindowFromPoint(point), 2 /* GA_ROOT */) != hwnd) return 0;
        if (region != 0) { InputDiagnostics.Capture($"caption geometry hit={region}"); return region; }
        // WM_NCHITTEST uses signed 16-bit screen coordinates, including negative monitors.
        nint position = unchecked((nint)((point.Y & 0xffff) << 16 | (point.X & 0xffff)));
        if (SendMessageTimeout(hwnd, 0x84, 0, position, 0x22 /* ABORTIFHUNG | ERRORONEXIT */, 50, out var hit) == 0)
            throw new TimeoutException("窗口命中测试未响应，已停止此次控制器点击。");
        InputDiagnostics.Capture($"caption native hit={unchecked((int)hit)}");
        return unchecked((int)hit);
    }
    public void PostCommand(uint command)
    {
        InputDiagnostics.Capture($"caption command=0x{command:X}");
        if (!PostMessage(Volatile.Read(ref handle), 0x112 /* WM_SYSCOMMAND */, command, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法提交窗口操作。");
    }
}
