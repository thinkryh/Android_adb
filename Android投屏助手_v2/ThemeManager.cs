using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Android投屏助手;

internal static class ThemeManager
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Android投屏助手", "theme.txt");

    public static bool IsDark { get; private set; } = true;
    public static event Action? Changed;

    public static void Initialize()
    {
        try { IsDark = !File.ReadAllText(PreferencePath).Trim().Equals("light", StringComparison.OrdinalIgnoreCase); }
        catch (IOException) { IsDark = true; }
        catch (UnauthorizedAccessException) { IsDark = true; }
        Apply();
    }

    public static void SetDark(bool dark)
    {
        if (IsDark == dark) return;
        IsDark = dark;
        Apply();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, dark ? "dark" : "light");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    public static Color ToolbarNeutralBorder => Parse(IsDark ? "#34465A" : "#E0EDFB");
    public static Color ToolbarDockedBorder => Parse(IsDark ? "#40556B" : "#85B8F7");
    public static Color ToolbarSnapBorder => Parse(IsDark ? "#7196BF" : "#307EEF");

    private static void Apply()
    {
        if (IsDark)
        {
            Gradient("WindowBrush", "#101723", "#192536", "#101A2B");
            Gradient("PanelBrush", "#E12B394C", "#D11C2A3E");
            Gradient("PanelStrongBrush", "#F12D3B50", "#E3223044");
            Gradient("ToolbarGlassBrush", "#F42A394C", "#F027364A", "#ED202C3D");
            Solid("TextBrush", "#F4F7FD");
            Solid("MutedTextBrush", "#A9B8CC");
            Solid("AccentBrush", "#83B4FF");
            Solid("ActionBrush", "#326CC5");
            Solid("AccentLightBrush", "#365678");
            Solid("DangerBrush", "#FF7087");
            Solid("BorderBrush", "#59738F");
            Solid("ButtonBorderBrush", "#54708C");
            Solid("ButtonHoverBrush", "#328EC0FF");
            Solid("ToolbarButtonBrush", "#A1374A60");
            Solid("ToolbarButtonBorderBrush", "#455B72");
            Solid("ToolbarBorderBrush", "#34465A");
            Solid("ToolbarIconBrush", "#E5F1FF");
            Solid("RecordBrush", "#A8757F");
            Solid("RecordActiveBrush", "#D16D7D");
            Solid("TextBoxBrush", "#EC19283A");
            Solid("TextBoxBorderBrush", "#7189A3");
            Solid("StopButtonBrush", "#39465A");
            Solid("HeaderIconBrush", "#183329");
            Solid("DialogBadgeBrush", "#35557B");
            Solid("DialogBadgeBorderBrush", "#698CB2");
        }
        else
        {
            Gradient("WindowBrush", "#F8FBFF", "#E7F1FB", "#DCECF7");
            Gradient("PanelBrush", "#EFFFFFFF", "#C8EAF5FF");
            Gradient("PanelStrongBrush", "#F8FFFFFF", "#DDE9F4FF");
            Gradient("ToolbarGlassBrush", "#F8FFFFFF", "#E7F6FCFF", "#D1DFEDFF");
            Solid("TextBrush", "#1D2938");
            Solid("MutedTextBrush", "#68798B");
            Solid("AccentBrush", "#2770E8");
            Solid("ActionBrush", "#2770E8");
            Solid("AccentLightBrush", "#DCEBFF");
            Solid("DangerBrush", "#DC4D64");
            Solid("BorderBrush", "#EFFFFFFF");
            Solid("ButtonBorderBrush", "#B8FFFFFF");
            Solid("ButtonHoverBrush", "#45FFFFFF");
            Solid("ToolbarButtonBrush", "#84FFFFFF");
            Solid("ToolbarButtonBorderBrush", "#D2FFFFFF");
            Solid("ToolbarBorderBrush", "#E0EDFB");
            Solid("ToolbarIconBrush", "#2A4C74");
            Solid("RecordBrush", "#B56F79");
            Solid("RecordActiveBrush", "#D25268");
            Solid("TextBoxBrush", "#ECFFFFFF");
            Solid("TextBoxBorderBrush", "#A9C8DEEF");
            Solid("StopButtonBrush", "#FFF0F1F3");
            Solid("HeaderIconBrush", "#0B1410");
            Solid("DialogBadgeBrush", "#E5F0FF");
            Solid("DialogBadgeBorderBrush", "#FFFFFFFF");
        }
    }

    private static void Solid(string key, string value) =>
        Application.Current.Resources[key] = new SolidColorBrush(Parse(value));

    private static void Gradient(string key, params string[] values)
    {
        var old = (LinearGradientBrush)Application.Current.Resources[key];
        var brush = new LinearGradientBrush { StartPoint = old.StartPoint, EndPoint = old.EndPoint };
        for (var i = 0; i < values.Length; i++)
            brush.GradientStops.Add(new GradientStop(Parse(values[i]), old.GradientStops[i].Offset));
        Application.Current.Resources[key] = brush;
    }

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
}
