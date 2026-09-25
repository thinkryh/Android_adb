using Android投屏助手.Models;
using Android投屏助手.Services;
using Android投屏助手.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly DeviceViewModel _device;
    private readonly AdbService _adb;
    private readonly Process _scrcpyProcess;
    private readonly string _scrcpyTitle;
    private readonly ScreenRecorderService _recorder;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _snapTimer;
    private readonly Stopwatch _snapWatch = new();
    private readonly StackPanel _panel;
    private readonly Border _dragArea;
    private readonly Border _shell;
    private readonly Button _collapseButton;
    private readonly Button _pinButton;
    private readonly Button _captureButton;
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
    private bool _forceClose;
    private bool _hasClosed;
    private Task? _recordingCloseTask;
    private Task? _shutdownTask;
    private bool _pinEnabled;
    private bool _isCollapsed;

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
        _winEventProc = OnScrcpyWindowEvent;

        Title = "投屏快捷操作";
        Width = 60;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        Topmost = false;
        ShowInTaskbar = false;
        _panel = new StackPanel { Margin = new Thickness(7, 7, 7, 7) };
        _dragArea = new Border
        {
            Width = 44,
            Height = 12,
            Margin = new Thickness(0, 0, 0, 5),
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            ToolTip = "移动工具栏"
        };
        _dragArea.MouseLeftButtonDown += Handle_MouseLeftButtonDown;
        _panel.Children.Add(_dragArea);

        _collapseButton = AddButton(_panel, Geometry.Parse("M 7,15 L 12,10 L 17,15"), "收起工具栏", ToggleCollapseAsync);
        _collapseButton.Height = 26;
        _collapseButton.Margin = new Thickness(0, 0, 0, 5);

        _pinButton = AddButton(_panel, Geometry.Parse("M 8,3.5 L 16,3.5 L 15,8.5 L 18,11.5 L 18,13.5 L 6,13.5 L 6,11.5 L 9,8.5 Z M 12,13.5 L 12,21"), "置顶投屏窗口", TogglePinAsync);
        AddButton(_panel, Geometry.Parse("M 3.5,10.5 L 12,3.8 L 20.5,10.5 M 5.5,9.6 L 5.5,20 L 18.5,20 L 18.5,9.6 M 9.5,20 L 9.5,14.3 L 14.5,14.3 L 14.5,20"), "主页", () => RunKeyAsync("3"));
        AddButton(_panel, Geometry.Parse("M 13.5,5.2 L 6.6,12 L 13.5,18.8 M 6.6,12 L 20,12"), "返回", () => RunKeyAsync("4"));
        AddButton(_panel, Geometry.Parse("M 7,5.3 L 18.4,5.3 C 19.5,5.3 20.4,6.2 20.4,7.3 L 20.4,18.4 C 20.4,19.5 19.5,20.4 18.4,20.4 L 7,20.4 C 5.9,20.4 5,19.5 5,18.4 L 5,7.3 C 5,6.2 5.9,5.3 7,5.3 Z M 3.5,8.3 L 3.5,18.4 M 8,3.5 L 18,3.5"), "最近任务", () => RunKeyAsync("187"));
        _captureButton = AddButton(_panel, Geometry.Parse("M 4.7,7.8 L 7.2,7.8 L 8.8,5.5 L 15.2,5.5 L 16.8,7.8 L 19.3,7.8 C 20.3,7.8 21,8.5 21,9.5 L 21,18.1 C 21,19.1 20.3,19.8 19.3,19.8 L 4.7,19.8 C 3.7,19.8 3,19.1 3,18.1 L 3,9.5 C 3,8.5 3.7,7.8 4.7,7.8 Z M 12,10 A 3.5,3.5 0 1 0 12,17 A 3.5,3.5 0 1 0 12,10"), "快速截图（右键保存原画）", () => CaptureAsync(false));
        _captureButton.PreviewMouseRightButtonUp += async (_, e) =>
        {
            e.Handled = true;
            await CaptureAsync(true);
        };
        _recordButton = AddButton(_panel, new EllipseGeometry(new Point(12, 12), 8.55, 8.55), "开始录屏", ToggleRecordingAsync, true);

        _shell = new Border
        {
            BorderBrush = (Brush)Application.Current.FindResource("ToolbarBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Child = _panel
        };
        _shell.SetResourceReference(Border.BackgroundProperty, "ToolbarGlassBrush");
        Content = _shell;
        ThemeManager.Changed += UpdateTheme;

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
        Closed += (_, _) =>
        {
            _hasClosed = true;
            _timer.Stop();
            _snapTimer.Stop();
            StopTrackingScrcpyWindow();
            ThemeManager.Changed -= UpdateTheme;
        };
    }

    private void UpdateTheme()
    {
        _shell.BorderBrush = new SolidColorBrush(_isSnapping ? ThemeManager.ToolbarSnapBorder :
            _isDocked ? ThemeManager.ToolbarDockedBorder : ThemeManager.ToolbarNeutralBorder);
    }

    private static FrameworkElement CreateIcon(Geometry geometry, bool filled = false, string? fillBrushKey = null)
    {
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        var path = new System.Windows.Shapes.Path
        {
            Data = geometry,
            StrokeThickness = filled ? 0 : 1.85,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = filled ? null : Brushes.Transparent,
            IsHitTestVisible = false
        };
        path.SetResourceReference(filled ? System.Windows.Shapes.Shape.FillProperty : System.Windows.Shapes.Shape.StrokeProperty,
            filled ? fillBrushKey ?? "RecordBrush" : "ToolbarIconBrush");
        canvas.Children.Add(path);
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
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        SetHint(button, tooltip);
        button.Click += async (_, _) => await action();
        panel.Children.Add(button);
        return button;
    }

    private static void SetHint(Button button, string label)
    {
        var hint = button.ToolTip as ToolTip ?? new ToolTip
        {
            Placement = PlacementMode.Left,
            PlacementTarget = button
        };
        hint.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 220 };
        button.ToolTip = hint;
        AutomationProperties.SetName(button, label);
        ToolTipService.SetInitialShowDelay(button, 280);
        ToolTipService.SetShowDuration(button, 7000);
        ToolTipService.SetShowOnDisabled(button, true);
    }

    private void ShowNotice(string message)
    {
        var notice = new ToolTip
        {
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 220 },
            Placement = PlacementMode.Left,
            PlacementTarget = _shell,
            StaysOpen = true,
            IsOpen = true
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { timer.Stop(); notice.IsOpen = false; };
        timer.Start();
    }

    private Task ToggleCollapseAsync()
    {
        _snapTimer.Stop();
        _isSnapping = false;
        if (!_isCollapsed && !_isDocked)
        {
            var projection = GetScrcpyHandle();
            var toolbar = GetToolbarHandle();
            if (projection != IntPtr.Zero && toolbar != IntPtr.Zero
                && Native.GetDockWindowRect(projection, out var projectionRect)
                && Native.GetVisibleWindowRect(toolbar, out var toolbarRect))
            {
                var leftDistance = Math.Abs(toolbarRect.Right - projectionRect.Left);
                var rightDistance = Math.Abs(toolbarRect.Left - projectionRect.Right);
                _dockSide = leftDistance < rightDistance ? DockSide.Left : DockSide.Right;
                _dockTopOffsetPx = Math.Max(0, toolbarRect.Top - projectionRect.Top);
            }
            _isDocked = true;
        }

        _isCollapsed = !_isCollapsed;
        _dragArea.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        foreach (UIElement child in _panel.Children)
            if (child is Button button && !ReferenceEquals(button, _collapseButton))
                button.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        _panel.Margin = new Thickness(_isCollapsed ? 4 : 7);
        Width = _isCollapsed ? 36 : 60;
        _collapseButton.Width = _isCollapsed ? 28 : 44;
        _collapseButton.Height = _isCollapsed ? 28 : 26;
        _collapseButton.Margin = new Thickness(0, 0, 0, _isCollapsed ? 0 : 5);
        UpdateCollapseButton();
        _shell.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(160)));
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_hasClosed) return;
            UpdateLayout();
            FollowScrcpyWindow();
        }));
        return Task.CompletedTask;
    }

    private void UpdateCollapseButton()
    {
        var icon = (Canvas)CreateIcon(Geometry.Parse(_isCollapsed
            ? "M 7,9 L 12,14 L 17,9"
            : "M 7,15 L 12,10 L 17,15"));
        if (_isCollapsed && _recorder.HasPendingRecording)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, IsHitTestVisible = false };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "RecordActiveBrush");
            Canvas.SetLeft(dot, 18);
            Canvas.SetTop(dot, 2);
            icon.Children.Add(dot);
        }
        _collapseButton.Content = icon;
        SetHint(_collapseButton, _isCollapsed
            ? _recorder.HasPendingRecording ? "展开工具栏（录屏中）" : "展开工具栏"
            : "收起工具栏");
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

    private Task TogglePinAsync()
    {
        var handle = GetScrcpyHandle();
        if (handle == IntPtr.Zero)
        {
            GlassDialog.Message(this, "无法置顶", "投屏窗口尚未就绪，请稍后重试。");
            return Task.CompletedTask;
        }

        var pin = !_pinEnabled;
        if (!Native.SetWindowPos(handle, pin ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0,
            Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate))
        {
            GlassDialog.Message(this, "置顶失败", "无法更改投屏窗口的置顶状态。", true);
            return Task.CompletedTask;
        }

        _pinEnabled = pin;
        var label = pin ? "取消投屏窗口置顶" : "置顶投屏窗口";
        SetHint(_pinButton, label);
        if (pin)
        {
            _pinButton.SetResourceReference(Control.BackgroundProperty, "AccentLightBrush");
            _pinButton.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
        }
        else
        {
            _pinButton.ClearValue(Control.BackgroundProperty);
            _pinButton.ClearValue(Control.BorderBrushProperty);
        }
        return Task.CompletedTask;
    }

    private async Task RunKeyAsync(string key)
    {
        try { await _adb.RunAsync(["-s", _device.Serial, "shell", "input", "keyevent", key]); } catch { }
    }

    private async Task CaptureAsync(bool original)
    {
        if (!_captureButton.IsEnabled) return;
        _captureButton.IsEnabled = false;
        SetHint(_captureButton, "正在保存截图…");
        try
        {
            var folder = MediaStorage.GetFolder();
            var file = Path.Combine(folder, $"{Sanitize(_device.DisplayName)}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            var fast = !original && await ProjectionCaptureService.TryCaptureAsync(_scrcpyProcess, file);
            if (!fast) await _adb.CapturePngToFileAsync(_device.Serial, file);
            ShowNotice($"{(fast ? "快速截图" : "原画截图")}已保存到：\n{folder}");
        }
        catch (Exception ex) { GlassDialog.Message(this, "截图失败", ex.Message, true); }
        finally
        {
            _captureButton.IsEnabled = true;
            SetHint(_captureButton, "快速截图（右键保存原画）");
        }
    }

    private async Task ToggleRecordingAsync()
    {
        if (!_recordButton.IsEnabled) return;
        _recordButton.IsEnabled = false;
        try
        {
            if (_recorder.HasPendingRecording)
            {
                SetHint(_recordButton, "正在保存录屏，请稍候…");
                ShowNotice("正在封装并传输录屏，请稍候…");
                var file = await _recorder.StopAsync();
                SetRecordingState(false);
                if (!string.IsNullOrWhiteSpace(file))
                    ShowNotice($"录屏已保存到：\n{Path.GetDirectoryName(file)}");
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
            ? CreateIcon(new RectangleGeometry(new Rect(3.75, 3.75, 16.5, 16.5), 3.9, 3.9), true, "RecordActiveBrush")
            : CreateIcon(new EllipseGeometry(new Point(12, 12), 8.55, 8.55), true);
        SetHint(_recordButton, recording ? "结束录屏并保存" : "开始录屏");
        UpdateCollapseButton();
    }

    public bool AttachToProjectionWindow()
    {
        var handle = GetScrcpyHandle();
        if (handle == IntPtr.Zero) return false;
        new WindowInteropHelper(this).Owner = handle;
        return true;
    }

    public Task CloseForShutdownAsync() => _shutdownTask ??= CloseForShutdownCoreAsync();

    private async Task CloseForShutdownCoreAsync()
    {
        if (_recordingCloseTask is not null) await _recordingCloseTask;
        else if (_recorder.HasPendingRecording)
        {
            try { await _recorder.StopAsync(); }
            catch (Exception ex)
            {
                GlassDialog.Message(this, "录屏未保存", $"投屏即将结束，录屏文件未能安全导出：\n{ex.Message}", true);
            }
        }
        _forceClose = true;
        if (!_hasClosed) Close();
    }

    private void FollowScrcpyWindow()
    {
        if (_isDragging)
        {
            UpdateSnapPreview();
            return;
        }
        var handle = GetScrcpyHandle();
        if (handle == IntPtr.Zero) return;
        TrackScrcpyWindow(handle);
        if (Native.IsIconic(handle) || !Native.IsWindowVisible(handle))
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
        if (_isSnapping) return;
        if (_isDocked && Native.GetDockWindowRect(handle, out var rect)) ApplyDockPosition(rect);
    }

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
            _shell.BorderBrush = new SolidColorBrush(ThemeManager.ToolbarNeutralBorder);
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
        _shell.BorderBrush = new SolidColorBrush(ThemeManager.ToolbarSnapBorder);
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
        _shell.BorderBrush = new SolidColorBrush(nearby ? ThemeManager.ToolbarSnapBorder : ThemeManager.ToolbarNeutralBorder);
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
        var brush = new SolidColorBrush(ThemeManager.ToolbarSnapBorder);
        _shell.BorderBrush = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(ThemeManager.ToolbarDockedBorder, TimeSpan.FromMilliseconds(350)));
        FollowScrcpyWindow();
    }

    private IntPtr GetToolbarHandle() => new WindowInteropHelper(this).Handle;

    private IntPtr GetScrcpyHandle()
    {
        try
        {
            if (!_scrcpyProcess.HasExited)
            {
                var named = Native.FindWindow(null, _scrcpyTitle);
                if (named != IntPtr.Zero)
                {
                    Native.GetWindowThreadProcessId(named, out var processId);
                    if (processId == _scrcpyProcess.Id) return named;
                }
                _scrcpyProcess.Refresh();
                if (_scrcpyProcess.MainWindowHandle != IntPtr.Zero) return _scrcpyProcess.MainWindowHandle;
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_forceClose)
        {
            base.OnClosing(e);
            return;
        }
        if (_closing && _recorder.HasPendingRecording)
        {
            e.Cancel = true;
            return;
        }
        if (!_closing && _recorder.HasPendingRecording)
        {
            e.Cancel = true;
            _closing = true;
            _recordingCloseTask = CloseAfterRecordingAsync();
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
            _recordingCloseTask = null;
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
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoActivate = 0x0010;
        private const int ExtendedFrameBounds = 9;

        public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint eventThread, uint eventTime);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? className, string? windowName);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
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
