using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AIUsage;

/// <summary>
/// Colour and type tokens from shared/DESIGN.md. The macOS app is the reference look;
/// these values reproduce its system colours and materials on Windows.
/// </summary>
internal static class Theme
{
    public const string Primary = "Primary", Secondary = "Secondary", Tertiary = "Tertiary";
    public const string Track = "Track", Tick = "Tick", Divider = "Divider", Edge = "Edge", Hover = "Hover";
    public const string Surface = "Surface", WidgetSurface = "WidgetSurface", MenuSurface = "MenuSurface";
    public const string Accent = "Accent", AccentText = "AccentText";
    public const string GreenBar = "GreenBar", YellowBar = "YellowBar", RedBar = "RedBar";
    public const string YellowText = "YellowText", RedText = "RedText";

    /// <summary>Segoe UI Variable is Windows' counterpart to SF Pro (which can't be redistributed).</summary>
    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily SymbolFont = new("Segoe UI Symbol");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private static SolidColorBrush Brush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    private static uint White(double alpha) => ((uint)Math.Round(alpha * 255) << 24) | 0xFFFFFF;
    private static uint Black(double alpha) => (uint)Math.Round(alpha * 255) << 24;

    public static void Apply(ResourceDictionary resources, bool dark)
    {
        void Set(string key, uint dark_, uint light) => resources[key] = Brush(dark ? dark_ : light);

        Set(Primary, White(0.85), Black(0.85));
        Set(Secondary, White(0.55), Black(0.50));
        Set(Tertiary, White(0.25), Black(0.26));
        Set(Track, White(0.85 * 0.10), Black(0.85 * 0.10));
        Set(Tick, Black(0.28), Black(0.28));
        Set(Divider, White(0.10), Black(0.10));
        Set(Edge, White(0.14), Black(0.10));
        Set(Hover, White(0.08), Black(0.06));
        Set(Surface, 0xF7323236, 0xF7F2F2F4);
        Set(MenuSurface, 0xFA3A3A3E, 0xFAF8F8FA);
        Set(WidgetSurface, 0xEB232326, 0xEB232326);
        Set(Accent, 0xFF0A84FF, 0xFF007AFF);
        Set(AccentText, 0xFFFFFFFF, 0xFFFFFFFF);
        Set(GreenBar, 0xFF30D158, 0xFF34C759);
        Set(YellowBar, 0xFFFFD60A, 0xFFFFCC00);
        Set(RedBar, 0xFFFF453A, 0xFFFF3B30);
        Set(YellowText, 0xFFFFD60A, 0xFFB38000);
        Set(RedText, 0xFFFF453A, 0xFFFF3B30);
    }

    public static string FillKey(UsageLevel level) => level switch
    {
        UsageLevel.Green => GreenBar,
        UsageLevel.Yellow => YellowBar,
        _ => RedBar,
    };

    /// <summary>Green stays neutral so only warnings draw the eye.</summary>
    public static string TextKey(UsageLevel level) => level switch
    {
        UsageLevel.Green => Primary,
        UsageLevel.Yellow => YellowText,
        _ => RedText,
    };

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static bool ReadDark(string value)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(value) is int light && light == 0;
    }

    /// <summary>"Choose your app mode" in Settings → Personalization → Colors.</summary>
    public static bool AppsUseDark => ReadDark("AppsUseLightTheme");

    /// <summary>"Choose your Windows mode": the taskbar the tray icon sits on.</summary>
    public static bool TaskbarUsesDark => ReadDark("SystemUsesLightTheme");

    /// <summary>Windows' "Animation effects" setting (off = reduce motion).</summary>
    public static bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;
}
