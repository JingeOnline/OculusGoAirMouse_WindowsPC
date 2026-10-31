# Oculus Go Air Mouse

[English](README.md) | [简体中文](README.zh-CN.md) | [日本語](README.ja.md)

使用 Oculus Go 控制器操作 Windows 鼠标的桌面应用。当前版本 **0.5.1**，基于 **.NET 10、WPF Fluent Theme 和 CommunityToolkit.Mvvm 8.4.2**，采用 MVVM 架构，界面跟随系统明暗主题。

![Oculus Go 控制器外观](https://s3files.core77.com/blog/images/922267_90183_87578_86lxkpMhC.jpg)

上图为本项目支持的 Oculus Go 控制器，便于首次使用时确认设备外观。

## 运行要求

- Oculus Go 控制器、电量充足，以及支持 Bluetooth LE（BLE）的蓝牙适配器和驱动。
- 满足 .NET 10 要求的 Windows 桌面系统，推荐 Windows 11。项目目标为 `net10.0-windows10.0.19041.0`；目标 API 版本不等于所有对应 Windows 版本都受当前 .NET 支持，具体参见[微软 Windows/.NET 支持说明](https://learn.microsoft.com/zh-cn/dotnet/core/install/windows)。
- 普通构建/框架依赖发布需要 **.NET 10 Desktop Runtime**，仅安装基础 .NET Runtime 不够。编译源码需要 .NET 10 SDK；使用发布包不需要 Visual Studio。
- 程序通过 `requireAdministrator` 请求管理员权限，用于控制任务管理器等已提升权限的普通桌面窗口。从普通桌面启动时确认 UAC；取消确认则不启动。已提升权限的环境可能不再提示。

## 首次使用与换电脑

> **先在 Windows 中完成蓝牙配对，再运行本程序。程序负责搜索和连接，不自动执行系统配对。“已发现控制器”不代表已配对。每台电脑都需要分别配对。**

1. 让控制器进入配对模式，打开 **Windows 设置 → 蓝牙和设备 → 添加设备 → 蓝牙**，选择控制器并确认配对完成。
2. 关闭其他电脑上的控制器程序，必要时暂时关闭其他电脑的蓝牙，避免同时连接。
3. 启动 `OculusGoAirMouse.exe`，确认管理员权限提示，按 **Trigger** 唤醒控制器。
4. 默认自动搜索并连接，连接成功后自动开启鼠标。若此前因未配对而停止，完成系统配对后点击 **搜索并连接**。
5. 默认不自动校准，允许直接使用未校准输入。如有静置漂移，可点击 **重新校准**，或持续按住 **Oculus 1 秒**，随后保持控制器静止。

扫描时按配置中的 `DeviceName` 与广播名称精确匹配，区分大小写，默认 `OMVR-V190`。程序连接扫描得到的蓝牙地址，再检查指定的服务和特征 UUID；不固定绑定某台电脑的地址。附近多个同名控制器可能被选中最先发现的一个。

## 界面语言

顶部 **Language / 语言 / 言語** 菜单提供 **English**、**中文**、**日本語** 和 **跟随系统**。默认配置 `"Language": "system"` 按当前 Windows 用户的系统界面语言选择：所有中文区域使用中文，英语使用英语，日语使用日语；其他语言无法匹配时使用英语。

手动选择立即刷新按钮、选项卡、使用说明、设置标签、状态及程序生成的连接日志文本，并自动保存到当前用户的 `settings.json`，下次启动沿用。选择“跟随系统”可恢复系统语言模式。切换不会重连、重新校准或应用未保存的设置草稿；也不会改变数值输入的区域格式。Windows/驱动返回的原始错误内容可能保留系统语言，磁盘诊断日志保留原始信息用于排错。

## 控制方式

| 操作 | 行为 |
| --- | --- |
| 转动控制器 | 移动光标 |
| 按下 Oculus | 立即将光标移到主显示器中央 |
| 持续按住 Oculus 达到 1 秒 | 自动开始重新校准，无需松开；每次长按只触发一次 |
| Trigger | 鼠标左键，持续按住可拖拽 |
| Back | 鼠标右键 |
| 触摸板上下 / 左右滑动 | 垂直 / 水平传统整格滚动；水平滚动效果取决于目标应用 |
| 断开 / 取消 | 停止连接操作或断开连接，并释放模拟按键 |
| 关闭窗口 | 停止控制并清理后台资源 |

长按 Oculus 会先在按下时居中，再在达到 1 秒时开始校准；松开不执行额外动作。

### 校准和进度环

- 校准时长由 `CalibrationDurationSeconds` 控制，允许 **0.1–3 秒**，默认 **1.25 秒**；与固定的 1 秒长按触发时间相互独立。
- 校准从有效陀螺仪数据开始计时，数据中断超过 0.25 秒后重新计时，避免用缺失的数据完成校准。
- 校准期间释放模拟按键、暂停移动和点击，保留鼠标开关状态。完成后，原先开启的继续控制，原先关闭的保持关闭；中途手动关闭也不会被重新开启。
- 校准时显示跟随鼠标的蓝色圆形进度环。进度环置顶、透明、点击穿透、不抢焦点；主窗口最小化时仍可显示。完成校准、断线或退出后隐藏/释放。
- 进度环覆盖普通应用窗口，不能保证覆盖安全桌面、独占全屏等特殊显示层。
- 关闭自动校准时可使用零偏置的未校准输入；同一次运行中重连可沿用已完成的校准。校准结果不跨程序重启保存。

### 点击防抖

Trigger / Back 按下和松开后，分别锁定陀螺仪光标移动一段时间。按键事件立即发送；持续按住超过锁定时间后仍可拖拽，滚动和本地鼠标不受此锁定影响。

锁定期间丢弃移动量并清空平滑残留，避免解除锁定后跳动。锁定从收到按下/松开数据时开始，不预测或回溯按下之前的移动。两项时长均可在设置页修改，设为 `0` 关闭对应锁定。

## 设置与配置文件

在 **设置** 页修改参数，点击 **保存并应用**。保存前执行范围校验；保存后暂停鼠标控制，可再次开启。是否自动重新校准遵循 `AutoCalibrateOnConnect`；启动行为下次启动生效，设备名称及连接超时在后续连接尝试时使用。

| 文件 | 用途 |
| --- | --- |
| 程序目录下的 `settings.default.json` | 没有个人配置时加载的默认配置 |
| `UserData\<当前用户名>\settings.json` | 当前 Windows 用户的个人设置，优先加载 |
| `UserData\<当前用户名>\input-diagnostics.log` | 实际运行诊断日志，启动时重新创建 |

修改程序目录中的默认配置不会覆盖已有个人配置。首次没有个人配置时会创建个人设置；配置读取失败时显示错误并暂用模型默认值，不在加载失败时自动覆盖损坏文件。保存使用临时文件替换。数据目录以程序所在目录（`AppContext.BaseDirectory`）为基准，不受快捷方式“起始位置”影响。例如以 `jinge` 运行时：

```text
OculusGoAirMouse.exe
settings.default.json
UserData/
  jinge/
    settings.json
    input-diagnostics.log
```

子目录名取当前进程的 Windows 用户名。如果以另一个管理员账户运行，会使用那个账户的用户名。程序自动创建所需目录；程序所在目录需要允许当前运行账户写入。

升级后，如果新目录没有 `settings.json`，程序会读取旧位置 `%LOCALAPPDATA%\OculusGoAirMouse\settings.json`，校验通过后将配置写入新目录，保留旧文件。新目录已有配置时不会被旧配置覆盖；旧配置损坏时显示错误，不自动覆盖原文件。历史日志保留在原位置，新的运行日志写入新目录。

每个程序目录有自己的 `UserData`；移动程序或使用新的发布目录时，如需保留数据，请一并复制对应用户子目录。不要将 `UserData` 作为公开发布包或源码的一部分。

下表列出当前仓库 `settings.default.json` 的值和可配置范围：

| 配置项 | 当前默认文件值 | 说明 / 范围 |
| --- | --- | --- |
| `Language` | `system` | 界面语言：`system` 跟随系统、`en` 英语、`zh` 中文、`ja` 日语 |
| `DeviceName` | `OMVR-V190` | 控制器广播名称，不能为空 |
| `AutoConnectOnStartup` | `true` | 启动后自动连接，成功后自动开启鼠标 |
| `AutoReconnect` | `true` | 断线及睡眠唤醒后自动重连 |
| `AutoCalibrateOnConnect` | `false` | 连接、重连及链路恢复后自动校准 |
| `SensitivityX` | `1.75` | 水平灵敏度，0.001–100 |
| `SensitivityY` | `1.25` | 垂直灵敏度，0.001–100 |
| `DeadZone` | `65` | 陀螺仪死区，0–32767 |
| `XAxis` | `0` | 水平轴，0=X、1=Y、2=Z |
| `YAxis` | `2` | 垂直轴，0=X、1=Y、2=Z |
| `XInvert` | `true` | 水平光标反转 |
| `YInvert` | `false` | 垂直光标反转 |
| `Smoothing` | `0.8` | 光标平滑系数，0.001–1，越大响应越快 |
| `MaxMove` | `100` | 单次最大位移，1–2000 像素 |
| `CalibrationDurationSeconds` | `1.25` | 校准时长，0.1–3 秒 |
| `ClickPressLockMilliseconds` | `20` | 按下后光标锁定，0–2000 毫秒 |
| `ClickReleaseLockMilliseconds` | `10` | 松开后光标锁定，0–2000 毫秒 |
| `ButtonTimeout` | `3` | 按键数据超时后释放按键，0.25–60 秒 |
| `ScrollUnitsPerNotch` | `30` | 每格滚动的触摸位移阈值，1–1000，越小越快 |
| `ScrollVerticalInvert` | `false` | 垂直滚动反转 |
| `ScrollHorizontalInvert` | `true` | 水平滚动反转 |
| `ScanTimeoutSeconds` | `15` | 每次扫描超时，3–120 秒 |
| `ConnectTimeoutSeconds` | `30` | 每次发现设备后的连接超时，5–120 秒 |

两项点击锁定的模型备用默认值为 100 毫秒；当前仓库默认文件已调整为 20 / 10 毫秒。已有个人设置继续使用自身保存的值。

## 连接、睡眠与权限恢复

- 启动或手动连接失败后，清理本次会话，间隔 **2 秒**重新尝试，最多 **3 次尝试**。未配对属于需要用户处理的错误，会立即停止重试并显示配对步骤；配对后手动重试。
- 短暂蓝牙中断时停止输入、释放按键，保留会话等待约 **3 秒**。持续中断且 `AutoReconnect=true` 时重新搜索，失败后间隔 2 秒继续尝试；主动断开、关闭自动重连或退出会停止重试。
- 重连成功后恢复断线前的鼠标开关，是否重新校准由 `AutoCalibrateOnConnect` 决定。完整订阅完成前不处理控制器输入。
- 电脑睡眠时取消正在进行的连接并释放按键。唤醒后等待 **2 秒**；若睡眠前正在连接、已连接或正在重连，且自动重连开启，就尝试恢复连接和原鼠标状态。控制器本身休眠时可能仍需按 Trigger 唤醒。
- 锁屏或安全桌面暂时不可输入时暂停输出、保留连接及鼠标开关。桌面恢复后丢弃旧输入、补发必要的松键，并等待控制器按键松开后再接受新按键，避免误点击。
- 普通桌面上持续的输入权限错误仍会报告故障。管理员运行不代表可以控制 UAC 安全桌面、锁屏或所有受保护窗口。

## CPU 优先级

鼠标输入线程使用 `AboveNormal`。后台每秒采样 CPU 负载，鼠标控制开启且已连接时：

- 连续 2 次负载达到 **80%**，将普通或更低优先级的本进程提高至 `AboveNormal`。
- 连续 5 次降至 **60% 或以下**、停止鼠标控制或断开连接后，恢复进程原优先级。
- 保留启动环境原本指定的更高优先级，不主动设置 `High` 或 `RealTime`。

监测失败不会中断蓝牙连接，优先级变化和错误写入日志。退出时停止监测并恢复原优先级。优先级改善调度机会，不保证在 CPU 饱和、驱动延迟或蓝牙干扰下完全消除卡顿。

## 构建与运行

在项目根目录执行：

```powershell
dotnet build OculusGoAirMouse.slnx
```

然后双击下列文件并确认 UAC：

```text
bin\Debug\net10.0-windows10.0.19041.0\OculusGoAirMouse.exe
```

也可以通过 PowerShell 启动：

```powershell
Start-Process -FilePath '.\bin\Debug\net10.0-windows10.0.19041.0\OculusGoAirMouse.exe' -Verb RunAs
```

如需 Visual Studio 调试，以管理员身份启动 Visual Studio，再打开 `OculusGoAirMouse.slnx`。更新构建前先关闭正在运行的程序，以免输出文件被占用。

## 发布到另一台电脑

下面以 **Windows x64** 为例，Arm64 / x86 电脑应使用匹配的运行时标识。两种发布方式都应复制整个输出目录，包含 `settings.default.json`。

需要目标电脑预装 .NET 10 Desktop Runtime：

```powershell
dotnet publish OculusGoAirMouse.csproj -c Release -r win-x64 --self-contained false -o .\bin\publish\win-x64-framework
```

包含运行时，目标电脑无需另装 .NET：

```powershell
dotnet publish OculusGoAirMouse.csproj -c Release -r win-x64 --self-contained true -o .\bin\publish\win-x64-selfcontained
```

参见[微软 .NET 发布说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/)。自包含发布不会免除 Windows 系统兼容性、蓝牙驱动、管理员权限和系统配对要求。

在新电脑上先完成系统配对，再运行发布目录里的 `.exe`。如需沿用个人设置，复制原电脑程序目录下的 `UserData\<原用户名>\settings.json` 到新电脑程序目录下的 `UserData\<新用户名>\settings.json`；配对记录和校准结果不会随配置文件复制。

## 常见问题与诊断

| 现象 | 检查方式 |
| --- | --- |
| 找不到控制器 | 检查电量、蓝牙、广播名称；按 Trigger 唤醒；检查其他电脑是否抢先连接 |
| 已发现，但提示尚未配对 | 在本机 Windows 蓝牙设置中完成配对，再手动连接 |
| `0x8065000F` / 加密不足 | 检查本机配对；已配对仍失败时可移除设备后重新配对 |
| `AccessDenied` / 服务访问被拒绝 | 查看配对状态、服务访问状态及诊断日志，排查其他连接和驱动；仅凭此状态无法确定唯一原因 |
| 三次连接失败 | 查看连接日志及诊断文件，保留错误码和 `connect attempt=` 附近的调用堆栈 |
| 无法控制任务管理器等窗口 | 确认启动的是当前 `.exe` 并已批准管理员权限；UAC 安全桌面不提供鼠标控制 |
| 静置时光标漂移 | 手动重新校准；检查死区、灵敏度和控制器是否稳定 |
| 点击时光标抖动 | 调整按下后 / 松开后的光标锁定时长；确认静置时是否也存在漂移 |
| 唤醒后仍未恢复 | 解锁桌面、按 Trigger 唤醒控制器，检查自动重连设置与诊断日志中的电源及输入桌面状态 |

复现问题后，在下次启动前保存诊断日志，避免被新会话覆盖。反馈时提供程序版本、Windows 版本、蓝牙适配器/驱动信息、复现步骤及对应日志。回归测试日志单独写入测试输出目录的 `scratch`，不会覆盖实际运行日志。

## 验证与开发结构

无需真实控制器的回归检查：

```powershell
dotnet run --project tests/OculusGoAirMouse.Checks.csproj
```

原生进度环检查会短暂显示测试窗口，不连接控制器；测试程序使用同一管理员清单，应从已提升权限的终端运行：

```powershell
dotnet run --project tests/OverlaySmoke/OverlaySmoke.csproj
```

中英日界面检查使用模拟运行时，不连接控制器，也不写入真实个人配置：

```powershell
dotnet run --project tests/LanguageSmoke/LanguageSmoke.csproj
```

检查覆盖系统语言回退、语言切换及偏好保存、配置校验/保存、未配对停止重试、鼠标输入、点击锁定、校准、滚动、独立输入线程、标题栏兼容、断线与睡眠恢复、桌面不可输入、优先级切换以及资源释放。真实蓝牙兼容性、睡眠唤醒和高负载手感仍需实际硬件测试。

```text
Models/        参数及范围校验
ViewModels/    界面状态、异步命令、设置编辑与保存
Services/      蓝牙连接、输入线程、鼠标输出、校准进度环、CPU 调度、配置和日志
MainWindow.*   界面绑定、标题栏几何、日志滚动及关闭流程
App.xaml.cs    服务组装、电源事件订阅及退出清理
tests/         回归检查和原生窗口检查
```

ViewModel 使用 CommunityToolkit.Mvvm 的 `ObservableObject`、`ObservableProperty` 和 `RelayCommand`，依赖运行时、设置存储及界面调度接口。控制器输入在独立线程串行执行，不依赖界面线程发送鼠标松键。

正常退出会取消并等待连接/重连、释放模拟按键、取消蓝牙事件订阅、关闭通知与会话、停止输入线程和界面定时器、移除电源事件订阅、关闭进度环、停止优先级监测，并排空及关闭日志。强制结束进程不执行完整退出流程。

仓库使用 Git 管理源码历史。`bin/`、`obj/`、Visual Studio 缓存、日志、`UserData/` 用户目录及备份文件由 `.gitignore` 排除。

## 许可证

[Apache License 2.0](LICENSE.txt)。
