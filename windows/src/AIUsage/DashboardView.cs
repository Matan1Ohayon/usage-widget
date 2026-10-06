using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AIUsage;

internal enum WidgetStyle { Medium, Small, Off }

internal static class WidgetStyles
{
    public static readonly WidgetStyle[] All = [WidgetStyle.Medium, WidgetStyle.Small, WidgetStyle.Off];

    public static string Title(this WidgetStyle style) => style switch
    {
        WidgetStyle.Medium => "Medium (Claude + Codex)",
        WidgetStyle.Small => "Small (one per AI)",
        _ => "Off",
    };
}

internal sealed record AppActions(
    Action Refresh,
    Action Quit,
    Action TestNotification,
    Func<WidgetStyle> GetWidgetStyle,
    Action<WidgetStyle> SetWidgetStyle,
    Func<bool> GetLaunchAtLogin,
    Action<bool> SetLaunchAtLogin);

/// <summary>Tray dashboard (design B): one row per bar, reset time underneath. Mirrors DashboardView.swift.</summary>
internal sealed class DashboardView : Grid
{
    private const double LabelWidth = 56, Gap = 8, SubtitleIndent = 4 + LabelWidth + Gap;

    /// Set by the release workflow (-p:Version); the "+commit" suffix the SDK appends is dropped.
    private static readonly string Version =
        (System.Reflection.Assembly.GetEntryAssembly()?
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "dev")
        .Split('+')[0];

    private sealed record RowParts(UsageBar Bar, TextBlock Value, TextBlock Subtitle);

    private readonly UsageStore _store;
    private readonly AppActions _actions;
    private readonly Dictionary<(Provider, WindowKind), RowParts> _rows = [];
    private readonly Dictionary<Provider, (StackPanel Panel, TextBlock Text)> _issues = [];
    private readonly TextBlock _updated = Ui.Text("", 11, Theme.Tertiary);
    private readonly IconButton _refresh = new("", "Refresh now");
    private readonly RotateTransform _spin = new();
    private readonly Border _menu = new();

    public DashboardView(UsageStore store, AppActions actions)
    {
        _store = store;
        _actions = actions;

        var content = new StackPanel { Width = 300, Margin = new Thickness(10) };
        foreach (var provider in Names.Providers)
        {
            content.Children.Add(Section(provider));
            content.Children.Add(Ui.Divider());
        }
        content.Children.Add(Footer());
        Children.Add(content);

        _menu.Visibility = Visibility.Collapsed;
        Children.Add(_menu);
        // A click anywhere outside the open menu closes it, like a macOS menu.
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_menu.IsVisible && !_menu.IsMouseOver) _menu.Visibility = Visibility.Collapsed;
        };

        store.Changed += Update;
        Update();
    }

    public void CloseMenu() => _menu.Visibility = Visibility.Collapsed;

    private UIElement Section(Provider provider)
    {
        var section = new StackPanel();
        var header = Ui.Row(ProviderBadge.Create(provider), Ui.Text(provider.DisplayName(), 12, Theme.Primary, FontWeights.SemiBold));
        ((FrameworkElement)header.Children[1]).Margin = new Thickness(6, 0, 0, 0);
        header.Margin = new Thickness(4, 4, 4, 2);
        section.Children.Add(header);

        foreach (var kind in Names.Windows)
        {
            var row = new Grid { Margin = new Thickness(4, 5, 4, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            var label = Ui.Text(kind.RowLabel(), 12, Theme.Secondary);
            label.VerticalAlignment = VerticalAlignment.Center;
            var bar = new UsageBar { VerticalAlignment = VerticalAlignment.Center };
            var value = Ui.Text("—", 13, Theme.Secondary, FontWeights.SemiBold);
            value.TextAlignment = TextAlignment.Right;
            value.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(bar, 2);
            Grid.SetColumn(value, 4);
            row.Children.Add(label);
            row.Children.Add(bar);
            row.Children.Add(value);

            var subtitle = Ui.Text("", 11, Theme.Tertiary);
            subtitle.Margin = new Thickness(SubtitleIndent, 0, 0, 6);
            section.Children.Add(row);
            section.Children.Add(subtitle);
            _rows[(provider, kind)] = new RowParts(bar, value, subtitle);
        }

        var icon = Ui.Text("", 11, Theme.Secondary);
        icon.FontFamily = Theme.IconFont;
        icon.Margin = new Thickness(0, 1, 5, 0);
        var text = Ui.Text("", 11, Theme.Secondary);
        var issue = Ui.Row(icon, text);
        issue.Margin = new Thickness(SubtitleIndent, 0, 0, 4);
        section.Children.Add(issue);
        _issues[provider] = (issue, text);
        return section;
    }

    private UIElement Footer()
    {
        var footer = new DockPanel { Margin = new Thickness(4, 2, 4, 0), LastChildFill = false };
        _updated.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_updated, Dock.Left);
        footer.Children.Add(_updated);

        var more = new IconButton("", "More");
        more.Pressed += ToggleMenu;
        DockPanel.SetDock(more, Dock.Right);
        footer.Children.Add(more);

        _refresh.Margin = new Thickness(0, 0, 6, 0);
        _refresh.Glyph.RenderTransformOrigin = new Point(0.5, 0.5);
        _refresh.Glyph.RenderTransform = _spin;
        _refresh.Pressed += _actions.Refresh;
        DockPanel.SetDock(_refresh, Dock.Right);
        footer.Children.Add(_refresh);
        return footer;
    }

    private void Update()
    {
        foreach (var provider in Names.Providers)
        {
            var state = _store.State(provider);
            foreach (var kind in Names.Windows)
            {
                var parts = _rows[(provider, kind)];
                var window = _store.Window(provider, kind);
                parts.Bar.SetPercent(window?.Percent);
                parts.Value.Text = window is null ? "—" : UsageFormat.Percent(window.Percent);
                parts.Value.SetBrush(window is null ? Theme.Secondary : Theme.TextKey(Levels.From(window.Percent)));
                parts.Subtitle.Text = window is null
                    ? state.Usage is null ? "Loading…" : "No limit reported"
                    : UsageFormat.ResetLine(kind, window, _store.Now);
            }
            var (panel, text) = _issues[provider];
            text.Text = state.Issue ?? "";
            panel.Visibility = state.Issue is null ? Visibility.Collapsed : Visibility.Visible;
        }

        _updated.Text = _store.LastUpdated is { } updated ? $"Updated {UsageFormat.Time(updated)}" : "Not updated yet";
        if (_store.IsRefreshing && UsageBar.Animate)
            _spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        else
            _spin.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    // The ⋯ menu, drawn inside the dashboard in macOS menu style (rounded, accent-highlighted rows).

    private void ToggleMenu()
    {
        if (_menu.IsVisible)
        {
            _menu.Visibility = Visibility.Collapsed;
            return;
        }
        BuildMenu();
        _menu.Visibility = Visibility.Visible;
    }

    private void BuildMenu()
    {
        var items = new StackPanel { Margin = new Thickness(5) };
        items.Children.Add(MenuHeader($"AI Usage {Version}"));
        items.Children.Add(MenuSeparator());
        items.Children.Add(MenuHeader("Desktop Widget"));
        var current = _actions.GetWidgetStyle();
        foreach (var style in WidgetStyles.All)
            items.Children.Add(MenuItem(style.Title(), style == current, () => _actions.SetWidgetStyle(style)));
        items.Children.Add(MenuSeparator());
        var launch = _actions.GetLaunchAtLogin();
        items.Children.Add(MenuItem("Launch at Login", launch, () => _actions.SetLaunchAtLogin(!launch)));
        items.Children.Add(MenuItem("Send Test Notification", false, _actions.TestNotification));
        items.Children.Add(MenuSeparator());
        items.Children.Add(MenuItem("Quit AI Usage", false, _actions.Quit));

        _menu.Child = items;
        _menu.CornerRadius = new CornerRadius(8);
        _menu.BorderThickness = new Thickness(0.5);
        _menu.SetResourceReference(Border.BackgroundProperty, Theme.MenuSurface);
        _menu.SetResourceReference(Border.BorderBrushProperty, Theme.Edge);
        _menu.HorizontalAlignment = HorizontalAlignment.Right;
        _menu.VerticalAlignment = VerticalAlignment.Bottom;
        _menu.Margin = new Thickness(0, 0, 12, 34);
        _menu.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.3 };
    }

    private static UIElement MenuHeader(string title)
    {
        var header = Ui.Text(title, 11, Theme.Tertiary, FontWeights.SemiBold);
        header.Margin = new Thickness(8, 2, 8, 2);
        return header;
    }

    private static UIElement MenuSeparator() => Ui.Brushed(new Border { Height = 1, Margin = new Thickness(8, 4, 8, 4) }, Border.BackgroundProperty, Theme.Divider);

    private UIElement MenuItem(string title, bool isChecked, Action action)
    {
        var check = Ui.Text(isChecked ? "✓" : "", 12, Theme.Primary);
        check.Width = 18;
        var label = Ui.Text(title, 13, Theme.Primary);
        var item = new Border
        {
            Padding = new Thickness(6, 3, 12, 3),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Child = Ui.Row(check, label),
        };
        item.MouseEnter += (_, _) =>
        {
            item.SetResourceReference(Border.BackgroundProperty, Theme.Accent);
            check.SetBrush(Theme.AccentText);
            label.SetBrush(Theme.AccentText);
        };
        item.MouseLeave += (_, _) =>
        {
            item.Background = Brushes.Transparent;
            check.SetBrush(Theme.Primary);
            label.SetBrush(Theme.Primary);
        };
        item.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _menu.Visibility = Visibility.Collapsed;
            action();
        };
        return item;
    }
}
