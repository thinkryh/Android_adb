using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Android投屏助手;

internal static class GlassWindowEffects
{
    private const int CornerPreference = 33;
    private const int Round = 2;

    public static void EnableRoundedCorners(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var value = Round;
                DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, CornerPreference, ref value, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
