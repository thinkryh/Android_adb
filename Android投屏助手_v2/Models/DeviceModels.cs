using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Android投屏助手.Models;

public enum DeviceConnectionType
{
    Usb,
    Wifi,
    Mdns,
    Emulator,
    Unknown
}

public enum DeviceStatus
{
    Online,
    Unauthorized,
    Offline,
    Connecting,
    Mirroring,
    Failed
}

public sealed class AdbDevice
{
    public required string Serial { get; init; }
    public string? Product { get; init; }
    public string? Model { get; set; }
    public string? Device { get; set; }
    public required string RawStatus { get; init; }
    public DeviceConnectionType ConnectionType { get; init; }
    public DeviceStatus Status { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? Serial : Model!;
    public string ConnectionLabel => ConnectionType switch
    {
        DeviceConnectionType.Usb => "USB 数据线",
        DeviceConnectionType.Wifi => "无线调试",
        DeviceConnectionType.Mdns => "无线发现",
        DeviceConnectionType.Emulator => "模拟器",
        _ => "未知连接"
    };
}

public sealed class MdnsEndpoint
{
    public required string ServiceType { get; init; }
    public required string Address { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    public bool IsPairing => ServiceType.Contains("pairing", StringComparison.OrdinalIgnoreCase);
}

public class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
