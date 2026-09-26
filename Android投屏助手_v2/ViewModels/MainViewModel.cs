using Android投屏助手.Infrastructure;
using Android投屏助手.Models;
using Android投屏助手.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;

namespace Android投屏助手.ViewModels;

public sealed class DeviceViewModel : ObservableObject
{
    private DeviceStatus _status;
    private string _displayName;
    public DeviceViewModel(AdbDevice device)
    {
        Device = device;
        _status = device.Status;
        _displayName = device.DisplayName;
    }
    public AdbDevice Device { get; private set; }
    public string Serial => Device.Serial;
    public string ConnectionLabel => Device.ConnectionLabel;
    public bool IsConnected => Status is DeviceStatus.Online or DeviceStatus.Mirroring;
    public string ConnectionStatusLabel
    {
        get
        {
            var connection = Device.ConnectionType switch
            {
                DeviceConnectionType.Usb => "USB",
                DeviceConnectionType.Wifi or DeviceConnectionType.Mdns => "无线",
                DeviceConnectionType.Emulator => "模拟器",
                _ => "设备"
            };
            var state = Status switch
            {
                DeviceStatus.Unauthorized => "待授权",
                DeviceStatus.Offline => "离线",
                DeviceStatus.Connecting => "连接中",
                DeviceStatus.Failed => "连接失败",
                _ => "已连接"
            };
            return $"{connection} {state}";
        }
    }
    public string SystemLabel => Device.Device ?? "正在读取设备信息";
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public DeviceStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value)) return;
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(ConnectionStatusLabel));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsMirroring));
            OnPropertyChanged(nameof(IsNotMirroring));
            OnPropertyChanged(nameof(CanStartMirror));
        }
    }
    public bool IsMirroring => Status == DeviceStatus.Mirroring;
    public bool IsNotMirroring => !IsMirroring;
    public bool CanStartMirror => Status is DeviceStatus.Online or DeviceStatus.Failed;
    public void UpdateDevice(AdbDevice device, bool mirroring)
    {
        Device = device;
        DisplayName = device.DisplayName;
        OnPropertyChanged(nameof(ConnectionLabel));
        OnPropertyChanged(nameof(ConnectionStatusLabel));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(SystemLabel));
        if (mirroring) Status = DeviceStatus.Mirroring;
        else if (Status != DeviceStatus.Connecting) Status = device.Status;
    }
    public string StatusLabel => Status switch
    {
        DeviceStatus.Online => "准备就绪",
        DeviceStatus.Unauthorized => "请在手机上允许调试",
        DeviceStatus.Offline => "请重新连接设备",
        DeviceStatus.Connecting => "正在建立连接",
        DeviceStatus.Mirroring => "正在投屏",
        DeviceStatus.Failed => "请检查连接",
        _ => "未知状态"
    };
    private void OnPropertyChanged(string name) => RaisePropertyChanged(name);
}

public sealed class MainViewModel : ObservableObject
{
    private readonly AdbService _adb;
    private readonly ScrcpyService _scrcpy;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _refreshCts;
    private bool _isBusy;
    private string _statusText = "正在准备设备服务…";
    public ObservableCollection<DeviceViewModel> Devices { get; } = [];
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public AsyncCommand RefreshCommand { get; }

    public MainViewModel(AdbService adb, ScrcpyService scrcpy, SettingsService settings)
    {
        _adb = adb;
        _scrcpy = scrcpy;
        _settings = settings;
        RefreshCommand = new AsyncCommand(_ => RefreshAsync());
    }

    public async Task RefreshAsync()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = _refreshCts.Token;
        try
        {
            IsBusy = true;
            StatusText = "正在检查 USB 和无线设备…";
            var devices = await _adb.ListDevicesAsync(token);
            if (devices.Count == 0 && _settings.LastWifiAddresses.Count > 0)
            {
                StatusText = "没有发现在线设备，正在尝试已保存的无线地址…";
                foreach (var address in _settings.LastWifiAddresses.Take(4))
                {
                    try
                    {
                        if (await _adb.ConnectAsync(address, token)) _settings.RememberWifiAddress(address);
                    }
                    catch { }
                }
                devices = await _adb.ListDevicesAsync(token);
            }
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var oldBySerial = Devices.ToDictionary(x => x.Serial, StringComparer.OrdinalIgnoreCase);
                Devices.Clear();
                foreach (var device in devices)
                {
                    DeviceViewModel item;
                    if (oldBySerial.TryGetValue(device.Serial, out var old))
                    {
                        old.UpdateDevice(device, _scrcpy.IsRunning(device.Serial));
                        item = old;
                    }
                    else item = new DeviceViewModel(device);
                    Devices.Add(item);
                }
                foreach (var old in oldBySerial.Values)
                {
                    if (Devices.Any(x => x.Serial.Equals(old.Serial, StringComparison.OrdinalIgnoreCase))
                        || !_scrcpy.IsRunning(old.Serial)) continue;
                    old.Status = DeviceStatus.Mirroring;
                    Devices.Add(old);
                }
            });
            StatusText = Devices.Count == 0 ? "没有检测到设备，请连接 USB 或添加无线设备。" : $"已发现 {Devices.Count} 台设备。";
        }
        catch (OperationCanceledException) { StatusText = "设备刷新已取消。"; }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task<bool> ConnectIpAsync(string address)
    {
        try
        {
            StatusText = $"正在连接 {address}…";
            if (!await _adb.ConnectAsync(address)) throw new InvalidOperationException("无线连接没有返回成功结果。");
            _settings.RememberWifiAddress(address);
            await RefreshAsync();
            StatusText = "无线设备连接成功。";
            return true;
        }
        catch (Exception ex) { StatusText = ex.Message; return false; }
    }

    public async Task<bool> PairCodeAsync(string address, string code, string? connectAddress)
    {
        try
        {
            StatusText = "正在进行无线配对…";
            if (!await _adb.PairAsync(address, code)) throw new InvalidOperationException("配对失败，请检查配对地址和六位配对码。");
            if (!string.IsNullOrWhiteSpace(connectAddress))
            {
                if (!await _adb.ConnectAsync(connectAddress)) throw new InvalidOperationException("配对成功，但连接地址不可用。");
                _settings.RememberWifiAddress(connectAddress);
            }
            await RefreshAsync();
            StatusText = "无线配对成功。";
            return true;
        }
        catch (Exception ex) { StatusText = ex.Message; return false; }
    }

    public async Task<IReadOnlyList<MdnsEndpoint>> DiscoverMdnsAsync()
    {
        try { return await _adb.DiscoverMdnsAsync(); }
        catch { return []; }
    }

    public async Task<Process> StartMirrorAsync(DeviceViewModel item)
    {
        item.Status = DeviceStatus.Connecting;
        try
        {
            var process = await _scrcpy.StartAsync(item.Device);
            item.Status = DeviceStatus.Mirroring;
            StatusText = $"{item.DisplayName} 已开始投屏。";
            return process;
        }
        catch
        {
            item.Status = DeviceStatus.Failed;
            throw;
        }
    }

    public void StopMirror(DeviceViewModel item)
    {
        _scrcpy.Stop(item.Serial);
        item.Status = DeviceStatus.Online;
        StatusText = $"已结束 {item.DisplayName} 的投屏。";
    }

    public AdbService Adb => _adb;
}
