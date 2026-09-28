using Android投屏助手.Models;
using Android投屏助手.Services;
using Android投屏助手.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Android投屏助手;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ScrcpyService _scrcpy;
    private readonly DispatcherTimer _deviceTimer;
    private readonly Dictionary<string, FloatingToolbarWindow> _toolbars = new(StringComparer.OrdinalIgnoreCase);
    private bool _exitInProgress;
    private bool _exitApproved;
    private bool _layoutUpdateQueued;

    public MainWindow()
    {
        InitializeComponent();
        GlassWindowEffects.EnableRoundedCorners(this);
        var root = FindBundleRoot();
        var scrcpyRoot = System.IO.Path.Combine(root, "scrcpy");
        var adb = new AdbService(System.IO.Path.Combine(scrcpyRoot, "adb.exe"));
        _scrcpy = new ScrcpyService(System.IO.Path.Combine(scrcpyRoot, "scrcpy.exe"), adb.Path);
        _viewModel = new MainViewModel(adb, _scrcpy, new SettingsService());
        DataContext = _viewModel;
        _viewModel.Devices.CollectionChanged += (_, _) => QueueLayoutUpdate();
        UpdateThemeButton();
        _deviceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _deviceTimer.Tick += async (_, _) =>
        {
            if (!_viewModel.IsBusy) await _viewModel.RefreshAsync();
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Adb.Exists)
        {
            GlassDialog.Message(this, "缺少程序组件", "没有找到 scrcpy 文件夹或 adb.exe，请检查程序文件是否完整。", true);
            return;
        }
        await _viewModel.RefreshAsync();
        if (_exitInProgress || _exitApproved) return;
        _deviceTimer.Start();
        // 仅在首次打开时自动启动唯一的已授权设备；后续刷新和多设备连接仍由用户选择。
        if (_viewModel.Devices.Count == 1 && _viewModel.Devices[0].Status == DeviceStatus.Online)
            await StartMirrorForDeviceAsync(_viewModel.Devices[0]);
    }

    private void Header_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is ButtonBase) return;
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private void WirelessButton_Click(object sender, RoutedEventArgs e) => WirelessPopup.IsOpen = !WirelessPopup.IsOpen;
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.SetDark(!ThemeManager.IsDark);
        UpdateThemeButton();
    }
    private void UpdateThemeButton()
    {
        ThemeMoonIcon.Visibility = ThemeManager.IsDark ? Visibility.Visible : Visibility.Collapsed;
        ThemeSunIcon.Visibility = ThemeManager.IsDark ? Visibility.Collapsed : Visibility.Visible;
        ThemeLabel.Text = ThemeManager.IsDark ? "深色" : "浅色";
        ThemeButton.ToolTip = ThemeManager.IsDark ? "黑夜模式已开启，点击切换为浅色模式" : "浅色模式已开启，点击切换为黑夜模式";
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitApproved) return;
        e.Cancel = true;
        if (_exitInProgress) return;
        if (!GlassDialog.Confirm(this, "退出助手", "关闭助手将结束投屏。正在录制的视频会先尝试保存。\n确定要退出吗？", "结束并退出", "继续使用")) return;
        _exitInProgress = true;
        _deviceTimer.Stop();
        try
        {
            foreach (var toolbar in _toolbars.Values.ToArray()) await toolbar.CloseForShutdownAsync();
            _scrcpy.StopAll();
            _exitApproved = true;
            await Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Close();
        }
        catch (Exception ex)
        {
            _exitInProgress = false;
            if (IsLoaded) _deviceTimer.Start();
            GlassDialog.Message(this, "退出未完成", $"仍有投屏进程未能结束，请重试：\n{ex.Message}", true);
        }
    }

    private async void AddWireless_Click(object sender, RoutedEventArgs e)
    {
        WirelessPopup.IsOpen = false;
        var address = Prompt("IP 直连", "请输入无线连接地址（例如 192.168.1.10:38467）：");
        if (!string.IsNullOrWhiteSpace(address) && !await _viewModel.ConnectIpAsync(address.Trim()))
            GlassDialog.Message(this, "无线连接失败", _viewModel.StatusText, true);
    }

    private async void PairCode_Click(object sender, RoutedEventArgs e)
    {
        WirelessPopup.IsOpen = false;
        var values = ShowPairDialog();
        if (values is not null && !await _viewModel.PairCodeAsync(values.Value.PairAddress, values.Value.Code, values.Value.ConnectAddress))
            GlassDialog.Message(this, "无线配对失败", _viewModel.StatusText, true);
    }

    private async void StartMirror_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not DeviceViewModel item) return;
        await StartMirrorForDeviceAsync(item);
    }

    private void QueueLayoutUpdate()
    {
        if (_layoutUpdateQueued || Dispatcher.HasShutdownStarted) return;
        _layoutUpdateQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _layoutUpdateQueued = false;
            var targetWidth = _viewModel.Devices.Count > 1 ? 660 : 520;
            if (Math.Abs(Width - targetWidth) < 1) return;
            var center = Left + Width / 2;
            Width = targetWidth;
            var screenLeft = SystemParameters.VirtualScreenLeft;
            var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
            Left = Math.Clamp(center - targetWidth / 2, screenLeft, Math.Max(screenLeft, screenRight - targetWidth));
        }));
    }

    private async Task StartMirrorForDeviceAsync(DeviceViewModel item)
    {
        if (_exitInProgress || _exitApproved || item.Status is DeviceStatus.Mirroring or DeviceStatus.Connecting) return;
        try
        {
            var process = await _viewModel.StartMirrorAsync(item);
            var toolbar = new FloatingToolbarWindow(item, _viewModel.Adb, process, $"投屏助手 - {item.DisplayName}");
            var attached = false;
            for (var attempt = 0; attempt < 25; attempt++)
            {
                if (toolbar.AttachToProjectionWindow()) { attached = true; break; }
                if (process.HasExited) break;
                await Task.Delay(100);
            }
            if (!attached)
            {
                toolbar.Close();
                _viewModel.StopMirror(item);
                throw new InvalidOperationException("投屏窗口未能创建，快捷栏无法绑定到投屏窗口。");
            }
            _toolbars[item.Serial] = toolbar;
            toolbar.Closed += (_, _) =>
            {
                if (_toolbars.TryGetValue(item.Serial, out var current) && ReferenceEquals(current, toolbar))
                    _toolbars.Remove(item.Serial);
            };
            process.Exited += (_, _) =>
            {
                if (Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_exitInProgress && _toolbars.TryGetValue(item.Serial, out var current) && ReferenceEquals(current, toolbar))
                        toolbar.Close();
                    item.Status = DeviceStatus.Online;
                }));
            };
            toolbar.Show();
            if (process.HasExited) toolbar.Close();
        }
        catch (Exception ex) { GlassDialog.Message(this, "投屏启动失败", ex.Message, true); }
    }

    private async void StopMirror_Click(object sender, RoutedEventArgs e)
    {
        if (_exitInProgress) return;
        if ((sender as Button)?.Tag is not DeviceViewModel item) return;
        if (item.Status != DeviceStatus.Mirroring) return;
        try
        {
            if (_toolbars.TryGetValue(item.Serial, out var toolbar)) await toolbar.CloseForShutdownAsync();
            _viewModel.StopMirror(item);
        }
        catch (Exception ex) { GlassDialog.Message(this, "结束投屏失败", ex.Message, true); }
    }

    private static string FindBundleRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && current is not null; i++, current = current.Parent)
        {
            if (Directory.Exists(System.IO.Path.Combine(current.FullName, "scrcpy"))) return current.FullName;
        }
        return AppContext.BaseDirectory;
    }

    private string? Prompt(string title, string message)
    {
        var box = new TextBox { Width = 330, Margin = new Thickness(0, 8, 0, 10) };
        var ok = new Button { Content = "连接", IsDefault = true, Width = 80, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 90, 0) };
        var buttons = new Grid(); buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        var tip = new TextBlock
        {
            Text = "手机：设置 → 开发者选项 → 无线调试。请填写无线调试主页面的 IP 地址和连接端口，不要填写配对弹窗中的临时端口。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 16)
        };
        tip.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        panel.Children.Add(tip);
        panel.Children.Add(buttons);
        var window = GlassDialog.Form(this, title, 430, 285, panel);
        ok.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true ? box.Text : null;
    }

    private (string PairAddress, string Code, string? ConnectAddress)? ShowPairDialog()
    {
        var pair = new TextBox { Margin = new Thickness(0, 4, 0, 10) };
        var code = new TextBox { Margin = new Thickness(0, 4, 0, 10) };
        var connect = new TextBox { Margin = new Thickness(0, 4, 0, 16) };
        var ok = new Button { Content = "开始配对", IsDefault = true, Width = 90, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 100, 0) };
        var buttons = new Grid(); buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(22) };
        var tip = new TextBlock
        {
            Text = "手机：设置 → 开发者选项 → 无线调试 → 使用配对码配对设备。配对地址和六位码在配对弹窗中；连接端口在无线调试主页面，两者通常不同。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12)
        };
        tip.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        panel.Children.Add(tip);
        panel.Children.Add(new TextBlock { Text = "配对地址（例如 192.168.1.10:42699）" }); panel.Children.Add(pair);
        panel.Children.Add(new TextBlock { Text = "六位配对码" }); panel.Children.Add(code);
        panel.Children.Add(new TextBlock { Text = "连接地址（可选，例如 192.168.1.10:38467）" }); panel.Children.Add(connect);
        panel.Children.Add(buttons);
        var window = GlassDialog.Form(this, "配对码连接", 480, 420, panel);
        ok.Click += (_, _) => window.DialogResult = true;
        if (window.ShowDialog() != true) return null;
        return (pair.Text.Trim(), code.Text.Trim(), string.IsNullOrWhiteSpace(connect.Text) ? null : connect.Text.Trim());
    }


}
