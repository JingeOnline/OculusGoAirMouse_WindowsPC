using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace OculusGoAirMouse;

public sealed class ControllerConnection
{
    private static readonly Guid ServiceId = new("81265652-3692-ae93-e711-270f223c83b3");
    private static readonly Guid GyroId = new("8126beef-3692-ae93-e711-270f223c83b3");
    private static readonly Guid SensorId = new("8126face-3692-ae93-e711-270f223c83b3");
    private BluetoothLEDevice? device;
    private GattDeviceService? service;
    private GattSession? session;
    private ConnectionLossMonitor? lossMonitor;
    private GattCharacteristic? gyro, sensor;
    private CancellationTokenSource? pending;
    public event Action<string>? Progress;
    public event Action<byte[], bool>? Frame;
    public event Action? Disconnected;
    public event Action? LinkInterrupted;
    public event Action? LinkRecovered;
    public bool Connected => device?.ConnectionStatus == BluetoothConnectionStatus.Connected && gyro != null && sensor != null;

    public async Task ConnectAsync(Settings settings)
    {
        Disconnect();
        var cts = pending = new CancellationTokenSource();
        try
        {
            Progress?.Invoke("正在搜索控制器，请按 Trigger 唤醒…");
            var found = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            watcher.Received += (_, e) => { if (e.Advertisement.LocalName == settings.DeviceName) found.TrySetResult(e.BluetoothAddress); };
            watcher.Stopped += (_, e) => { if (e.Error != BluetoothError.Success) found.TrySetException(new InvalidOperationException($"蓝牙扫描停止：{e.Error}，请检查蓝牙开关。")); };
            ulong address;
            try { watcher.Start(); address = await found.Task.WaitAsync(TimeSpan.FromSeconds(settings.ScanTimeoutSeconds), cts.Token); }
            finally { watcher.Stop(); }
            cts.CancelAfter(TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds));
            Progress?.Invoke("已发现控制器，正在建立连接并读取服务…");
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(cts.Token) ?? throw new InvalidOperationException("无法访问控制器，请检查 Windows 蓝牙配对。");
            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(cts.Token)
                ?? throw new InvalidOperationException("无法创建控制器蓝牙会话。");
            session.SessionStatusChanged += OnSessionStatusChanged;
            if (session.CanMaintainConnection)
            {
                session.MaintainConnection = true;
                InputDiagnostics.Write("GATT MaintainConnection=true");
            }
            else InputDiagnostics.Write("GATT MaintainConnection unsupported");
            var result = await device.GetGattServicesForUuidAsync(ServiceId, BluetoothCacheMode.Uncached).AsTask(cts.Token);
            if (result.Status != GattCommunicationStatus.Success || result.Services.Count == 0) throw new InvalidOperationException($"读取控制器服务失败：{result.Status}。请确认已在 Windows 中配对。");
            service = result.Services[0];
            foreach (var extra in result.Services.Skip(1)) extra.Dispose();
            var characteristics = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(cts.Token);
            if (characteristics.Status != GattCommunicationStatus.Success) throw new InvalidOperationException($"读取特征失败：{characteristics.Status}");
            gyro = characteristics.Characteristics.FirstOrDefault(c => c.Uuid == GyroId) ?? throw new InvalidOperationException("缺少陀螺仪特征。");
            sensor = characteristics.Characteristics.FirstOrDefault(c => c.Uuid == SensorId) ?? throw new InvalidOperationException("缺少按钮特征。");
            gyro.ValueChanged += OnGyro; sensor.ValueChanged += OnSensor;
            Progress?.Invoke("正在订阅运动和按键数据…");
            foreach (var characteristic in new[] { gyro, sensor })
            {
                var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(cts.Token);
                if (status != GattCommunicationStatus.Success) throw new InvalidOperationException($"订阅失败：{status}。请检查蓝牙配对和设备权限。");
            }
            lossMonitor = new ConnectionLossMonitor(
                () => Connected,
                () => { InputDiagnostics.Capture("link interrupted; keeping session for 3 seconds"); LinkInterrupted?.Invoke(); },
                () => { InputDiagnostics.Capture("link recovered; recalibration required"); LinkRecovered?.Invoke(); },
                () => { InputDiagnostics.Capture("link disconnect confirmed after grace period"); Disconnected?.Invoke(); },
                TimeSpan.FromSeconds(3));
            device.ConnectionStatusChanged += OnConnectionChanged;
            if (!Connected) throw new InvalidOperationException("控制器在连接期间断开，请重试。");
            Progress?.Invoke("已连接，请保持控制器静止，完成校准后开启鼠标。");
        }
        catch { Disconnect(); throw; }
        finally { if (ReferenceEquals(pending, cts)) pending = null; cts.Dispose(); }
    }
    private void OnGyro(GattCharacteristic sender, GattValueChangedEventArgs e) => ReadFrame(sender, e, true);
    private void OnSensor(GattCharacteristic sender, GattValueChangedEventArgs e) => ReadFrame(sender, e, false);
    private void ReadFrame(GattCharacteristic sender, GattValueChangedEventArgs e, bool isGyro)
    {
        if (!ReferenceEquals(sender, isGyro ? gyro : sensor)) return;
        using var reader = DataReader.FromBuffer(e.CharacteristicValue);
        var bytes = new byte[e.CharacteristicValue.Length]; reader.ReadBytes(bytes);
        InputDiagnostics.Frame(bytes, isGyro, false);
        Frame?.Invoke(bytes, isGyro);
    }
    private void OnConnectionChanged(BluetoothLEDevice sender, object args)
    {
        if (!ReferenceEquals(sender, device)) return;
        var status = sender.ConnectionStatus;
        InputDiagnostics.Capture("Bluetooth status=" + status);
        lossMonitor?.Notify(status == BluetoothConnectionStatus.Connected);
    }
    private void OnSessionStatusChanged(GattSession sender, GattSessionStatusChangedEventArgs args)
        => InputDiagnostics.Capture($"GATT status={args.Status} error={args.Error}");
    public void Disconnect()
    {
        InputDiagnostics.Write("disposing GATT session");
        lossMonitor?.Dispose(); lossMonitor = null;
        pending?.Cancel();
        if (device != null) device.ConnectionStatusChanged -= OnConnectionChanged;
        if (gyro != null) gyro.ValueChanged -= OnGyro;
        if (sensor != null) sensor.ValueChanged -= OnSensor;
        gyro = sensor = null;
        if (session != null)
        {
            session.SessionStatusChanged -= OnSessionStatusChanged;
            try { if (session.CanMaintainConnection) session.MaintainConnection = false; }
            catch (Exception ex) { InputDiagnostics.Write("GATT session cleanup=" + ex.Message); }
            finally { session.Dispose(); session = null; }
        }
        service?.Dispose(); service = null;
        device?.Dispose(); device = null;
    }
}
