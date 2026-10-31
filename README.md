# Oculus Go Air Mouse

[English](README.md) | [简体中文](README.zh-CN.md) | [日本語](README.ja.md)

Use your Oculus Go controller as a Windows air mouse. Version **0.5.1**, built with **.NET 10, WPF Fluent Theme, and CommunityToolkit.Mvvm 8.4.2** using MVVM. The interface follows the system light/dark theme.

![Oculus Go controller](https://s3files.core77.com/blog/images/922267_90183_87578_86lxkpMhC.jpg)

The image shows the Oculus Go controller supported by this project.

## Requirements

- An Oculus Go controller with sufficient battery charge, a Bluetooth LE (BLE) adapter, and working drivers.
- A Windows desktop version compatible with .NET 10; Windows 11 is recommended. The project targets `net10.0-windows10.0.19041.0`. The target API version does not mean that every corresponding Windows release is supported by current .NET. See [Microsoft's Windows/.NET requirements](https://learn.microsoft.com/en-us/dotnet/core/install/windows).
- **.NET 10 Desktop Runtime** for a regular build or framework-dependent release. The base .NET Runtime alone is insufficient. Building requires the .NET 10 SDK; running a release does not require Visual Studio.
- The executable requests administrator access through `requireAdministrator`, allowing interaction with elevated windows such as Task Manager on the normal desktop. Approve UAC when prompted; cancelling prevents startup. An already elevated environment may not prompt again.

## First use or a different PC

> **Pair the controller in Windows before using this app. The app searches and connects; it does not perform system pairing. A discovered controller is not necessarily paired. Pair separately on each PC.**

1. Put the controller into pairing mode. Open **Windows Settings → Bluetooth & devices → Add device → Bluetooth**, select the controller, and complete pairing.
2. Close controller apps on other PCs. Temporarily turn off their Bluetooth if necessary to avoid competing connections.
3. Run `OculusGoAirMouse.exe`, approve the administrator prompt, and press **Trigger** to wake the controller.
4. By default, the app automatically searches, connects, and enables mouse control. If an earlier attempt stopped because pairing was missing, finish pairing and click **Search and connect**.
5. Automatic calibration is off by default, and uncalibrated input is supported. If the cursor drifts while still, click **Recalibrate** or hold **Oculus for 1 second**, then keep the controller stationary.

Discovery matches the Bluetooth broadcast name exactly and case-sensitively against `DeviceName`, which defaults to `OMVR-V190`. The app connects to the discovered address and checks the required service and characteristic UUIDs; it is not tied to a particular PC's address. If several controllers share the same name, the first one discovered may be selected.

## Interface language

The top **Language / 语言 / 言語** menu offers **English**, **中文**, **日本語**, and **System default**. With `"Language": "system"`, Chinese system UI languages select Chinese, English selects English, Japanese selects Japanese, and other languages fall back to English.

A manual selection immediately updates buttons, tabs, instructions, settings labels, status, and app-generated connection log text. It is automatically saved in the current user's `settings.json` for the next launch. Choose System default to restore automatic selection. Switching does not reconnect, recalibrate, apply unsaved settings, or change regional number formatting. Original Windows/driver errors may remain in the system language; disk diagnostics retain the original information.

## Controls

| Action | Behavior |
| --- | --- |
| Rotate the controller | Move the cursor |
| Press Oculus | Immediately center the cursor on the main display |
| Hold Oculus for 1 second | Start recalibration without releasing; once per hold |
| Trigger | Left click; hold to drag |
| Back | Right click |
| Slide the touchpad vertically / horizontally | Vertical / horizontal whole-notch scrolling; horizontal support depends on the target app |
| Disconnect / cancel | Cancel a connection attempt or disconnect, and release simulated buttons |
| Close the window | Stop control and clean up background resources |

An Oculus hold first centers the cursor on press, then starts calibration at 1 second. Releasing does not trigger another action.

### Calibration and progress ring

- `CalibrationDurationSeconds` sets the calibration duration: **0.1–3 seconds**, default **1.25 seconds**. This is separate from the fixed 1-second hold threshold.
- Calibration timing starts with valid gyroscope data. A data gap longer than 0.25 seconds restarts the measurement.
- Calibration releases simulated buttons and pauses cursor movement and clicks while retaining the mouse switch state. Previously enabled control resumes on completion; disabled control remains disabled, including if turned off during calibration.
- A blue circular progress ring follows the cursor. It stays on top, allows clicks through, and does not take focus. It can appear while the main window is minimized and hides/is released after completion, disconnection, or exit.
- The ring covers normal app windows but is not guaranteed above secure desktops, exclusive fullscreen, or other special display layers.
- With automatic calibration off, uncalibrated input uses zero bias. Reconnection can reuse a completed calibration within the same app session. Calibration results are not saved across restarts.

### Click stabilization

Trigger and Back briefly lock gyroscope cursor movement after press and after release. Button events are sent immediately; dragging resumes after the press-lock period even while held. Scrolling and the physical mouse are unaffected by this lock.

Movement during the lock is discarded and smoothing residue is cleared, avoiding a jump on unlock. The lock begins when press/release data is received; it does not predict or undo movement before the press. Both durations are configurable; `0` disables the corresponding lock.

## Settings and user data

Edit values on **Settings**, then click **Save and apply**. Values are validated before saving. Saving pauses mouse control, which can be enabled again. Automatic recalibration follows `AutoCalibrateOnConnect`; startup behavior applies on the next launch, and device name/timeouts apply to subsequent connection attempts.

| File | Purpose |
| --- | --- |
| `settings.default.json` beside the executable | Defaults used when no personal configuration exists |
| `UserData\<username>\settings.json` | Current Windows user's configuration; loaded first |
| `UserData\<username>\input-diagnostics.log` | Runtime diagnostics; recreated at startup |

Changing defaults does not overwrite personal settings. A personal configuration is created when none exists. On a loading error, the app reports it and uses model defaults without automatically overwriting the damaged file. Saving uses a temporary file replacement. Paths are relative to the executable directory (`AppContext.BaseDirectory`), not the shortcut's working directory. For user `jinge`:

```text
OculusGoAirMouse.exe
settings.default.json
UserData/
  jinge/
    settings.json
    input-diagnostics.log
```

The subfolder uses the Windows username of the running process. Running under another administrator account uses that account's username. Directories are created automatically, and the running account needs write access to the application directory.

If the new location has no `settings.json`, the app validates and migrates `%LOCALAPPDATA%\OculusGoAirMouse\settings.json`, retaining the old file. Existing new settings take precedence. A corrupt old file is reported and not automatically overwritten. Historical logs remain in the old location; new logs use the new directory.

Each application directory has its own `UserData`. Copy the appropriate user subfolder when moving the app or switching publishing directories. Do not include `UserData` in public release packages or source control.

The following values match the repository's current `settings.default.json`:

| Setting | Default file value | Description / range |
| --- | --- | --- |
| `Language` | `system` | UI language: `system`, `en`, `zh`, or `ja` |
| `DeviceName` | `OMVR-V190` | Bluetooth broadcast name; required |
| `AutoConnectOnStartup` | `true` | Connect and enable mouse control at startup |
| `AutoReconnect` | `true` | Reconnect after disconnection or system resume |
| `AutoCalibrateOnConnect` | `false` | Calibrate on connection, reconnection, and link recovery |
| `SensitivityX` | `1.75` | Horizontal sensitivity, 0.001–100 |
| `SensitivityY` | `1.25` | Vertical sensitivity, 0.001–100 |
| `DeadZone` | `65` | Gyroscope dead zone, 0–32767 |
| `XAxis` | `0` | Horizontal axis: 0=X, 1=Y, 2=Z |
| `YAxis` | `2` | Vertical axis: 0=X, 1=Y, 2=Z |
| `XInvert` | `true` | Invert horizontal movement |
| `YInvert` | `false` | Invert vertical movement |
| `Smoothing` | `0.8` | Cursor smoothing, 0.001–1; higher responds faster |
| `MaxMove` | `100` | Maximum movement per update, 1–2000 pixels |
| `CalibrationDurationSeconds` | `1.25` | Calibration duration, 0.1–3 seconds |
| `ClickPressLockMilliseconds` | `20` | Cursor lock after press, 0–2000 milliseconds |
| `ClickReleaseLockMilliseconds` | `10` | Cursor lock after release, 0–2000 milliseconds |
| `ButtonTimeout` | `3` | Release buttons after a data timeout, 0.25–60 seconds |
| `ScrollUnitsPerNotch` | `30` | Touch displacement per scroll notch, 1–1000; lower scrolls faster |
| `ScrollVerticalInvert` | `false` | Invert vertical scrolling |
| `ScrollHorizontalInvert` | `true` | Invert horizontal scrolling |
| `ScanTimeoutSeconds` | `15` | Timeout per scan, 3–120 seconds |
| `ConnectTimeoutSeconds` | `30` | Connection timeout after discovery, 5–120 seconds |

The model fallback for each click-lock duration is 100 milliseconds. The repository's default file uses 20 / 10 milliseconds. Existing personal settings retain their saved values.

## Connection, sleep, and desktop recovery

- Startup/manual connection failures clean up the session and retry after **2 seconds**, with at most **3 attempts**. An unpaired controller stops retries immediately and shows pairing instructions. Retry manually after pairing.
- A brief Bluetooth interruption stops input and releases buttons, retaining the session for about **3 seconds**. A persistent interruption with `AutoReconnect=true` starts repeated searches, with 2 seconds between failed attempts. Manual disconnect, disabling auto-reconnect, or exit stops retries.
- Successful reconnection restores the previous mouse switch state. `AutoCalibrateOnConnect` controls recalibration. Controller input is ignored until all subscriptions are complete.
- System sleep cancels pending connections and releases buttons. After resume, the app waits **2 seconds**. If it was connected, connecting, or reconnecting before sleep and auto-reconnect is enabled, it attempts to restore the connection and mouse state. A sleeping controller may still need a Trigger press to wake it.
- Lock screens and temporarily unavailable secure desktops pause output while retaining the connection and mouse switch. Recovery discards old input, sends necessary button releases, and waits for held controller buttons to be released before accepting new presses.
- Persistent input permission errors on the normal desktop are still reported as faults. Administrator access does not provide control over UAC secure desktops, lock screens, or every protected window.

## CPU priority

The input thread runs at `AboveNormal`. The monitor samples CPU load once per second and adjusts priority when the controller is connected and mouse control is enabled:

- Two consecutive samples at **80% or higher** raise this process from normal/lower priority to `AboveNormal`.
- Five consecutive samples at **60% or lower**, disabling mouse control, or disconnecting restores the original process priority.
- An originally higher priority is retained; the app does not explicitly set `High` or `RealTime`.

Monitoring failures do not interrupt Bluetooth. Changes and errors are logged. Exit stops monitoring and restores the original priority. Scheduling priority can improve responsiveness but cannot guarantee smooth movement under CPU saturation, driver delays, or Bluetooth interference.

## Build and run

From the repository root:

```powershell
dotnet build OculusGoAirMouse.slnx
```

Double-click the following executable and approve UAC:

```text
bin\Debug\net10.0-windows10.0.19041.0\OculusGoAirMouse.exe
```

Or start it from PowerShell:

```powershell
Start-Process -FilePath '.\bin\Debug\net10.0-windows10.0.19041.0\OculusGoAirMouse.exe' -Verb RunAs
```

For debugging, run Visual Studio as administrator and open `OculusGoAirMouse.slnx`. Close the running app before rebuilding to avoid locked output files.

## Publish for another PC

These examples target **Windows x64**. Use the matching runtime identifier for Arm64 / x86 PCs. Copy the entire output folder, including `settings.default.json`.

Framework-dependent; the destination needs .NET 10 Desktop Runtime:

```powershell
dotnet publish OculusGoAirMouse.csproj -c Release -r win-x64 --self-contained false -o .\bin\publish\win-x64-framework
```

Self-contained; no separate .NET installation is needed:

```powershell
dotnet publish OculusGoAirMouse.csproj -c Release -r win-x64 --self-contained true -o .\bin\publish\win-x64-selfcontained
```

See [Microsoft's publishing documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/). Self-contained deployment still requires a compatible Windows version, Bluetooth drivers, administrator access, and system pairing.

Pair on the destination PC before running its `.exe`. To transfer personal settings, copy `UserData\<old-username>\settings.json` from the original app folder to `UserData\<new-username>\settings.json` in the destination app folder. Pairing records and calibration results do not travel with the configuration.

## Troubleshooting and diagnostics

| Symptom | What to check |
| --- | --- |
| Controller not found | Battery, Bluetooth, broadcast name, Trigger wake-up, and competing connections from another PC |
| Controller found but not paired | Complete pairing in this PC's Windows Bluetooth settings, then connect manually |
| `0x8065000F` / insufficient encryption | Check local pairing; if already paired, remove and pair again |
| `AccessDenied` / service access denied | Check pairing, service access state, diagnostics, other connections, and drivers; this status alone does not identify a single cause |
| Three failed connection attempts | Inspect the connection log and diagnostics, including the error code and stack near `connect attempt=` |
| Cannot control Task Manager or another elevated window | Run the current `.exe` and approve administrator access; UAC secure desktops are not controllable |
| Cursor drifts while still | Recalibrate; check the dead zone, sensitivity, and controller stability |
| Cursor jumps when clicking | Adjust press/release lock durations; check whether drift also occurs while still |
| No recovery after system resume | Unlock the desktop, wake the controller with Trigger, and inspect auto-reconnect settings and power/input-desktop diagnostics |

Save the diagnostic log before launching another session, which would overwrite it. Include app version, Windows version, Bluetooth adapter/driver information, reproduction steps, and relevant logs when reporting a problem. Regression-test logs use `scratch` under the test output folder and do not overwrite runtime diagnostics.

## Tests and architecture

Regression checks without a physical controller:

```powershell
dotnet run --project tests/OculusGoAirMouse.Checks.csproj
```

The native progress-ring check briefly shows a test window without connecting a controller. It uses the same administrator manifest, so run it from an elevated terminal:

```powershell
dotnet run --project tests/OverlaySmoke/OverlaySmoke.csproj
```

The English/Chinese/Japanese UI check uses a simulated runtime, does not connect a controller, and does not write real personal settings:

```powershell
dotnet run --project tests/LanguageSmoke/LanguageSmoke.csproj
```

Coverage includes language fallback/switching/persistence, configuration validation, pairing errors, input, click locks, calibration, scrolling, the independent input thread, title-bar compatibility, disconnection/sleep recovery, unavailable desktops, scheduling priority, and resource disposal. Real Bluetooth compatibility, sleep behavior, and high-load responsiveness still require hardware testing.

```text
Models/        Settings and validation
ViewModels/    UI state, asynchronous commands, settings editing and saving
Services/      Bluetooth, input worker, mouse output, progress ring, CPU scheduling, settings and logs
MainWindow.*   Bindings, title-bar geometry, log scrolling and closing
App.xaml.cs    Service composition, power events and exit cleanup
tests/         Regression and native-window checks
```

ViewModels use CommunityToolkit.Mvvm's `ObservableObject`, `ObservableProperty`, and `RelayCommand`, with runtime, settings-store, and UI-scheduler interfaces. Controller input is serialized on an independent thread; mouse-up delivery does not depend on the UI thread.

Normal exit cancels and awaits connection/reconnect work, releases simulated buttons, unsubscribes Bluetooth events, closes notifications/sessions, stops the input thread and UI timers, removes power subscriptions, closes the progress ring, stops priority monitoring, and drains/closes logs. Forced termination does not run the complete cleanup path.

Git tracks source history. `.gitignore` excludes `bin/`, `obj/`, Visual Studio caches, logs, `UserData/`, and backup files.

## License

[Apache License 2.0](LICENSE.txt).
