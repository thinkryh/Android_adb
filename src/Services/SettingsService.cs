using System.Text.Json;
using System.IO;

namespace Android投屏助手.Services;

public sealed class SettingsService
{
    private readonly string _path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android投屏助手", "settings.json");
    public IReadOnlyList<string> LastWifiAddresses => _lastWifiAddresses;
    private readonly List<string> _lastWifiAddresses = [];

    public SettingsService()
    {
        try
        {
            if (File.Exists(_path))
            {
                var values = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path));
                if (values is not null) _lastWifiAddresses.AddRange(values.Where(AdbService.IsEndpoint).Distinct(StringComparer.OrdinalIgnoreCase));
            }
        }
        catch { }
    }

    public void RememberWifiAddress(string address)
    {
        if (!AdbService.IsEndpoint(address)) return;
        _lastWifiAddresses.RemoveAll(x => x.Equals(address, StringComparison.OrdinalIgnoreCase));
        _lastWifiAddresses.Insert(0, address);
        if (_lastWifiAddresses.Count > 12) _lastWifiAddresses.RemoveRange(12, _lastWifiAddresses.Count - 12);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_lastWifiAddresses));
        }
        catch { }
    }
}
