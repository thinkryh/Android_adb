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

    public MainWindow()
    {
        InitializeComponent();
        var root = FindBundleRoot();
        var scrcpyRoot = System.IO.Path.Combine(root, "scrcpy");
        var adb = new AdbService(System.IO.Path.Combine(scrcpyRoot, "adb.exe"));
        _scrcpy = new ScrcpyService(System.IO.Path.Combine(scrcpyRoot, "scrcpy.exe"), adb.Path);
        _viewModel = new MainViewModel(adb, _scrcpy, new SettingsService());
        DataContext = _viewModel;
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
            MessageBox.Show("没有找到 scrcpy 文件夹或 adb.exe。\n请保持发布目录中的 scrcpy 文件夹完整。", "Android投屏助手", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await _viewModel.RefreshAsync();
        _deviceTimer.Start();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _deviceTimer.Stop();
        if (MessageBox.Show("关闭主界面后，已打开的投屏窗口仍会继续运行。\n要退出助手并关闭主界面吗？", "Android投屏助手", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
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
            MessageBox.Show("没有发现可连接的无线调试服务。\n请确认手机已开启无线调试，并与电脑连接同一网络。", "mDNS 发现", MessageBoxButton.OK, MessageBoxImage.Information);
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
        var window = new Window
        {
            Title = "扫码配对",
            Width = 430,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Brushes.White,
            ResizeMode = ResizeMode.NoResize
        };
        var image = new Image { Source = ToBitmap(bytes), Width = 300, Height = 300, Margin = new Thickness(0, 12, 0, 8) };
        var start = new Button { Content = "我已扫码，开始查找", Padding = new Thickness(16, 8, 16, 8), HorizontalAlignment = HorizontalAlignment.Center };
        var status = new TextBlock { Text = "手机：设置 → 开发者选项 → 无线调试 → 使用二维码配对", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(25, 0, 25, 12), Foreground = Brushes.DimGray };
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        using var pairingCancellation = new CancellationTokenSource();
        window.Closed += (_, _) => pairingCancellation.Cancel();
        panel.Children.Add(new TextBlock { Text = "请使用手机无线调试页面扫描", FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) });
        panel.Children.Add(image);
        panel.Children.Add(status);
        panel.Children.Add(start);
        window.Content = panel;
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
        if (item.Status == DeviceStatus.Mirroring) return;
        try
        {
            var process = await _viewModel.StartMirrorAsync(item);
            var toolbar = new FloatingToolbarWindow(item, _viewModel.Adb, process, $"Android投屏助手 - {item.DisplayName}");
            process.Exited += (_, _) => Dispatcher.Invoke(() =>
            {
                if (toolbar.IsVisible) toolbar.Close();
                item.Status = DeviceStatus.Online;
            });
            toolbar.Show();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "投屏启动失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void StopMirror_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is DeviceViewModel item) _viewModel.StopMirror(item);
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

    private static string? Prompt(string title, string message)
    {
        var box = new TextBox { Width = 330, Margin = new Thickness(0, 8, 0, 18) };
        var ok = new Button { Content = "连接", IsDefault = true, Width = 80, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 90, 0) };
        var buttons = new Grid(); buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box); panel.Children.Add(buttons);
        var window = new Window { Title = title, Content = panel, Width = 430, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        ok.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true ? box.Text : null;
    }

    private static (string PairAddress, string Code, string? ConnectAddress)? ShowPairDialog()
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
        var window = new Window { Title = "配对码连接", Content = panel, Width = 480, Height = 330, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        ok.Click += (_, _) => window.DialogResult = true;
        if (window.ShowDialog() != true) return null;
        return (pair.Text.Trim(), code.Text.Trim(), string.IsNullOrWhiteSpace(connect.Text) ? null : connect.Text.Trim());
    }

    private static int? ShowSelectionDialog(string title, IReadOnlyList<string> items)
    {
        var list = new ListBox { ItemsSource = items, SelectedIndex = 0, Margin = new Thickness(18) };
        var ok = new Button { Content = "连接选中设备", IsDefault = true, Width = 120, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 0, 18, 18) };
        var panel = new DockPanel(); DockPanel.SetDock(ok, Dock.Bottom); panel.Children.Add(ok); panel.Children.Add(list);
        var window = new Window { Title = title, Content = panel, Width = 500, Height = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        ok.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true && list.SelectedIndex >= 0 ? list.SelectedIndex : null;
    }
}
