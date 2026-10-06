using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace AIUsage;

/// <summary>Small builders so the code-only views read like their SwiftUI counterparts.</summary>
internal static class Ui
{
    public static TextBlock Text(string text, double size, string brushKey, FontWeight? weight = null)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    public static void SetBrush(this TextBlock block, string brushKey) => block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);

    public static Border Divider() => Brushed(new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) }, Border.BackgroundProperty, Theme.Divider);

    public static T Brushed<T>(T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }
}

/// <summary>Capsule usage bar with hairline ticks at the 60% and 75% thresholds.</summary>
internal sealed class UsageBar : Grid
{
    /// <summary>Off for offscreen snapshots, which capture a single frame.</summary>
    public static bool Animate { get; set; } = true;

    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Rectangle[] _ticks = [new() { Width = 1 }, new() { Width = 1 }];
    private static readonly double[] TickPositions = [0.60, 0.75];
    private double? _percent;

    public UsageBar(double height = 6)
    {
        Height = height;
        Children.Add(Ui.Brushed(new Border(), Border.BackgroundProperty, Theme.Track));
        Children.Add(_fill);
        foreach (var tick in _ticks)
        {
            tick.HorizontalAlignment = HorizontalAlignment.Left;
            tick.SetResourceReference(Shape.FillProperty, Theme.Tick);
            Children.Add(tick);
        }
    }

    public void SetPercent(double? percent)
    {
        var changed = _percent != percent;
        _percent = percent;
        if (percent is { } value) _fill.SetResourceReference(Border.BackgroundProperty, Theme.FillKey(Levels.From(value)));
        Layout(animate: changed);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Layout(animate: false);
    }

    private void Layout(bool animate)
    {
        var width = ActualWidth;
        var radius = Height / 2;
        Clip = new RectangleGeometry(new Rect(0, 0, width, Height), radius, radius);
        _fill.CornerRadius = new CornerRadius(radius);
        for (var i = 0; i < _ticks.Length; i++) _ticks[i].Margin = new Thickness(width * TickPositions[i], 0, 0, 0);

        var fraction = Math.Clamp((_percent ?? 0) / 100, 0, 1);
        var target = fraction > 0 ? Math.Max(Height, width * fraction) : 0;
        if (animate && Animate && Theme.AnimationsEnabled && width > 0)
        {
            _fill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromSeconds(0.35))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        else
        {
            _fill.BeginAnimation(WidthProperty, null);
            _fill.Width = target;
        }
    }
}

internal static class ProviderBadge
{
    public static Border Create(Provider provider, double size = 16)
    {
        var claude = provider == Provider.Claude;
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.27),
            Background = claude ? new SolidColorBrush(Color.FromRgb(0xD9, 0x78, 0x57)) : Brushes.White,
            BorderBrush = claude ? null : new SolidColorBrush(Color.FromArgb(38, 0, 0, 0)),
            BorderThickness = new Thickness(claude ? 0 : 0.5),
            Child = new TextBlock
            {
                Text = claude ? "✳" : "❯_",
                FontFamily = Theme.SymbolFont,
                FontSize = size * (claude ? 0.6 : 0.5),
                FontWeight = FontWeights.Heavy,
                Foreground = claude ? Brushes.White : Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }
}

/// <summary>Borderless icon button that highlights on hover and responds on press, like macOS toolbar buttons.</summary>
internal sealed class IconButton : Border
{
    public event Action? Pressed;
    public TextBlock Glyph { get; }

    public IconButton(string glyph, string tooltip)
    {
        Glyph = Ui.Text(glyph, 13, Theme.Secondary);
        Glyph.FontFamily = Theme.IconFont;
        Glyph.TextTrimming = TextTrimming.None;
        Child = Glyph;
        Padding = new Thickness(4);
        CornerRadius = new CornerRadius(5);
        Background = Brushes.Transparent;
        ToolTip = tooltip;
        System.Windows.Automation.AutomationProperties.SetName(this, tooltip);
        MouseEnter += (_, _) => SetResourceReference(BackgroundProperty, Theme.Hover);
        MouseLeave += (_, _) => Background = Brushes.Transparent;
        MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Pressed?.Invoke();
        };
    }
}
