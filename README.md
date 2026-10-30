# Oculus Go Air Mouse

使用 Oculus Go 控制器操作 Windows 桌面的 .NET 10 WPF 应用。使用 WPF 内置 Fluent 主题，跟随系统明暗模式；通过 CommunityToolkit.Mvvm 8.4.2 实现 MVVM。

## 构建与启动

需要 Windows 10 2004 或更高版本、蓝牙适配器以及 .NET 10 SDK。用 Visual Studio 打开 `OculusGoAirMouse.slnx`，或运行：

```powershell
dotnet build OculusGoAirMouse.slnx
dotnet run --project OculusGoAirMouse.csproj
```

调试程序位于 `bin/Debug/net10.0-windows10.0.19041.0/OculusGoAirMouse.exe`。

## 使用

1. 在 Windows 蓝牙设置中配对控制器，退出旧 Python 程序。
2. 按 Trigger 唤醒后点击“搜索并连接”。连接阶段和等待时间显示在主界面，可随时取消。
3. 静置完成校准后点击“开启鼠标”。
4. Oculus 回主屏中央；Trigger 为左键并支持拖拽；Back 为右键；触摸板支持水平、垂直整格滚动。
5. 在“设置”页修改参数后选择“保存并应用”。数值校验通过才保存；保存后暂停鼠标并重新校准。蓝牙名称和超时设置在下次连接时使用。

默认参数来自原 Python 程序的稳定配置：水平/垂直灵敏度 1.75/1.25，死区 65，轴映射 0/2，水平光标反转，光标平滑 0.8，最大位移 100，校准样本 250，按键超时 3 秒，滚动阈值 30，水平滚动反转。

## 项目结构

```text
Models/       参数模型及校验
ViewModels/   主界面状态、异步命令、设置草稿与保存
Services/     蓝牙会话、输入线程、鼠标输出、配置读写、界面调度适配
tests/        无需控制器的回归检查与 ViewModel 测试
App.xaml.cs   应用组装入口
MainWindow.*  XAML 绑定及窗口句柄、DPI、关闭与滚动等视图工作
```

`MainViewModel` 和 `SettingsViewModel` 使用 Toolkit 的 `ObservableObject`、`ObservableProperty` 和 `RelayCommand`。按钮通过 XAML Command 绑定，设置字段通过 ItemsControl/DataTemplate 双向绑定。ViewModel 依赖可替换的运行时、配置存储和界面调度接口，不直接访问窗口控件或 Windows 蓝牙 API。

控制器处理和鼠标输出在独立线程串行执行，界面定时读取状态快照。断线/超时/退出会释放按键。本程序标题栏使用系统按钮区域快照及异步窗口命令处理控制器点击，保留最大化、最小化兼容修复。

蓝牙会话显式启用 `GattSession.MaintainConnection`。短暂链路中断时立即停止鼠标，保留会话等待 3 秒；期间恢复则重新校准，需手动开启鼠标；持续中断才清理会话。主动断开和退出会释放会话，不后台无限重连。

## 配置与诊断

默认配置：`settings.default.json`。个人配置：`%LOCALAPPDATA%/OculusGoAirMouse/settings.json`。升级和重构沿用原有个人配置。

配置损坏时显示错误并暂用默认值，不在加载失败时覆盖原文件。保存使用临时文件替换。诊断记录：`%LOCALAPPDATA%/OculusGoAirMouse/input-diagnostics.log`，每次启动重建，记录链路和输入处理状态。

## 验证

```powershell
dotnet run --project tests/OculusGoAirMouse.Checks.csproj
```

检查覆盖参数/JSON、校准、双向滚动、按键释放、独立输入线程、标题栏识别、掉线确认，以及 ViewModel 属性通知、命令状态、设置草稿/校验/保存、异常与退出清理。真实蓝牙和控制器手感仍需硬件验收。

## 本地版本控制

本地 `main` 分支已保留重构前的 0.2.2 基线和 MVVM 重构提交。使用 Git 查看和恢复历史，不再生成源码备份文件。

`.gitignore` 排除构建产物、Visual Studio 缓存、日志、临时文件及备份。个人设置在项目外，不纳入版本控制。仓库尚未配置远程地址或推送；可稍后自行发布到 GitHub。

程序以普通用户权限运行，不能控制权限更高的窗口。水平滚动支持取决于目标应用。

## 重新校准与 Oculus 按钮（0.3.2）

点击“重新校准”保留当前鼠标开关。校准期间暂停光标移动和鼠标按键输出，释放正在模拟按下的按键；校准完成后，原先开启的鼠标自动继续控制，原先关闭的保持关闭。期间可手动关闭鼠标，校准结束不会重新开启。新连接、掉线恢复、应用设置时仍默认关闭鼠标。

Oculus 短按在松开时居中；长按达到 1 秒触发重新校准，不执行居中，持续按住不会重复触发。请在长按后保持控制器静止。按键数据超时会取消未完成手势，避免断线后的迟到数据误触发。

## 退出清理（0.3.1）

关闭时先阻止新的控制器输入并释放模拟按键；取消并等待进行中的扫描/连接操作；移除蓝牙事件订阅，尽力关闭通知，关闭会话保持并释放 GATT 会话、服务和设备。单项原生释放失败不会跳过后续资源清理。输入线程退出后才完成运行时释放，重复退出调用会等待同一个清理任务。

界面定时器在 ViewModel 关闭时停止。应用退出时取消诊断定时器、排空日志队列并关闭文件。检查覆盖拖拽时关闭、连接中关闭、取消后重连、原生清理异常、线程终止和日志句柄释放。强制结束进程或调试器直接终止不会运行正常退出流程。

自动重连：设置页面的“蓝牙丢失后自动重连”或配置文件 `AutoReconnect`（默认 `true`）控制。确认断线后自动搜索，失败间隔 2 秒重试；重连后静置校准，恢复断线前的鼠标开关状态。主动断开、关闭此设置或退出程序会取消重连。初次手动连接失败不会自动重试。
