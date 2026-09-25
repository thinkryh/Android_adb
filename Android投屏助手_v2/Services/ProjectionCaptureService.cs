using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Android投屏助手.Services;

/// <summary>从已显示的 scrcpy 窗口读取当前帧；分辨率与投屏窗口一致。</summary>
internal static class ProjectionCaptureService
{
    public static Task<bool> TryCaptureAsync(Process scrcpyProcess, string destinationPath) =>
        Task.Run(() =>
        {
            try { return TryCapture(scrcpyProcess, destinationPath); }
            catch { return false; } // SDL/显卡不支持窗口读取时，调用方会改用手机原画截图。
        });

    private static bool TryCapture(Process scrcpyProcess, string destinationPath)
    {
        IntPtr window;
        try
        {
            if (scrcpyProcess.HasExited) return false;
            window = scrcpyProcess.MainWindowHandle;
        }
        catch (InvalidOperationException) { return false; }
        if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window)
            || !GetClientRect(window, out var rect)) return false;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width is < 32 or > 16384 || height is < 32 or > 16384) return false;

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return false;
        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero) return false;
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (bitmap == IntPtr.Zero) return false;
            oldBitmap = SelectObject(memoryDc, bitmap);
            if (oldBitmap == IntPtr.Zero || oldBitmap == new IntPtr(-1)) return false;
            // CLIENTONLY 去掉标题栏；RENDERFULLCONTENT 使被其他窗口遮挡时仍能抓到 SDL 画面。
            if (!PrintWindow(window, memoryDc, 0x1 | 0x2)) return false;

            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            var temporaryPath = destinationPath + ".partial";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    encoder.Save(stream);
                if (new FileInfo(temporaryPath).Length < 8) return false;
                File.Move(temporaryPath, destinationPath);
                return true;
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero && oldBitmap != new IntPtr(-1)) SelectObject(memoryDc, oldBitmap);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
