using OculusGoAirMouse;

static class CaptionChecks
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        var host = new FakeCaptionWindow();
        var router = new CaptionClickRouter(host);
        foreach (var (hit, maximized, command) in new[] { (8, false, 0xF020u), (9, false, 0xF030u), (9, true, 0xF120u), (20, false, 0xF060u) })
        {
            host.Hit = hit; host.IsMaximized = maximized; host.Commands.Clear();
            Check(router.TryHandle(2), "Caption down must not enter SendInput");
            Check(host.Commands.Count == 0, "Caption command fired on down");
            Check(router.TryHandle(4), "Caption up must not enter SendInput");
            Check(host.Commands.SequenceEqual(new[] { command }), "Wrong caption command");
            Check(!router.TryHandle(4), "Caption release repeated command");
        }
        host.Hit = 9; host.Commands.Clear(); router.TryHandle(2); host.Hit = 1;
        Check(router.TryHandle(4) && host.Commands.Count == 0, "Release off button should cancel");
        host.Hit = 9; router.TryHandle(2); host.Hit = 8;
        Check(router.TryHandle(4) && host.Commands.Count == 0, "Release over different caption button should cancel");
        foreach (int hit in new[] { 8, 9, 20 })
        {
            host.Hit = hit; router.TryHandle(2); router.Cancel();
            Check(router.TryHandle(4) && host.Commands.Count == 0, "Watchdog/disconnect must cancel caption command");
        }
        host.Hit = 1;
        Check(!router.TryHandle(2) && !router.TryHandle(4), "Ordinary clicks changed");
        host.Hit = 2;
        Check(!router.TryHandle(2) && !router.TryHandle(4), "Title bar dragging changed");
        Check(!router.TryHandle(1) && !router.TryHandle(8) && !router.TryHandle(16) && !router.TryHandle(0x800) && !router.TryHandle(0x1000), "Other input types changed");
        var regions = new NativeCaptionWindow.CaptionRegions(new(800, 0, 845, 30), new(845, 0, 890, 30), new(890, 0, 935, 30));
        Check(regions.HitTest(820, 15) == 8, "System rectangle identifies minimize without native HTMINBUTTON");
        Check(regions.HitTest(860, 15) == 9 && regions.HitTest(910, 15) == 20, "Other system caption rectangles");
        Check(regions.HitTest(845, 15) == 9 && regions.HitTest(820, 30) == 0, "Caption rectangle boundaries");
        regions = new(new(-400, -100, -355, -70), default, default);
        Check(regions.HitTest(-380, -80) == 8, "Negative monitor coordinates");
        Check(NativeCaptionWindow.CaptionRegions.Empty.HitTest(0, 0) == 0, "Empty or minimized geometry");
        return count;
    }
    private sealed class FakeCaptionWindow : ICaptionWindow
    {
        public int Hit;
        public bool IsMaximized { get; set; }
        public List<uint> Commands { get; } = [];
        public int HitTest() => Hit;
        public void PostCommand(uint command) => Commands.Add(command);
    }
}
