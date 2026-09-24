using System.Diagnostics;
using System.IO;
using System.Text;
using Android投屏助手.Models;

namespace Android投屏助手.Services;

/// <summary>
/// 使用 Android 自带 screenrecord 录制当前设备画面。
/// 录屏独立于 scrcpy 投屏进程，因此开始/结束录屏不会重启投屏窗口。
/// </summary>
public sealed class ScreenRecorderService
{
    private readonly AdbService _adb;
    private readonly string _serial;
    private Process? _process;
    private string? _remoteFile;
    private string? _localFile;
    private Task<string>? _stdoutTask;
    private Task<string>? _stderrTask;

    public ScreenRecorderService(AdbService adb, AdbDevice device)
    {
        _adb = adb;
        _serial = device.Serial;
    }

    public bool IsRecording => _process is { HasExited: false };
    public string? LocalFile => _localFile;

    public async Task StartAsync(string displayName, CancellationToken cancellationToken = default)
    {
        if (IsRecording) return;
        if (_process is not null)
        {
            _process.Dispose();
            _process = null;
        }

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Android投屏助手");
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var safeName = Sanitize(displayName);
        _localFile = Path.Combine(folder, $"{safeName}-{stamp}.mp4");
        // 远端路径使用 ASCII 文件名，避免部分 Android shell 的中文编码问题。
        _remoteFile = $"/sdcard/android_tou_ping_{stamp}.mp4";

        var info = new ProcessStartInfo
        {
            FileName = _adb.Path,
            WorkingDirectory = Path.GetDirectoryName(_adb.Path) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[]
        {
            "-s", _serial, "shell", "screenrecord",
            "--bit-rate", "12000000",
            "--time-limit", "3600",
            _remoteFile
        }) info.ArgumentList.Add(argument);

        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        if (!_process.Start()) throw new InvalidOperationException("无法启动 Android 录屏。");
        _stdoutTask = _process.StandardOutput.ReadToEndAsync(cancellationToken);
        _stderrTask = _process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.Delay(700, cancellationToken);
        if (_process.HasExited)
        {
            var error = _stderrTask is null ? string.Empty : await _stderrTask;
            ResetProcess();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Android 录屏启动失败。" : error.Trim());
        }
    }

    public async Task<string?> StopAsync(CancellationToken cancellationToken = default)
    {
        var process = _process;
        var remoteFile = _remoteFile;
        var localFile = _localFile;
        if (process is null || string.IsNullOrWhiteSpace(remoteFile) || string.IsNullOrWhiteSpace(localFile)) return null;

        try
        {
            if (!process.HasExited)
            {
                var pid = await FindScreenRecordPidAsync(cancellationToken);
                if (pid is not null)
                {
                    try { await _adb.RunAsync(["-s", _serial, "shell", "kill", "-2", pid], cancellationToken); }
                    catch { }
                }
                else
                {
                    try { await _adb.RunAsync(["-s", _serial, "shell", "pkill", "-2", "screenrecord"], cancellationToken); }
                    catch { }
                }

                try
                {
                    await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
                }
                catch (TimeoutException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
            }

            var pull = await _adb.RunAsync(["-s", _serial, "pull", remoteFile, localFile], cancellationToken);
            if (pull.ExitCode != 0 || !File.Exists(localFile) || new FileInfo(localFile).Length == 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(pull.Error) ? "录屏文件导出失败。" : pull.Error.Trim());
            }

            try { await _adb.RunAsync(["-s", _serial, "shell", "rm", remoteFile], cancellationToken); } catch { }
            return localFile;
        }
        finally
        {
            await DrainAsync(_stdoutTask);
            await DrainAsync(_stderrTask);
            ResetProcess();
        }
    }

    private async Task<string?> FindScreenRecordPidAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _adb.RunAsync(["-s", _serial, "shell", "pidof", "screenrecord"], cancellationToken);
            var pid = result.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(x => x.All(char.IsDigit));
            return string.IsNullOrWhiteSpace(pid) ? null : pid;
        }
        catch { return null; }
    }

    private async static Task DrainAsync(Task<string>? task)
    {
        if (task is null) return;
        try { await task; } catch { }
    }

    private void ResetProcess()
    {
        try { _process?.Dispose(); } catch { }
        _process = null;
        _remoteFile = null;
        _stdoutTask = null;
        _stderrTask = null;
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "Android设备" : value;
    }
}
