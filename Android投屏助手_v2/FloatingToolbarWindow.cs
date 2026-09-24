using Android投屏助手.Models;
using Android投屏助手.Services;
using Android投屏助手.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Android投屏助手;

public sealed class FloatingToolbarWindow : Window
{
    private const int SnapDistance = 44;
    private static readonly Color NeutralBorder = Color.FromRgb(224, 237, 251);
    private static readonly Color DockedBorder = Color.FromRgb(133, 184, 247);
    private static readonly Color SnapBorder = Color.FromRgb(48, 126, 239);
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
    private readonly Native.WinEventProc _winEventProc;
    private IntPtr _trackedScrcpyHandle;
    private IntPtr _locationHook;
    private IntPtr _minimizeHook;
    private int _followQueued;
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
        _winEventProc = OnScrcpyWindowEvent;

        Title = "投屏快捷操作";
        Width = 66;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Background = (Brush)Application.Current.FindResource("ToolbarGlassBrush");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        Topmost = true;
        ShowInTaskbar = false;
        GlassWindowEffects.EnableRoundedCorners(this);

        var panel = new StackPanel { Margin = new Thickness(8, 7, 8, 7) };
        var handle = new Border
        {
            Width = 48,
            Height = 25,
            Margin = new Thickness(0, 0, 0, 7),
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.FromArgb(166, 224, 237, 253)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.SizeAll,
            ToolTip = "移动工具栏"
        };
        handle.Child = CreateDragDots();
        handle.MouseLeftButtonDown += Handle_MouseLeftButtonDown;
        panel.Children.Add(handle);

        AddButton(panel, Geometry.Parse("M 3.5,10.5 L 12,3.8 L 20.5,10.5 M 5.5,9.6 L 5.5,20 L 18.5,20 L 18.5,9.6 M 9.5,20 L 9.5,14.3 L 14.5,14.3 L 14.5,20"), "主页", () => RunKeyAsync("3"));
        AddButton(panel, Geometry.Parse("M 13.5,5.2 L 6.6,12 L 13.5,18.8 M 6.6,12 L 20,12"), "返回", () => RunKeyAsync("4"));
        AddButton(panel, Geometry.Parse("M 7,5.3 L 18.4,5.3 C 19.5,5.3 20.4,6.2 20.4,7.3 L 20.4,18.4 C 20.4,19.5 19.5,20.4 18.4,20.4 L 7,20.4 C 5.9,20.4 5,19.5 5,18.4 L 5,7.3 C 5,6.2 5.9,5.3 7,5.3 Z M 3.5,8.3 L 3.5,18.4 M 8,3.5 L 18,3.5"), "最近任务", () => RunKeyAsync("187"));
        AddButton(panel, Geometry.Parse("M 4.7,7.8 L 7.2,7.8 L 8.8,5.5 L 15.2,5.5 L 16.8,7.8 L 19.3,7.8 C 20.3,7.8 21,8.5 21,9.5 L 21,18.1 C 21,19.1 20.3,19.8 19.3,19.8 L 4.7,19.8 C 3.7,19.8 3,19.1 3,18.1 L 3,9.5 C 3,8.5 3.7,7.8 4.7,7.8 Z M 12,10 A 3.5,3.5 0 1 0 12,17 A 3.5,3.5 0 1 0 12,10"), "截图", CaptureAsync);
        _recordButton = AddButton(panel, new EllipseGeometry(new Point(12, 12), 5.7, 5.7), "开始录屏", ToggleRecordingAsync, true);

        _shell = new Border
        {
            Background = (Brush)Application.Current.FindResource("ToolbarGlassBrush"),
            BorderBrush = new SolidColorBrush(NeutralBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(26),
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
        _mainWindow.StateChanged += MainWindow_StateChanged;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _snapTimer.Stop();
            _mainWindow.StateChanged -= MainWindow_StateChanged;
            StopTrackingScrcpyWindow();
        };
    }

    private static FrameworkElement CreateDragDots()
    {
        var dots = new Canvas { Width = 15, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 2; col++)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 2.6, Height = 2.6, Fill = new SolidColorBrush(Color.FromRgb(89, 124, 162)) };
                Canvas.SetLeft(dot, 3 + col * 6);
                Canvas.SetTop(dot, 1.6 + row * 4.6);
                dots.Children.Add(dot);
            }
        }
        return dots;
    }

    private static FrameworkElement CreateIcon(Geometry geometry, bool filled = false)
    {
        var ink = new SolidColorBrush(filled ? Color.FromRgb(238, 76, 94) : Color.FromRgb(42, 76, 116));
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = filled ? null : ink,
            StrokeThickness = filled ? 0 : 1.85,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = filled ? ink : Brushes.Transparent,
            IsHitTestVisible = false
        });
        return canvas;
    }

    private Button AddButton(Panel panel, Geometry icon, string tooltip, Func<Task> action, bool filled = false)
    {
        var button = new Button
        {
            Content = CreateIcon(icon, filled),
            Style = (Style)Application.Current.FindResource("ToolbarButtonStyle"),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = tooltip,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        AutomationProperties.SetName(button, tooltip);
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
            GlassDialog.Message(this, "截图完成", $"截图已保存：\n{file}");
        }
        catch (Exception ex) { GlassDialog.Message(this, "截图失败", ex.Message, true); }
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
                    if (GlassDialog.Choice(this, "录屏完成", $"录屏已保存（{size / 1024.0 / 1024.0:F1} MB）：\n{file}", "打开文件夹"))
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
            GlassDialog.Message(this, "录屏失败", ex.Message, true);
        }
        finally
        {
            _recordButton.IsEnabled = true;
        }
    }

    private void SetRecordingState(bool recording)
    {
        _recordButton.Content = recording
            ? CreateIcon(new RectangleGeometry(new Rect(6.5, 6.5, 11, 11), 2.6, 2.6), true)
            : CreateIcon(new EllipseGeometry(new Point(12, 12), 5.7, 5.7), true);
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
        TrackScrcpyWindow(handle);
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

    private void MainWindow_StateChanged(object? sender, EventArgs e) => FollowScrcpyWindow();

    private void TrackScrcpyWindow(IntPtr handle)
    {
        if (handle == _trackedScrcpyHandle && _locationHook != IntPtr.Zero && _minimizeHook != IntPtr.Zero) return;
        StopTrackingScrcpyWindow();
        _trackedScrcpyHandle = handle;
        Native.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0) return;
        _locationHook = Native.SetWinEventHook(Native.EventObjectLocationChange, Native.EventObjectLocationChange,
            IntPtr.Zero, _winEventProc, processId, 0, Native.WinEventOutOfContext | Native.WinEventSkipOwnProcess);
        _minimizeHook = Native.SetWinEventHook(Native.EventSystemMinimizeStart, Native.EventSystemMinimizeEnd,
            IntPtr.Zero, _winEventProc, processId, 0, Native.WinEventOutOfContext | Native.WinEventSkipOwnProcess);
    }

    private void StopTrackingScrcpyWindow()
    {
        if (_locationHook != IntPtr.Zero) Native.UnhookWinEvent(_locationHook);
        if (_minimizeHook != IntPtr.Zero) Native.UnhookWinEvent(_minimizeHook);
        _locationHook = IntPtr.Zero;
        _minimizeHook = IntPtr.Zero;
        _trackedScrcpyHandle = IntPtr.Zero;
    }

    private void OnScrcpyWindowEvent(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (hwnd != _trackedScrcpyHandle || objectId != 0 || Dispatcher.HasShutdownStarted
            || Interlocked.Exchange(ref _followQueued, 1) != 0) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() =>
        {
            Interlocked.Exchange(ref _followQueued, 0);
            if (IsLoaded) FollowScrcpyWindow();
        }));
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
            MoveToolbarToPhysical(target.Left, target.Top);
    }

    private void MoveToolbarToPhysical(int left, int top)
    {
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var point = transform.Transform(new Point(left, top));
        Left = point.X;
        Top = point.Y;
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
        MoveToolbarToPhysical(left, top);
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
                GlassDialog.Message(this, "录屏完成", $"录屏已保存：\n{file}");
            await Dispatcher.InvokeAsync(Close);
        }
        catch (Exception ex)
        {
            _closing = false;
            GlassDialog.Message(this, "录屏失败", $"录屏尚未安全保存，快捷栏会保留供重试：\n{ex.Message}", true);
        }
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "Android设备" : value;
    }

    private static class Native
    {
        public const uint EventObjectLocationChange = 0x800B;
        public const uint EventSystemMinimizeStart = 0x0016;
        public const uint EventSystemMinimizeEnd = 0x0017;
        public const uint WinEventOutOfContext = 0x0000;
        public const uint WinEventSkipOwnProcess = 0x0002;
        private const int ExtendedFrameBounds = 9;

        public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint eventThread, uint eventTime);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? className, string? windowName);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
            WinEventProc callback, uint processId, uint threadId, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
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
