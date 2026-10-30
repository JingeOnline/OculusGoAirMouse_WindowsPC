using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;

static class Smoke
{
    [STAThread]
    public static int Main()
    {
        int count = 0, code = 0;
        void Check(bool value, string text) { if (!value) throw new Exception(text); count++; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var fake = new FakeRuntime();
        using var presenter = new WpfCalibrationOverlay(fake, app.Dispatcher);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        int stage = 0;
        nint foreground = GetForegroundWindow();
        timer.Tick += (_, _) =>
        {
            try
            {
                var windows = app.Windows.Cast<Window>().ToArray();
                if (stage == 0)
                {
                    Check(windows.Length == 0, "Overlay created while disconnected");
                    fake.Connected = true;
                }
                else if (stage == 1)
                {
                    var window = windows.Single();
                    var hwnd = new WindowInteropHelper(window).Handle;
                    int style = GetWindowLong(hwnd, -20);
                    Check(window.IsVisible && window.Topmost, "Calibration overlay is not visible/topmost");
                    Check((style & (0x20 | 0x80 | 0x08000000 | 8 | 0x80000)) == (0x20 | 0x80 | 0x08000000 | 8 | 0x80000), "Overlay styles do not pass through/noactivate/topmost");
                    Check(GetForegroundWindow() == foreground, "Overlay stole foreground focus");
                    GetCursorPos(out var cursor); GetWindowRect(hwnd, out var rect);
                    Check(Math.Abs((rect.Left + rect.Right) / 2 - cursor.X) <= 2 && Math.Abs((rect.Top + rect.Bottom) / 2 - cursor.Y) <= 2, "Overlay not centered on cursor");
                    Check(WindowFromPoint(cursor) != hwnd, "Overlay intercepts cursor hit testing");
                    var bitmap = new RenderTargetBitmap(80, 80, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render((Visual)window.Content);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, "ring-preview.png")); png.Save(stream);
                    fake.Snapshot = new(false, true, "done", 1);
                }
                else if (stage == 2)
                {
                    Check(!windows.Single().IsVisible, "Completion did not hide overlay");
                    fake.Snapshot = new(false, false, "calibrating", .75);
                }
                else if (stage == 3)
                {
                    Check(windows.Single().IsVisible, "Recalibration did not show overlay");
                    fake.Connected = false;
                }
                else if (stage == 4)
                {
                    Check(!windows.Single().IsVisible, "Disconnect left overlay visible");
                    presenter.Dispose(); presenter.Dispose();
                    Check(app.Windows.Count == 0, "Dispose left a window open");
                    Console.WriteLine($"PASS: {count} native overlay visibility, cursor position, topmost, click-through, focus and disposal checks.");
                    timer.Stop(); app.Shutdown();
                }
                stage++;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); code = 1; timer.Stop(); app.Shutdown(); }
        };
        timer.Start(); app.Run(); return code;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    private sealed class FakeRuntime : IControllerRuntime
    {
        public EngineSnapshot Snapshot { get; set; } = new(false, false, "calibrating", .6);
        public bool Connected { get; set; }
        public event Action<string>? Progress { add {} remove {} }
        public event Action<Exception>? Faulted { add {} remove {} }
        public event Action? Disconnected { add {} remove {} }
        public event Action<bool>? LinkChanged { add {} remove {} }
        public Task ApplyAsync(Settings value) => Task.CompletedTask;
        public Task ConnectAsync(Settings value) => Task.CompletedTask;
        public Task DisconnectAsync(string reason) => Task.CompletedTask;
        public Task ToggleAsync() => Task.CompletedTask;
        public Task CalibrateAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
