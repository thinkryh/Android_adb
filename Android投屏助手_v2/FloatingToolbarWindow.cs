using Android投屏助手.Models;
using Android投屏助手.Services;
using Android投屏助手.ViewModels;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Android投屏助手;

public sealed class FloatingToolbarWindow : Window
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);
    private readonly DeviceViewModel _device;
    private readonly AdbService _adb;
    private readonly string _scrcpyTitle;
    private readonly DispatcherTimer _timer;

    public FloatingToolbarWindow(DeviceViewModel device, AdbService adb, string scrcpyTitle)
    {
        _device = device; _adb = adb; _scrcpyTitle = scrcpyTitle;
        Title = "投屏快捷操作"; Width = 68; Height = 330;
        WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent; Topmost = true; ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(6) };
        AddButton(panel, "⌂", "主页", () => RunKeyAsync("3"));
        AddButton(panel, "‹", "返回", () => RunKeyAsync("4"));
        AddButton(panel, "▣", "最近任务", () => RunKeyAsync("187"));
        AddButton(panel, "＋", "音量加", () => RunKeyAsync("24"));
        AddButton(panel, "－", "音量减", () => RunKeyAsync("25"));
        AddButton(panel, "◎", "截图", CaptureAsync);
        AddButton(panel, "□", "置顶", () => { ToggleTopmost(); return Task.CompletedTask; });
        Content = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(232, 239, 247, 250)),
            CornerRadius = new CornerRadius(18),
            Child = panel
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => FollowScrcpyWindow();
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void AddButton(Panel panel, string text, string tooltip, Func<Task> action)
    {
        var button = new Button { Content = text, Width = 50, Height = 38, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 5), ToolTip = tooltip, FontSize = 18, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
        button.Click += async (_, _) => await action();
        panel.Children.Add(button);
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
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Android投屏助手");
            Directory.CreateDirectory(folder);
            var file = System.IO.Path.Combine(folder, $"{Sanitize(_device.DisplayName)}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            await File.WriteAllBytesAsync(file, bytes);
            MessageBox.Show($"截图已保存：\n{file}", "截图完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "截图失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ToggleTopmost()
    {
        var handle = Native.FindWindow(null, _scrcpyTitle);
        if (handle != IntPtr.Zero) Native.SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoActivate | SwpShowWindow);
    }

    private void FollowScrcpyWindow()
    {
        var handle = Native.FindWindow(null, _scrcpyTitle);
        if (handle == IntPtr.Zero || !Native.GetWindowRect(handle, out var rect)) return;
        Left = rect.Right + 8; Top = rect.Top;
    }

    private static string Sanitize(string value)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value;
    }

    private static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? className, string? windowName);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
}
