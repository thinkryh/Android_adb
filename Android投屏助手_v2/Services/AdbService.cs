using Android投屏助手.Models;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Android投屏助手.Services;

public sealed class AdbService
{
    private static readonly Regex EndpointPattern = new(@"^(?<host>\[[^\]]+\]|[^:]+):(?<port>\d{1,5})$", RegexOptions.Compiled);
    private readonly string _adbPath;
    private readonly string _workingDirectory;

    public AdbService(string adbPath)
    {
        _adbPath = adbPath;
        _workingDirectory = System.IO.Path.GetDirectoryName(adbPath) ?? AppContext.BaseDirectory;
    }

    public bool Exists => File.Exists(_adbPath);
    public string Path => _adbPath;

    public async Task<AdbResult> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        if (!Exists) throw new FileNotFoundException("找不到 adb.exe，请检查 scrcpy 文件夹。", _adbPath);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _adbPath,
                WorkingDirectory = _workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException("无法启动 adb.exe。");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new AdbResult(process.ExitCode, (await stdout).Trim(), (await stderr).Trim());
    }

    public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["devices", "-l"], cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException(DescribeError(result));
        var list = new List<AdbDevice>();
        foreach (var line in result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            var match = Regex.Match(line.Trim(), @"^(\S+)\s+(device|unauthorized|offline)(?:\s+(.*))?$");
            if (!match.Success) continue;
            var serial = match.Groups[1].Value;
            var statusText = match.Groups[2].Value;
            var attributes = match.Groups[3].Value;
            list.Add(new AdbDevice
            {
                Serial = serial,
                RawStatus = statusText,
                Product = FindAttribute(attributes, "product"),
                Model = DecodeAttribute(FindAttribute(attributes, "model")),
                Device = FindAttribute(attributes, "device"),
                ConnectionType = Classify(serial),
                Status = statusText switch
                {
                    "device" => DeviceStatus.Online,
                    "unauthorized" => DeviceStatus.Unauthorized,
                    _ => DeviceStatus.Offline
                }
            });
        }
        await Task.WhenAll(list.Where(x => x.Status == DeviceStatus.Online)
            .Select(x => FillPropertiesAsync(x, cancellationToken)));
        return list;
    }

    private async Task FillPropertiesAsync(AdbDevice device, CancellationToken cancellationToken)
    {
        try
        {
            var model = await RunAsync(["-s", device.Serial, "shell", "getprop", "ro.product.model"], cancellationToken);
            var version = await RunAsync(["-s", device.Serial, "shell", "getprop", "ro.build.version.release"], cancellationToken);
            if (!string.IsNullOrWhiteSpace(model.Output)) device.Model = model.Output.Trim();
            if (!string.IsNullOrWhiteSpace(version.Output)) device.Device = $"Android {version.Output.Trim()}";
        }
        catch { }
    }

    public async Task<bool> ConnectAsync(string address, CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(address, "连接地址");
        var result = await RunAsync(["connect", address.Trim()], cancellationToken);
        return result.ExitCode == 0 && (result.Output.Contains("connected", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("already", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> PairAsync(string address, string code, CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(address, "配对地址");
        if (!Regex.IsMatch(code, @"^\d{6}$")) throw new ArgumentException("配对码必须是六位数字。", nameof(code));
        var result = await RunAsync(["pair", address.Trim(), code], cancellationToken);
        return result.ExitCode == 0 && result.Output.Contains("success", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<MdnsEndpoint>> DiscoverMdnsAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["mdns", "services"], cancellationToken);
        if (result.ExitCode != 0) return [];
        var list = new List<MdnsEndpoint>();
        foreach (var line in result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line.Trim(), @"^(?<service>\S+)\s+(?<address>\S+)(?:\s+(?<name>.*))?$");
            if (!match.Success || !IsEndpoint(match.Groups["address"].Value)) continue;
            var service = match.Groups["service"].Value;
            if (!service.Contains("adb-tls", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new MdnsEndpoint
            {
                ServiceType = service,
                Address = match.Groups["address"].Value,
                ServiceName = match.Groups["name"].Value.Trim()
            });
        }
        return list;
    }

    public async Task CapturePngToFileAsync(string serial, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (!Exists) throw new FileNotFoundException("找不到 adb.exe。", _adbPath);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _adbPath,
                WorkingDirectory = _workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var arg in new[] { "-s", serial, "exec-out", "screencap", "-p" }) process.StartInfo.ArgumentList.Add(arg);
        if (!process.Start()) throw new InvalidOperationException("无法启动截图命令。");
        var temporaryPath = destinationPath + ".partial";
        try
        {
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous))
                await process.StandardOutput.BaseStream.CopyToAsync(file, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var error = await errorTask;
            if (process.ExitCode != 0 || new FileInfo(temporaryPath).Length < 8)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "截图失败，请检查设备连接。" : error.Trim());
            File.Move(temporaryPath, destinationPath);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
            try { File.Delete(temporaryPath); } catch { }
            throw;
        }
    }

    public static void ValidateEndpoint(string endpoint, string fieldName)
    {
        if (!IsEndpoint(endpoint)) throw new ArgumentException($"{fieldName}格式不正确，请填写 IP:端口。", fieldName);
    }

    public static bool IsEndpoint(string endpoint)
    {
        var match = EndpointPattern.Match(endpoint.Trim());
        if (!match.Success || !int.TryParse(match.Groups["port"].Value, out var port) || port is < 1 or > 65535) return false;
        var host = match.Groups["host"].Value.Trim('[', ']');
        return IPAddress.TryParse(host, out _) || Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
    }

    public static DeviceConnectionType Classify(string serial)
    {
        if (serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase)) return DeviceConnectionType.Emulator;
        if (serial.Contains("_adb-tls-connect", StringComparison.OrdinalIgnoreCase)) return DeviceConnectionType.Mdns;
        if (IsEndpoint(serial)) return DeviceConnectionType.Wifi;
        return DeviceConnectionType.Usb;
    }

    private static string? FindAttribute(string input, string key)
    {
        var match = Regex.Match(input ?? string.Empty, $@"(?:^|\s){Regex.Escape(key)}:(\S+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? DecodeAttribute(string? value) => value?.Replace("_", " ");
    private static string DescribeError(AdbResult result) => string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
}

public sealed record AdbResult(int ExitCode, string Output, string Error);
