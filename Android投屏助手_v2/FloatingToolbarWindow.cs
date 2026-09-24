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
    private const int SnapDistance = 32;
    private readonly DeviceViewModel _device;
    private readonly AdbService _adb;
    private readonly Process _scrcpyProcess;
    private readonly Window _mainWindow;
    private readonly string _scrcpyTitle;
    private readonly ScreenRecorderService _recorder;
    private readonly DispatcherTimer _timer;
    private readonly Button _recordButton;
    private DockSide _dockSide = DockSide.Right;
    private int _dockTopOffsetPx;
    private bool _isDocked = true;
    private bool _isDragging;
    private bool _hiddenForMinimize;
    private bool _autoFinalizeAttempted;
    private bool _closing;

    private enum DockSide
    {
        Left,
        Right
    }

    public FloatingToolbarWindow(DeviceViewModel device, AdbService adb, Process scrcpyProcess, Window mainWindow, string scrcpyTitle)
    {
        _device = device;
        _adb = adb;
        _scrcpyProcess = scrcpyProcess;
        _mainWindow = mainWindow;
        _scrcpyTitle = scrcpyTitle;
        _recorder = new ScreenRecorderService(adb, device.Device);

        Title = "投屏快捷操作";
        Width = 76;
        Height = 255;
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
            ToolTip = "移动工具栏"
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

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(235, 243, 246)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(205, 222, 227)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = panel
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) =>
        {
            FollowScrcpyWindow();
            if (_recorder.HasPendingRecording && !_recorder.IsRecording && !_autoFinalizeAttempted && _recordButton.IsEnabled)
            {
                _autoFinalizeAttempted = true;
                _ = ToggleRecordingAsync();
            }
        };
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
            if (_recorder.HasPendingRecording)
            {
                var file = await _recorder.StopAsync();
                SetRecordingState(false);
                if (!string.IsNullOrWhiteSpace(file))
                {
                    var size = new FileInfo(file).Length;
                    var choice = MessageBox.Show($"录屏已保存（{size / 1024.0 / 1024.0:F1} MB）：\n{file}\n\n现在打开保存文件夹吗？", "录屏完成", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (choice == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(file)!, UseShellExecute = true });
                    }
                }
                return;
            }

            await _recorder.StartAsync(_device.DisplayName);
            _autoFinalizeAttempted = false;
            SetRecordingState(true);
        }
        catch (Exception ex)
        {
            SetRecordingState(_recorder.HasPendingRecording);
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
    }

    private void FollowScrcpyWindow()
    {
        if (_isDragging) return;
        var handle = GetScrcpyHandle();
        if (handle == IntPtr.Zero) return;
        if (_mainWindow.WindowState == WindowState.Minimized || Native.IsIconic(handle))
        {
            if (IsVisible)
            {
                _hiddenForMinimize = true;
                Hide();
            }
            return;
        }
        if (_hiddenForMinimize)
        {
            _hiddenForMinimize = false;
            Show();
        }
        if (_isDocked && Native.GetVisibleWindowRect(handle, out var rect)) ApplyDockPosition(rect);
    }

    private void SnapToScrcpyWindow()
    {
        var handle = GetScrcpyHandle();
        if (handle == IntPtr.Zero || !Native.GetVisibleWindowRect(handle, out var rect)) return;
        var toolbarHandle = GetToolbarHandle();
        if (toolbarHandle == IntPtr.Zero || !Native.GetWindowRect(toolbarHandle, out var toolbarRect)) return;

        var distanceLeft = Math.Abs(toolbarRect.Right - rect.Left);
        var distanceRight = Math.Abs(toolbarRect.Left - rect.Right);
        var overlapsVertically = toolbarRect.Bottom >= rect.Top && toolbarRect.Top <= rect.Bottom;
        _isDocked = overlapsVertically && Math.Min(distanceLeft, distanceRight) <= SnapDistance;
        if (!_isDocked) return;

        _dockSide = distanceLeft < distanceRight ? DockSide.Left : DockSide.Right;
        var maxOffset = Math.Max(0, rect.Height - toolbarRect.Height);
        _dockTopOffsetPx = Math.Clamp(toolbarRect.Top - rect.Top, 0, maxOffset);
        ApplyDockPosition(rect);
    }

    private void ApplyDockPosition(Native.Rect rect)
    {
        var toolbarHandle = GetToolbarHandle();
        if (toolbarHandle == IntPtr.Zero || !Native.GetWindowRect(toolbarHandle, out var toolbarRect)) return;

        var maxOffset = Math.Max(0, rect.Height - toolbarRect.Height);
        _dockTopOffsetPx = Math.Clamp(_dockTopOffsetPx, 0, maxOffset);
        var left = _dockSide == DockSide.Right
            ? rect.Right
            : rect.Left - toolbarRect.Width;
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
        if (!_closing && _recorder.HasPendingRecording)
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
        try
        {
            var file = await _recorder.StopAsync();
            if (!string.IsNullOrWhiteSpace(file))
                MessageBox.Show($"录屏已保存：\n{file}", "录屏完成", MessageBoxButton.OK, MessageBoxImage.Information);
            await Dispatcher.InvokeAsync(Close);
        }
        catch (Exception ex)
        {
            _closing = false;
            MessageBox.Show($"录屏尚未安全保存，快捷栏会保留供重试：\n{ex.Message}", "录屏失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        private const int ExtendedFrameBounds = 9;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? className, string? windowName);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out Rect rect, int size);

        public static bool GetVisibleWindowRect(IntPtr hWnd, out Rect rect)
        {
            if (DwmGetWindowAttribute(hWnd, ExtendedFrameBounds, out rect, Marshal.SizeOf<Rect>()) == 0 && rect.Width > 0 && rect.Height > 0) return true;
            return GetWindowRect(hWnd, out rect);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }
    }
}
