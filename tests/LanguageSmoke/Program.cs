#pragma warning disable WPF0001 // Match the application's built-in Fluent theme in this view test.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OculusGoAirMouse;
using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;
using OculusGoAirMouse.ViewModels;

static class Smoke
{
    [STAThread]
    public static int Main()
    {
        AppContext.SetData("OculusGoAirMouse.DiagnosticsDirectory", Path.Combine(AppContext.BaseDirectory,"scratch"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System };
        var store = new Store(); var runtime = new Runtime();
        var vm = new MainViewModel(runtime,store,new WpfUiScheduler(app.Dispatcher));
        var window = new MainWindow(vm,new MouseOutput());
        int stage=0,checks=0,result=0;
        window.Closed += (_,_) => app.Shutdown();
        void Check(bool value,string message) { if (!value) throw new Exception(message); checks++; }
        var timer = new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(250) };
        timer.Tick += async (_,_) =>
        {
            try
            {
                if(stage==0)
                {
                    Check(((Button)window.FindName("ConnectButton")).Content?.ToString()=="Search and connect", "Initial English button binding failed");
                    Check(((TextBlock)window.FindName("ConnectionStatus")).Text=="Not connected", "English status converter failed");
                    var tabs = Descendants(window).OfType<TabControl>().Single(); tabs.SelectedIndex=1;
                }
                else if(stage==1)
                {
                    Check(Descendants(window).OfType<TextBlock>().Any(t=>t.Text=="Controller name"),"English settings label converter failed");
                    vm.Settings.Fields[1].TextValue="2.75";
                    SaveImage(window,"language-en.png");
                    vm.SelectedLanguage=vm.Languages.Single(l=>l.Code=="zh");
                }
                else if(stage==2)
                {
                    Check(((Button)window.FindName("ConnectButton")).Content?.ToString()=="搜索并连接", "Live Chinese button update failed");
                    Check(((TextBlock)window.FindName("ConnectionStatus")).Text=="未连接", "Live Chinese status update failed");
                    Check(Descendants(window).OfType<TextBlock>().Any(t=>t.Text=="控制器名称"),"Live Chinese settings label update failed");
                    Check(vm.Settings.Fields[1].TextValue=="2.75" && runtime.Applies==1 && store.Value.Language=="zh", "Switch affected unsaved draft or mouse runtime");
                    SaveImage(window,"language-zh.png");
                    window.Width=window.MinWidth; window.Height=window.MinHeight;
                    vm.SelectedLanguage=vm.Languages.Single(l=>l.Code=="en");
                }
                else if(stage==3)
                {
                    var selector=Descendants(window).OfType<ComboBox>().Single();
                    Check(selector.ActualWidth>=180 && selector.IsVisible,"Language selector unavailable at minimum window size");
                    Check(((Button)window.FindName("CalibrateButton")).Content?.ToString()=="Recalibrate","English switch back failed");
                    SaveImage(window,"language-en-small.png");
                    vm.SelectedLanguage=vm.Languages.Single(l=>l.Code=="ja");
                }
                else if(stage==4)
                {
                    Check(((Button)window.FindName("ConnectButton")).Content?.ToString()=="検索して接続", "Live Japanese button update failed");
                    Check(((TextBlock)window.FindName("ConnectionStatus")).Text=="未接続", "Live Japanese status update failed");
                    Check(Descendants(window).OfType<TextBlock>().Any(t=>t.Text=="コントローラー名"),"Japanese settings label binding failed");
                    Check(vm.Settings.Fields[1].TextValue=="2.75" && store.Value.Language=="ja" && runtime.Applies==1,"Japanese selection did not preserve settings draft/runtime");
                    SaveImage(window,"language-ja-small.png");
                    timer.Stop(); await vm.ShutdownAsync(); window.Close();
                    Console.WriteLine($"PASS: {checks} live WPF language, binding, settings draft and minimum-window checks.");
                }
                stage++;
            }
            catch(Exception ex) { Console.Error.WriteLine(ex); result=1; timer.Stop(); app.Shutdown(); }
        };
        window.Show(); timer.Start(); app.Run(); InputDiagnostics.ShutdownAsync().GetAwaiter().GetResult(); return result;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i); yield return child;
            foreach(var nested in Descendants(child)) yield return nested;
        }
    }
    private static void SaveImage(Window window,string name)
    {
        var visual=(FrameworkElement)window.Content;
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            dc.DrawRectangle(new VisualBrush(visual), null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        }
        bitmap.Render(background); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(Path.Combine(AppContext.BaseDirectory,name)); png.Save(file);
    }
    private sealed class Store : ISettingsStore
    {
        public Settings Value = new() { Language="en", AutoConnectOnStartup=false };
        public string FilePath=>Path.Combine(AppContext.BaseDirectory,"UserData","test","settings.json"); public bool Exists=>true;
        public Settings Load()=>Value.Copy(); public void Save(Settings settings)=>Value=settings.Copy();
    }
    private sealed class Runtime : IControllerRuntime
    {
        public int Applies; public bool Connected=>false;
        public EngineSnapshot Snapshot=>new(false,false,"未连接");
        public event Action<string>? Progress { add{} remove{} }
        public event Action<Exception>? Faulted { add{} remove{} }
        public event Action? Disconnected { add{} remove{} }
        public event Action<bool>? LinkChanged { add{} remove{} }
        public Task ApplyAsync(Settings settings){ Applies++; return Task.CompletedTask; }
        public Task ConnectAsync(Settings settings)=>Task.CompletedTask;
        public Task DisconnectAsync(string reason)=>Task.CompletedTask;
        public Task ToggleAsync()=>Task.CompletedTask;
        public Task CalibrateAsync()=>Task.CompletedTask;
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
