using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using ScreenRecorderLib;

namespace Android投屏助手.Services;

/// <summary>在电脑上录制指定的投屏窗口，结束时直接生成本地 MP4。</summary>
public sealed class ScreenRecorderService
{
    private readonly string? _outputFolder;
    private Recorder? _recorder;
    private TaskCompletionSource<string>? _completion;
    private string? _temporaryFile;
    private string? _localFile;
    private readonly Stopwatch _elapsed = new();

    public ScreenRecorderService(string? outputFolder = null) => _outputFolder = outputFolder;

    public bool IsRecording => _recorder is not null && _completion?.Task.IsCompleted == false;
    public bool HasPendingRecording => _recorder is not null;
    public TimeSpan Elapsed => _elapsed.Elapsed;
    public string? LocalFile => _localFile;

    public async Task StartAsync(string displayName, IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        if (HasPendingRecording) throw new InvalidOperationException("上一段录屏尚未保存，请先结束录屏。");
        if (windowHandle == IntPtr.Zero) throw new InvalidOperationException("找不到投屏窗口，请确认投屏已经打开。");

        var folder = _outputFolder ?? MediaStorage.GetFolder();
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var safeName = Sanitize(displayName);
        _localFile = Path.Combine(folder, $"{safeName}-{stamp}.mp4");
        _temporaryFile = Path.Combine(folder, $"{safeName}-{stamp}.partial.mp4");
        _completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new RecorderOptions
        {
            SourceOptions = new SourceOptions
            {
                RecordingSources = new List<RecordingSourceBase>
                {
                    new WindowRecordingSource(windowHandle)
                    {
                        IsBorderRequired = false,
                        IsCursorCaptureEnabled = false
                    }
                }
            },
            AudioOptions = new AudioOptions { IsAudioEnabled = false },
            MouseOptions = new MouseOptions { IsMousePointerEnabled = false },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Framerate = 30,
                Bitrate = 8_000_000,
                IsHardwareEncodingEnabled = true
            }
        };
        try
        {
            _recorder = Recorder.CreateRecorder(options);
            var completion = _completion;
            _recorder.OnRecordingComplete += (_, e) => completion.TrySetResult(e.FilePath);
            _recorder.OnRecordingFailed += (_, e) => completion.TrySetException(new InvalidOperationException(e.Error));
            _recorder.Record(_temporaryFile);
            _elapsed.Restart();
            await Task.Delay(300, cancellationToken);
            if (completion.Task.IsCompleted) await completion.Task;
        }
        catch
        {
            Reset();
            throw;
        }
    }

    public async Task<string?> StopAsync(CancellationToken cancellationToken = default)
    {
        var recorder = _recorder;
        var completion = _completion;
        var temporaryFile = _temporaryFile;
        var localFile = _localFile;
        if (recorder is null || completion is null || temporaryFile is null || localFile is null) return null;
        try
        {
            if (!completion.Task.IsCompleted) recorder.Stop();
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            if (!File.Exists(temporaryFile) || !HasMovieIndex(temporaryFile))
                throw new InvalidOperationException($"电脑录屏未生成完整 MP4 文件：{temporaryFile}");
            File.Move(temporaryFile, localFile);
            return localFile;
        }
        finally { Reset(); }
    }

    private void Reset()
    {
        _elapsed.Stop();
        _recorder?.Dispose();
        _recorder = null;
        _completion = null;
        _temporaryFile = null;
    }

    private static bool HasMovieIndex(string file)
    {
        using var stream = File.OpenRead(file);
        if (stream.Length < 256) return false;
        var hasFileType = false;
        var hasMovieIndex = false;
        var hasMediaData = false;
        Span<byte> header = stackalloc byte[16];
        while (stream.Position + 8 <= stream.Length)
        {
            if (stream.Read(header[..8]) != 8) break;
            var size = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
            var type = Encoding.ASCII.GetString(header.Slice(4, 4));
            long boxSize = size;
            var headerSize = 8;
            if (size == 1)
            {
                if (stream.Read(header[..8]) != 8) break;
                boxSize = checked((long)BinaryPrimitives.ReadUInt64BigEndian(header[..8]));
                headerSize = 16;
            }
            else if (size == 0) boxSize = stream.Length - (stream.Position - 8);
            if (boxSize < headerSize || stream.Position + boxSize - headerSize > stream.Length) return false;
            if (type == "ftyp") hasFileType = true;
            if (type == "moov") hasMovieIndex = true;
            if (type == "mdat" && boxSize > headerSize) hasMediaData = true;
            stream.Seek(boxSize - headerSize, SeekOrigin.Current);
        }
        return hasFileType && hasMovieIndex && hasMediaData;
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "Android设备" : value;
    }
}
