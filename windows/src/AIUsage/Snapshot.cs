using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIUsage;

/// <summary>
/// <c>AIUsage.exe --snapshot DIR [--demo]</c> renders the dashboard, widgets and tray icon to PNG, light and dark,
/// so the Windows look can be compared with the macOS snapshots (the release workflow uploads them).
/// Sends no notifications and saves nothing.
/// </summary>
internal static class Snapshot
{
    public static int Run(string directory, bool demo)
    {
        Directory.CreateDirectory(directory);
        UsageBar.Animate = false;
        var store = new UsageStore(new SilentNotifier(), statePath: null);
        if (demo) Demo(store);
        else store.RefreshAsync().GetAwaiter().GetResult();

        foreach (var provider in Names.Providers)
        {
            var windows = Names.Windows.Select(kind => store.Window(provider, kind) is { } w
                ? $"{kind.Key()}={UsageFormat.Percent(w.Percent)} {UsageFormat.ResetLine(kind, w, store.Now)}"
                : $"{kind.Key()}=none");
            var issue = store.State(provider).Issue is { } text ? $" issue={text}" : "";
            Console.WriteLine($"{provider.DisplayName()}: {string.Join(", ", windows)}{issue}");
        }

        var actions = new AppActions(() => { }, () => { }, () => { }, () => WidgetStyle.Medium, _ => { }, () => true, _ => { });
        foreach (var dark in new[] { true, false })
        {
            var suffix = dark ? "dark" : "light";
            Save(Surface(new DashboardView(store, actions), Theme.Surface, 12), dark, Path.Combine(directory, $"dashboard-{suffix}.png"));
            // Widgets are always dark (as on macOS); the light variant shows them over a light wallpaper.
            Save(Surface(new MediumWidgetView(store), Theme.WidgetSurface, WidgetMetrics.CornerRadius, dark: true), dark,
                 Path.Combine(directory, $"widget-medium-{suffix}.png"));
            var smalls = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var provider in Names.Providers)
            {
                var widget = Surface(new SmallWidgetView(store, provider), Theme.WidgetSurface, WidgetMetrics.CornerRadius, dark: true);
                widget.Margin = new Thickness(0, 0, 16, 0);
                smalls.Children.Add(widget);
            }
            Save(smalls, dark, Path.Combine(directory, $"widget-small-{suffix}.png"));
            SaveTrayIcon(store, dark, Path.Combine(directory, $"tray-{suffix}.png"));
        }
        Console.WriteLine($"Snapshots written to {Path.GetFullPath(directory)}");
        return 0;
    }

    private sealed class SilentNotifier : INotifier
    {
        public void Post(string id, string title, string body) { }
    }

    /// <summary>The mockup's figures: one bar in each colour band, plus a fetch error.</summary>
    private static void Demo(UsageStore store)
    {
        var now = DateTimeOffset.Now;
        store.SetDemo(Provider.Claude, new ProviderState(new ProviderUsage(
            new UsageWindow(42, now.AddMinutes(186)), new UsageWindow(68, now.AddHours(46)), "Max", now)));
        store.SetDemo(Provider.Codex, new ProviderState(new ProviderUsage(
            new UsageWindow(78, now.AddMinutes(101)), new UsageWindow(11, now.AddHours(141)), "Plus", now),
            "Offline. Showing last known usage"));
    }

    private static Border Surface(UIElement content, string surfaceKey, double radius, bool? dark = null)
    {
        var surface = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), Child = content };
        surface.SetResourceReference(Border.BackgroundProperty, surfaceKey);
        surface.SetResourceReference(Border.BorderBrushProperty, Theme.Edge);
        if (dark is { } forced) Theme.Apply(surface.Resources, forced);
        return surface;
    }

    private static void Save(FrameworkElement content, bool dark, string path)
    {
        var root = new Border
        {
            Padding = new Thickness(24),
            Background = new SolidColorBrush(dark ? Color.FromRgb(0x17, 0x1A, 0x24) : Color.FromRgb(0xCC, 0xD6, 0xE6)),
            Child = content,
        };
        Theme.Apply(root.Resources, dark);
        TextElement.SetFontFamily(root, Theme.Font);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);

        // Two passes: bars size their fills once their own width is known.
        for (var pass = 0; pass < 2; pass++)
        {
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();
        }

        const double scale = 2;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale), (int)Math.Ceiling(root.ActualHeight * scale),
                                            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>The tray icon at 8× on a taskbar-coloured strip.</summary>
    private static void SaveTrayIcon(UsageStore store, bool dark, string path)
    {
        const int iconSize = 16, scale = 8;
        using var icon = TrayIcon.Draw(iconSize * scale, store.Window(Provider.Claude, WindowKind.FiveHour)?.Percent,
                                       store.Window(Provider.Codex, WindowKind.FiveHour)?.Percent, dark);
        using var strip = new Drawing.Bitmap(iconSize * scale * 2, 24 * scale);
        using (var graphics = Drawing.Graphics.FromImage(strip))
        {
            graphics.Clear(dark ? Drawing.Color.FromArgb(0x20, 0x20, 0x20) : Drawing.Color.FromArgb(0xEE, 0xEE, 0xEE));
            graphics.DrawImage(icon, iconSize * scale / 2, 4 * scale);
        }
        strip.Save(path, Drawing.Imaging.ImageFormat.Png);
    }
}
