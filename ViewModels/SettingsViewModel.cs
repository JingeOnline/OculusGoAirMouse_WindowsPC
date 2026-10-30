using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OculusGoAirMouse.Models;
using OculusGoAirMouse.Services;

namespace OculusGoAirMouse.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore store;
    private readonly Func<Settings, Task> apply;
    private Settings current;
    public ObservableCollection<SettingFieldViewModel> Fields { get; } = [];
    public string ConfigPath => "配置文件：" + store.FilePath;
    [ObservableProperty] private string message = "";
    [ObservableProperty] private bool isAvailable = true;

    public SettingsViewModel(Settings settings, ISettingsStore store, Func<Settings, Task> apply)
    {
        current = settings.Copy(); this.store = store; this.apply = apply; BuildFields();
    }
    private bool CanSave() => IsAvailable;
    partial void OnIsAvailableChanged(bool value) { SaveCommand.NotifyCanExecuteChanged(); ReloadCommand.NotifyCanExecuteChanged(); }
    private void BuildFields()
    {
        (string Name, string Label)[] definitions = [
            ("DeviceName", "控制器名称"), ("SensitivityX", "水平灵敏度"), ("SensitivityY", "垂直灵敏度"),
            ("DeadZone", "陀螺仪死区"), ("XAxis", "水平轴映射（0=X，1=Y，2=Z）"), ("YAxis", "垂直轴映射（0=X，1=Y，2=Z）"),
            ("XInvert", "水平光标反转"), ("YInvert", "垂直光标反转"), ("Smoothing", "光标平滑系数（越大响应越快）"),
            ("MaxMove", "单次最大光标位移（像素）"), ("CalibrationDurationSeconds", "校准时长（秒，0.1–3）"), ("ButtonTimeout", "按键数据超时（秒）"),
            ("ScrollUnitsPerNotch", "滚动阈值（越小滚动越快）"), ("ScrollVerticalInvert", "垂直滚动反转"), ("ScrollHorizontalInvert", "水平滚动反转"),
            ("ScanTimeoutSeconds", "扫描超时（秒）"), ("ConnectTimeoutSeconds", "连接超时（秒）"), ("AutoReconnect", "蓝牙丢失后自动重连"), ("AutoCalibrateOnConnect", "连接后自动校准"), ("AutoConnectOnStartup", "启动时自动连接并开启鼠标")];
        Fields.Clear();
        foreach (var (name, label) in definitions) Fields.Add(new(name, label, current));
    }
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        try
        {
            var candidate = current.Copy();
            foreach (var field in Fields) field.WriteTo(candidate);
            candidate.Validate(); store.Save(candidate); current = candidate.Copy();
            await apply(candidate);
            Message = "已保存并应用。鼠标已暂停；可重新开启鼠标，校准期间暂停输入。";
        }
        catch (Exception ex) { Message = "保存失败：" + ex.Message; }
    }
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Reload() { BuildFields(); Message = "已恢复当前生效的设置。"; }
}
