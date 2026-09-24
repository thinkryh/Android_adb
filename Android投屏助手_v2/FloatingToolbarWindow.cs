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
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Android投屏助手;

public sealed class FloatingToolbarWindow : Window
{
    private const int SnapDistance = 44;
    private static readonly Color NeutralBorder = Color.FromRgb(205, 222, 227);
    private static readonly Color DockedBorder = Color.FromRgb(142, 205, 218);
    private static readonly Color SnapBorder = Color.FromRgb(28, 163, 191);
    private readonly DeviceViewModel _device;
    private readonly AdbService _adb;
    private readonly Process _scrcpyProcess;
    private readonly Window _mainWindow;
    private readonly string _scrcpyTitle;
    private readonly ScreenRecorderService _recorder;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _snapTimer;
    private readonly Stopwatch _snapWatch = new();
    private readonly Border _shell;
    private readonly Button _recordButton;
    private DockSide _dockSide = DockSide.Right;
    private int _dockTopOffsetPx;
    private bool _isDocked = true;
    private bool _isDragging;
    private bool _isSnapping;
    private int _snapFromLeft;
    private int _snapFromTop;
    private int _snapToLeft;
    private int _snapToTop;
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
        Width = 70;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Background = new SolidColorBrush(Color.FromRgb(235, 243, 246));
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        Topmost = true;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(6) };
        var handle = new Border
        {
            Width = 54,
            Height = 26,
            Margin = new Thickness(0, 0, 0, 6),
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

        AddButton(panel, Geometry.Parse("M 3,9 L 10,3 L 17,9 M 5,8 L 5,17 L 15,17 L 15,8 M 8,17 L 8,12 L 12,12 L 12,17"), "主页", () => RunKeyAsync("3"));
        AddButton(panel, Geometry.Parse("M 11,4 L 5,10 L 11,16 M 5,10 L 17,10"), "返回", () => RunKeyAsync("4"));
        AddButton(panel, Geometry.Parse("M 5,4 L 17,4 L 17,16 L 5,16 Z M 3,7 L 3,18 L 14,18"), "最近任务", () => RunKeyAsync("187"));
        AddButton(panel, Geometry.Parse("M 3,6 L 6,6 L 8,3 L 13,3 L 15,6 L 18,6 L 18,17 L 3,17 Z M 10.5,8 A 3,3 0 1 0 10.5,14 A 3,3 0 1 0 10.5,8"), "截图", CaptureAsync);
        _recordButton = AddButton(panel, new EllipseGeometry(new Point(10, 10), 6, 6), "开始录屏", ToggleRecordingAsync, true);

        _shell = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(235, 243, 246)),
            BorderBrush = new SolidColorBrush(NeutralBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = panel
        };
        Content = _shell;

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
        _snapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _snapTimer.Tick += (_, _) => AdvanceSnapAnimation();
        Loaded += (_, _) =>
        {
            _timer.Start();
            FollowScrcpyWindow();
        };
        Closed += (_, _) => { _timer.Stop(); _snapTimer.Stop(); };
    }

    private static System.Windows.Shapes.Path CreateIcon(Geometry geometry, bool filled = false)
    {
        var ink = new SolidColorBrush(Color.FromRgb(27, 61, 74));
        return new System.Windows.Shapes.Path
        {
            Data = geometry,
            Width = 20,
            Height = 20,
            Stretch = Stretch.Uniform,
            Stroke = filled ? null : ink,
            StrokeThickness = filled ? 0 : 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = filled ? ink : Brushes.Transparent,
            IsHitTestVisible = false
        };
    }

    private Button AddButton(Panel panel, Geometry icon, string tooltip, Func<Task> action, bool filled = false)
    {
        var button = new Button
        {
            Content = CreateIcon(icon, filled),
            Width = 54,
            Height = 42,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = tooltip,
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
        _snapTimer.Stop();
        _isSnapping = false;
        _isDragging = true;
        _timer.Interval = TimeSpan.FromMilliseconds(40);
        try
        {
            DragMove();
        }
        catch (InvalidOperationException) { }
        finally
        {
            _isDragging = false;
            _timer.Interval = TimeSpan.FromMilliseconds(250);
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
        _recordButton.Content = recording
            ? CreateIcon(new RectangleGeometry(new Rect(4, 4, 12, 12), 2, 2), true)
            : CreateIcon(new EllipseGeometry(new Point(10, 10), 6, 6), true);
        _recordButton.ToolTip = recording ? "结束录屏并保存" : "开始录屏";
    }

    private void FollowScrcpyWindow()
    {
        if (_isDragging)
        {
            UpdateSnapPreview();
            return;
        }
        if (_isSnapping) return;
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
        if (_isDocked && Native.GetDockWindowRect(handle, out var rect)) ApplyDockPosition(rect);
    }

    private void SnapToScrcpyWindow()
    {
        var handle = GetScrcpyHandle();
        if (handle == IntPtr.Zero || !Native.GetDockWindowRect(handle, out var rect)) return;
        var toolbarHandle = GetToolbarHandle();
        if (toolbarHandle == IntPtr.Zero || !Native.GetWindowRect(toolbarHandle, out var outerRect)
            || !Native.GetVisibleWindowRect(toolbarHandle, out var visibleRect)) return;

        if (!TryGetDockSide(rect, visibleRect, out var side))
        {
            _isDocked = false;
            _shell.BorderBrush = new SolidColorBrush(NeutralBorder);
            return;
        }

        _isDocked = true;
        _dockSide = side;
        var maxOffset = Math.Max(0, rect.Height - visibleRect.Height);
        _dockTopOffsetPx = Math.Clamp(visibleRect.Top - rect.Top, 0, maxOffset);
        var target = GetDockPosition(rect, outerRect, visibleRect);
        _snapFromLeft = outerRect.Left;
        _snapFromTop = outerRect.Top;
        _snapToLeft = target.Left;
        _snapToTop = target.Top;
        _shell.BorderBrush = new SolidColorBrush(SnapBorder);
        _snapWatch.Restart();
        _isSnapping = true;
        _snapTimer.Start();
    }

    private void ApplyDockPosition(Native.Rect rect)
    {
        var toolbarHandle = GetToolbarHandle();
        if (toolbarHandle == IntPtr.Zero || !Native.GetWindowRect(toolbarHandle, out var outerRect)
            || !Native.GetVisibleWindowRect(toolbarHandle, out var visibleRect)) return;

        var maxOffset = Math.Max(0, rect.Height - visibleRect.Height);
        _dockTopOffsetPx = Math.Clamp(_dockTopOffsetPx, 0, maxOffset);
        var target = GetDockPosition(rect, outerRect, visibleRect);
        if (outerRect.Left != target.Left || outerRect.Top != target.Top)
            Native.SetWindowPos(toolbarHandle, Native.HwndTopmost, target.Left, target.Top, 0, 0, Native.SwpNoSize | Native.SwpNoActivate | Native.SwpShowWindow);
    }

    private static bool TryGetDockSide(Native.Rect scrcpyRect, Native.Rect toolbarRect, out DockSide side)
    {
        var distanceLeft = Math.Abs(toolbarRect.Right - scrcpyRect.Left);
        var distanceRight = Math.Abs(toolbarRect.Left - scrcpyRect.Right);
        side = distanceLeft < distanceRight ? DockSide.Left : DockSide.Right;
        return toolbarRect.Bottom >= scrcpyRect.Top && toolbarRect.Top <= scrcpyRect.Bottom
            && Math.Min(distanceLeft, distanceRight) <= SnapDistance;
    }

    private (int Left, int Top) GetDockPosition(Native.Rect scrcpyRect, Native.Rect outerRect, Native.Rect visibleRect)
    {
        var visibleLeftOffset = visibleRect.Left - outerRect.Left;
        var visibleTopOffset = visibleRect.Top - outerRect.Top;
        var left = _dockSide == DockSide.Right
            ? scrcpyRect.Right - visibleLeftOffset
            : scrcpyRect.Left - visibleRect.Width - visibleLeftOffset;
        var top = scrcpyRect.Top + _dockTopOffsetPx - visibleTopOffset;
        return (left, top);
    }

    private void UpdateSnapPreview()
    {
        var scrcpyHandle = GetScrcpyHandle();
        var toolbarHandle = GetToolbarHandle();
        var nearby = scrcpyHandle != IntPtr.Zero && toolbarHandle != IntPtr.Zero
            && Native.GetDockWindowRect(scrcpyHandle, out var scrcpyRect)
            && Native.GetVisibleWindowRect(toolbarHandle, out var toolbarRect)
            && TryGetDockSide(scrcpyRect, toolbarRect, out _);
        _shell.BorderBrush = new SolidColorBrush(nearby ? SnapBorder : NeutralBorder);
    }

    private void AdvanceSnapAnimation()
    {
        var progress = Math.Clamp(_snapWatch.Elapsed.TotalMilliseconds / 170.0, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var left = (int)Math.Round(_snapFromLeft + (_snapToLeft - _snapFromLeft) * eased);
        var top = (int)Math.Round(_snapFromTop + (_snapToTop - _snapFromTop) * eased);
        Native.SetWindowPos(GetToolbarHandle(), Native.HwndTopmost, left, top, 0, 0, Native.SwpNoSize | Native.SwpNoActivate | Native.SwpShowWindow);
        if (progress < 1) return;
        _snapTimer.Stop();
        _isSnapping = false;
        var brush = new SolidColorBrush(SnapBorder);
        _shell.BorderBrush = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(DockedBorder, TimeSpan.FromMilliseconds(350)));
        FollowScrcpyWindow();
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
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out Rect rect, int size);

        public static bool GetVisibleWindowRect(IntPtr hWnd, out Rect rect)
        {
            if (DwmGetWindowAttribute(hWnd, ExtendedFrameBounds, out rect, Marshal.SizeOf<Rect>()) == 0 && rect.Width > 0 && rect.Height > 0) return true;
            return GetWindowRect(hWnd, out rect);
        }

        public static bool GetDockWindowRect(IntPtr hWnd, out Rect rect)
        {
            if (!GetVisibleWindowRect(hWnd, out rect)) return false;
            if (!GetClientRect(hWnd, out var client) || client.Width <= 0) return true;
            var left = new Point { X = client.Left, Y = client.Top };
            var right = new Point { X = client.Right, Y = client.Bottom };
            if (!ClientToScreen(hWnd, ref left) || !ClientToScreen(hWnd, ref right) || right.X <= left.X) return true;
            rect.Left = left.X;
            rect.Right = right.X;
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X, Y;
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
