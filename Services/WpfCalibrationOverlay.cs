using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace OculusGoAirMouse.Services;

// A view-only presenter. Calibration and mouse output stay on the input worker.
public sealed class WpfCalibrationOverlay : IDisposable
{
    private readonly IControllerRuntime runtime;
    private readonly DispatcherTimer timer;
    private RingWindow? window;
    private bool disposed;

    public WpfCalibrationOverlay(IControllerRuntime runtime, Dispatcher dispatcher)
    {
        this.runtime = runtime;
        timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += Tick;
        timer.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        var snapshot = runtime.Snapshot;
        if (disposed || !runtime.Connected || !snapshot.IsCalibrating)
        {
            window?.Hide();
            return;
        }
        window ??= new RingWindow();
        window.FollowCursor(snapshot.CalibrationProgress);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop(); timer.Tick -= Tick;
        window?.Close(); window = null;
    }

    private sealed class RingWindow : Window
    {
        private readonly RingVisual ring = new();
        private nint handle;
        public RingWindow()
        {
            Width = Height = 80;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowActivated = false;
            ShowInTaskbar = false;
            Focusable = false;
            IsHitTestVisible = false;
            Topmost = true;
            Content = ring;
            SourceInitialized += InitializeNativeWindow;
        }
        private void InitializeNativeWindow(object? sender, EventArgs e)
        {
            handle = new WindowInteropHelper(this).Handle;
            // Layered + transparent passes all clicks through, including the ring.
            // NoActivate and ToolWindow avoid focus changes and taskbar/Alt-Tab entries.
            int style = GetWindowLong(handle, -20);
            _ = SetWindowLong(handle, -20, style | 0x20 | 0x80 | 0x08000000);
            HwndSource.FromHwnd(handle)?.AddHook(WindowHook);
        }
        private static nint WindowHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == 0x0084) { handled = true; return -1; } // HTTRANSPARENT
            if (message == 0x0021) { handled = true; return 3; } // MA_NOACTIVATE
            return 0;
        }
        public void FollowCursor(double progress)
        {
            if (!GetCursorPos(out var point)) { Hide(); return; }
            ring.Progress = progress;
            new WindowInteropHelper(this).EnsureHandle();
            // Position in physical pixels so negative monitor coordinates and
            // mixed DPI screens use the same coordinate space as GetCursorPos.
            nint monitor = MonitorFromPoint(point, 2);
            uint dpi = 96;
            if (GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0) dpi = dpiX;
            int size = (int)Math.Round(80 * dpi / 96.0);
            _ = SetWindowPos(handle, -1, point.X - size / 2, point.Y - size / 2, size, size, 0x0010);
            if (!IsVisible) Show();
            // Reassert the topmost position without activating the overlay.
            _ = SetWindowPos(handle, -1, point.X - size / 2, point.Y - size / 2, size, size, 0x0010);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    }

    private sealed class RingVisual : FrameworkElement
    {
        private double progress;
        public double Progress { set { progress = Math.Clamp(value, 0, 1); InvalidateVisual(); } }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double radius = Math.Min(ActualWidth, ActualHeight) * .35;
            dc.DrawEllipse(null, new Pen(Brushes.Black, 7), center, radius, radius);
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(210, 235, 235, 235)), 4), center, radius, radius);
            if (progress <= 0) return;
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 170, 255)), 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (progress >= 1) { dc.DrawEllipse(null, pen, center, radius, radius); return; }
            double angle = progress * Math.PI * 2;
            var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(center.X, center.Y - radius), false, false);
                context.ArcTo(end, new Size(radius, radius), 0, progress > .5, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze(); dc.DrawGeometry(null, pen, geometry);
        }
    }
}
