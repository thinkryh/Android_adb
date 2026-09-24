using Android投屏助手.Models;
using Android投屏助手.Services;
using Android投屏助手.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Android投屏助手;

public sealed class FloatingToolbarWindow : Window
{
    private const int DockGap = 8;
    private readonly DeviceViewModel _device;
    private readonly AdbService _adb;
    private readonly Process _scrcpyProcess;
    private readonly string _scrcpyTitle;
    private readonly ScreenRecorderService _recorder;
    private readonly DispatcherTimer _timer;
    private readonly Button _recordButton;
    private readonly TextBlock _recordHint;
    private DockSide _dockSide = DockSide.Right;
    private int _dockTopOffsetPx;
    private bool _hasDockPosition;
    private bool _isDragging;
    private bool _closing;

    private enum DockSide
    {
        Left,
        Right
    }

    public FloatingToolbarWindow(DeviceViewModel device, AdbService adb, Process scrcpyProcess, string scrcpyTitle)
    {
        _device = device;
        _adb = adb;
        _scrcpyProcess = scrcpyProcess;
        _scrcpyTitle = scrcpyTitle;
        _recorder = new ScreenRecorderService(adb, device.Device);

        Title = "投屏快捷操作";
        Width = 76;
        Height = 285;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Background = new SolidColorBrush(Color.FromRgb(235, 243, 246));
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        Topmost = true;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(7, 6, 7, 7) };
        var handle = new Border
        {
            Height = 25,
            Margin = new Thickness(0, 0, 0, 3),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(213, 229, 234)),
            Cursor = Cursors.SizeAll,
            ToolTip = "拖动此处，可吸附到投屏窗口左侧或右侧"
        };
        handle.Child = new TextBlock
        {
            Text = "⋮⋮",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(64, 102, 112))
        };
        handle.MouseLeftButtonDown += Handle_MouseLeftButtonDown;
        panel.Children.Add(handle);

        AddButton(panel, "⌂", "主页", () => RunKeyAsync("3"));
        AddButton(panel, "‹", "返回", () => RunKeyAsync("4"));
        AddButton(panel, "▣", "最近任务", () => RunKeyAsync("187"));
        AddButton(panel, "◎", "截图", CaptureAsync);
        _recordButton = AddButton(panel, "●", "开始录屏", ToggleRecordingAsync);
        _recordHint = new TextBlock
        {
            Text = "拖动上方手柄调整位置",
            FontSize = 9,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(96, 115, 125)),
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(_recordHint);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(235, 243, 246)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(205, 222, 227)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = panel
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => FollowScrcpyWindow();
        Loaded += (_, _) =>
        {
            _timer.Start();
            FollowScrcpyWindow();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private Button AddButton(Panel panel, string text, string tooltip, Func<Task> action)
    {
        var button = new Button
        {
            Content = text,
            Width = 58,
            Height = 38,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = tooltip,
            FontSize = 18,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        button.Click += async (_, _) => await action();
        panel.Children.Add(button);
        return button;
    }

    private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _isDragging = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException) { }
        finally
        {
            _isDragging = false;
            SnapToScrcpyWindow();
        }
        e.Handled = true;
    }

    private async Task RunKeyAsync(string key)
    {
        try { await _adb.RunAsync(["-s", _device.Serial, "shell", "input", "keyevent", key]); } catch { }
    }

    private async Task CaptureAsync()
    {
        try
        {
            var bytes = await _adb.CapturePngAsync(_device.Serial);
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Android投屏助手");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"{Sanitize(_device.DisplayName)}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            await File.WriteAllBytesAsync(file, bytes);
            MessageBox.Show($"截图已保存：\n{file}", "截图完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "截图失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async Task ToggleRecordingAsync()
    {
        if (!_recordButton.IsEnabled) return;
        _recordButton.IsEnabled = false;
        try
        {
            if (_recorder.IsRecording)
            {
                var file = await _recorder.StopAsync();
                SetRecordingState(false);
                if (!string.IsNullOrWhiteSpace(file))
                {
                    MessageBox.Show($"录屏已保存：\n{file}", "录屏完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            await _recorder.StartAsync(_device.DisplayName);
            SetRecordingState(true);
        }
        catch (Exception ex)
        {
            SetRecordingState(false);
            MessageBox.Show(ex.Message, "录屏失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _recordButton.IsEnabled = true;
        }
    }

    private void SetRecordingState(bool recording)
    {
        _recordButton.Content = recording ? "■" : "●";
        _recordButton.ToolTip = recording ? "结束录屏并保存" : "开始录屏";
        _recordHint.Text = recording ? "正在录制，点击 ■ 结束并保存" : "拖动上方手柄调整位置";
    }

    private void FollowScrcpyWindow()
    {
        if (_isDragging) return;
        var handle = GetScrcpyHandle();
        if (handle == IntPtr.Zero || !Native.GetWindowRect(handle, out var rect)) return;
        if (!_hasDockPosition)
        {
            _dockSide = DockSide.Right;
            _dockTopOffsetPx = 0;
            _hasDockPosition = true;
        }
        ApplyDockPosition(rect);
    }

    private void SnapToScrcpyWindow()
    {
        var handle = GetScrcpyHandle();
        if (handle == IntPtr.Zero || !Native.GetWindowRect(handle, out var rect)) return;
        var toolbarHandle = GetToolbarHandle();
        if (toolbarHandle == IntPtr.Zero || !Native.GetWindowRect(toolbarHandle, out var toolbarRect)) return;

        var toolbarCenter = (toolbarRect.Left + toolbarRect.Right) / 2.0;
        var scrcpyCenter = (rect.Left + rect.Right) / 2.0;
        _dockSide = toolbarCenter < scrcpyCenter ? DockSide.Left : DockSide.Right;
        var maxOffset = Math.Max(0, rect.Height - toolbarRect.Height);
        _dockTopOffsetPx = Math.Clamp(toolbarRect.Top - rect.Top, 0, maxOffset);
        _hasDockPosition = true;
        ApplyDockPosition(rect);
    }

    private void ApplyDockPosition(Native.Rect rect)
    {
        var toolbarHandle = GetToolbarHandle();
        if (toolbarHandle == IntPtr.Zero || !Native.GetWindowRect(toolbarHandle, out var toolbarRect)) return;

        var maxOffset = Math.Max(0, rect.Height - toolbarRect.Height);
        _dockTopOffsetPx = Math.Clamp(_dockTopOffsetPx, 0, maxOffset);
        var left = _dockSide == DockSide.Right
            ? rect.Right + DockGap
            : rect.Left - toolbarRect.Width - DockGap;
        var top = rect.Top + _dockTopOffsetPx;
        Native.SetWindowPos(toolbarHandle, Native.HwndTopmost, left, top, 0, 0, Native.SwpNoSize | Native.SwpNoActivate | Native.SwpShowWindow);
    }

    private IntPtr GetToolbarHandle() => new WindowInteropHelper(this).Handle;

    private IntPtr GetScrcpyHandle()
    {
        try
        {
            if (!_scrcpyProcess.HasExited)
            {
                _scrcpyProcess.Refresh();
                if (_scrcpyProcess.MainWindowHandle != IntPtr.Zero) return _scrcpyProcess.MainWindowHandle;
            }
        }
        catch { }
        return Native.FindWindow(null, _scrcpyTitle);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closing && _recorder.IsRecording)
        {
            e.Cancel = true;
            _closing = true;
            _ = CloseAfterRecordingAsync();
            return;
        }
        base.OnClosing(e);
    }

    private async Task CloseAfterRecordingAsync()
    {
        try { await _recorder.StopAsync(); }
        catch { }
        await Dispatcher.InvokeAsync(Close);
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "Android设备" : value;
    }

    private static class Native
    {
        public static readonly IntPtr HwndTopmost = new(-1);
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpShowWindow = 0x0040;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? className, string? windowName);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }
    }
}
