using Android投屏助手.Models;
using Android投屏助手.Services;
using Android投屏助手.ViewModels;
using QRCoder;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        ThemeButton.Content = ThemeManager.IsDark ? "浅色模式" : "黑夜模式";
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
            GlassDialog.Message(this, "缺少程序组件", "没有找到 scrcpy 文件夹或 adb.exe。\n请保持发布目录中的 scrcpy 文件夹完整。", true);
            return;
        }
        await _viewModel.RefreshAsync();
        if (_exitInProgress || _exitApproved) return;
        _deviceTimer.Start();
        // 仅在首次打开时自动启动唯一的已授权设备；后续刷新和多设备连接仍由用户选择。
        if (_viewModel.Devices.Count == 1 && _viewModel.Devices[0].Status == DeviceStatus.Online)
            await StartMirrorForDeviceAsync(_viewModel.Devices[0]);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.SetDark(!ThemeManager.IsDark);
        ThemeButton.Content = ThemeManager.IsDark ? "浅色模式" : "黑夜模式";
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitApproved) return;
        e.Cancel = true;
        if (_exitInProgress) return;
        if (!GlassDialog.Confirm(this, "退出助手", "关闭助手将结束所有投屏和快捷栏；正在录制的视频会先尝试保存。\n确定要退出吗？", "结束并退出", "继续使用")) return;
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
        var address = Prompt("IP 直连", "请输入无线连接地址（例如 192.168.1.10:45915）：");
        if (!string.IsNullOrWhiteSpace(address)) await _viewModel.ConnectIpAsync(address.Trim());
    }

    private async void PairCode_Click(object sender, RoutedEventArgs e)
    {
        var values = ShowPairDialog();
        if (values is not null) await _viewModel.PairCodeAsync(values.Value.PairAddress, values.Value.Code, values.Value.ConnectAddress);
    }

    private async void Mdns_Click(object sender, RoutedEventArgs e)
    {
        var services = await _viewModel.DiscoverMdnsAsync();
        var connect = services.Where(x => !x.IsPairing).ToList();
        if (connect.Count == 0)
        {
            GlassDialog.Message(this, "mDNS 发现", "没有发现可连接的无线调试服务。\n请确认手机已开启无线调试，并与电脑连接同一网络。");
            return;
        }
        var selected = ShowSelectionDialog("选择无线设备", connect.Select(x => $"{x.Address}  {x.ServiceName}").ToList());
        if (selected is not null) await _viewModel.ConnectIpAsync(connect[selected.Value].Address);
    }

    private async void QrPair_Click(object sender, RoutedEventArgs e)
    {
        var serviceName = "studio-" + Guid.NewGuid().ToString("N")[..10];
        var password = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString();
        var payload = $"WIFI:T:ADB;S:{serviceName};P:{password};;";
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrData);
        var bytes = qrCode.GetGraphic(8);
        var image = new Image { Source = ToBitmap(bytes), Width = 300, Height = 300, Margin = new Thickness(0, 12, 0, 8) };
        var start = new Button { Content = "我已扫码，开始查找", Padding = new Thickness(16, 8, 16, 8), HorizontalAlignment = HorizontalAlignment.Center };
        var status = new TextBlock { Text = "手机：设置 → 开发者选项 → 无线调试 → 使用二维码配对", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(25, 0, 25, 12), Foreground = (Brush)Application.Current.FindResource("MutedTextBrush") };
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        using var pairingCancellation = new CancellationTokenSource();
        panel.Children.Add(new TextBlock { Text = "请使用手机无线调试页面扫描", FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) });
        panel.Children.Add(image);
        panel.Children.Add(status);
        panel.Children.Add(start);
        var window = GlassDialog.Form(this, "扫码配对", 430, 590, panel);
        window.Closed += (_, _) => pairingCancellation.Cancel();
        start.Click += async (_, _) =>
        {
            start.IsEnabled = false;
            status.Text = "正在等待手机广播配对服务…";
            try
            {
                var endpoint = await WaitForMdnsAsync(serviceName, true, TimeSpan.FromMinutes(2), pairingCancellation.Token);
                if (endpoint is null) throw new InvalidOperationException("等待扫码超时，请保持手机和电脑在同一网络。");
                if (!await _viewModel.Adb.PairAsync(endpoint.Address, password)) throw new InvalidOperationException("扫码配对失败。");
                status.Text = "配对成功，正在查找连接服务…";
                var connect = await WaitForMdnsAsync(string.Empty, false, TimeSpan.FromSeconds(30), pairingCancellation.Token);
                if (connect is not null) await _viewModel.ConnectIpAsync(connect.Address);
                status.Text = "配对成功，可以关闭此窗口。";
            }
            catch (OperationCanceledException) { status.Text = "扫码配对已取消。"; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { start.IsEnabled = true; }
        };
        window.ShowDialog();
    }

    private async Task<MdnsEndpoint?> WaitForMdnsAsync(string serviceName, bool pairing, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var services = await _viewModel.DiscoverMdnsAsync();
            var endpoint = services.FirstOrDefault(x => x.IsPairing == pairing && (string.IsNullOrWhiteSpace(serviceName) || x.ServiceName.Contains(serviceName, StringComparison.OrdinalIgnoreCase)));
            if (endpoint is not null) return endpoint;
            await Task.Delay(1000, cancellationToken);
        }
        return null;
    }

    private async void StartMirror_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not DeviceViewModel item) return;
        await StartMirrorForDeviceAsync(item);
    }

    private async Task StartMirrorForDeviceAsync(DeviceViewModel item)
    {
        if (_exitInProgress || _exitApproved || item.Status is DeviceStatus.Mirroring or DeviceStatus.Connecting) return;
        try
        {
            var process = await _viewModel.StartMirrorAsync(item);
            var toolbar = new FloatingToolbarWindow(item, _viewModel.Adb, process, $"Android投屏助手 - {item.DisplayName}");
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

    private static BitmapImage ToBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private string? Prompt(string title, string message)
    {
        var box = new TextBox { Width = 330, Margin = new Thickness(0, 8, 0, 18) };
        var ok = new Button { Content = "连接", IsDefault = true, Width = 80, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 90, 0) };
        var buttons = new Grid(); buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box); panel.Children.Add(buttons);
        var window = GlassDialog.Form(this, title, 430, 240, panel);
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
        panel.Children.Add(new TextBlock { Text = "配对地址（例如 192.168.1.10:42699）" }); panel.Children.Add(pair);
        panel.Children.Add(new TextBlock { Text = "六位配对码" }); panel.Children.Add(code);
        panel.Children.Add(new TextBlock { Text = "连接地址（可选，例如 192.168.1.10:45915）" }); panel.Children.Add(connect);
        panel.Children.Add(buttons);
        var window = GlassDialog.Form(this, "配对码连接", 480, 365, panel);
        ok.Click += (_, _) => window.DialogResult = true;
        if (window.ShowDialog() != true) return null;
        return (pair.Text.Trim(), code.Text.Trim(), string.IsNullOrWhiteSpace(connect.Text) ? null : connect.Text.Trim());
    }

    private int? ShowSelectionDialog(string title, IReadOnlyList<string> items)
    {
        var list = new ListBox { ItemsSource = items, SelectedIndex = 0, Margin = new Thickness(18) };
        var ok = new Button { Content = "连接选中设备", IsDefault = true, Width = 120, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 0, 18, 18) };
        var panel = new DockPanel(); DockPanel.SetDock(ok, Dock.Bottom); panel.Children.Add(ok); panel.Children.Add(list);
        var window = GlassDialog.Form(this, title, 500, 350, panel);
        ok.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true && list.SelectedIndex >= 0 ? list.SelectedIndex : null;
    }
}
