using Android投屏助手.Models;
using System.Diagnostics;
using System.IO;

namespace Android投屏助手.Services;

public sealed class ScrcpyService
{
    private readonly string _scrcpyPath;
    private readonly string _adbPath;
    private readonly Dictionary<string, Process> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public ScrcpyService(string scrcpyPath, string adbPath)
    {
        _scrcpyPath = scrcpyPath;
        _adbPath = adbPath;
    }

    public bool Exists => File.Exists(_scrcpyPath);
    public IReadOnlyDictionary<string, Process> Sessions => _sessions;

    public async Task<Process> StartAsync(AdbDevice device, CancellationToken cancellationToken = default)
    {
        if (!Exists) throw new FileNotFoundException("找不到 scrcpy.exe，请检查 scrcpy 文件夹。", _scrcpyPath);
        if (_sessions.TryGetValue(device.Serial, out var existing) && !existing.HasExited) return existing;
        var wireless = device.ConnectionType is DeviceConnectionType.Wifi or DeviceConnectionType.Mdns;
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _scrcpyPath,
                WorkingDirectory = System.IO.Path.GetDirectoryName(_scrcpyPath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.Environment["ADB"] = _adbPath;
        foreach (var arg in BuildArguments(device, wireless)) process.StartInfo.ArgumentList.Add(arg);
        if (!process.Start()) throw new InvalidOperationException("无法启动投屏进程。");
        _sessions[device.Serial] = process;
        process.Exited += (_, _) => _sessions.Remove(device.Serial);
        await Task.Delay(700, cancellationToken);
        if (process.HasExited) throw new InvalidOperationException("投屏窗口启动失败，请检查设备授权和无线连接。");
        return process;
    }

    public void Stop(string serial)
    {
        if (!_sessions.Remove(serial, out var process)) return;
        if (!process.HasExited) process.CloseMainWindow();
        process.Dispose();
    }

    public static IEnumerable<string> BuildArguments(AdbDevice device, bool wireless)
    {
        yield return $"--serial={device.Serial}";
        yield return $"--window-title=Android投屏助手 - {device.DisplayName}";
        yield return "--stay-awake";
        yield return "--max-fps=30";
        yield return wireless ? "--max-size=1600" : "--max-size=1920";
        yield return wireless ? "--video-bit-rate=12M" : "--video-bit-rate=16M";
        if (wireless) yield return "--video-buffer=30";
        yield return "--video-codec=h264";
        yield return "--keep-active";
        yield return "--audio-bit-rate=128K";
        yield return "--keyboard=uhid";
        yield return "--no-mouse-hover";
        yield return "--verbosity=error";
    }
}
