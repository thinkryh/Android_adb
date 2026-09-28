using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Android投屏助手;

internal static class GlassWindowEffects
{
    private const int CornerPreference = 33;
    private const int BorderColor = 34;
    private const int Round = 2;
    private const int NoBorder = unchecked((int)0xFFFFFFFE);

    public static void EnableRoundedCorners(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyWindowFrame(window);
        window.Loaded += (_, _) => ApplyWindowFrame(window);
        window.Activated += (_, _) => ApplyWindowFrame(window);
        window.StateChanged += (_, _) => ApplyWindowFrame(window);
    }

    private static void ApplyWindowFrame(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        try
        {
            var value = Round;
            DwmSetWindowAttribute(handle, CornerPreference, ref value, sizeof(int));
            value = NoBorder;
            DwmSetWindowAttribute(handle, BorderColor, ref value, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
