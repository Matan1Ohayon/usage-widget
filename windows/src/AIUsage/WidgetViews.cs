using System.Windows;
using System.Windows.Controls;

namespace AIUsage;

internal static class WidgetMetrics
{
    public static readonly Size Small = new(170, 170);
    public static readonly Size Medium = new(360, 170);
    public const double CornerRadius = 22;
}

/// <summary>Provider name with its badge, plus a warning glyph when the last fetch failed.</summary>
internal sealed class WidgetHeader : StackPanel
{
    private readonly TextBlock _warning = Ui.Text("", 10, Theme.Secondary);

    public WidgetHeader(Provider provider)
    {
        Orientation = Orientation.Horizontal;
        var name = Ui.Text(provider.DisplayName(), 12, Theme.Primary, FontWeights.SemiBold);
        name.Margin = new Thickness(6, 0, 6, 0);
        _warning.FontFamily = Theme.IconFont;
        _warning.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(ProviderBadge.Create(provider));
        Children.Add(name);
        Children.Add(_warning);
    }

    public void SetIssue(string? issue)
    {
        _warning.Visibility = issue is null ? Visibility.Collapsed : Visibility.Visible;
        _warning.ToolTip = issue;
    }
}

/// <summary>Medium desktop widget: Claude and Codex side by side. Mirrors MediumWidgetView in WidgetViews.swift.</summary>
internal sealed class MediumWidgetView : Grid
{
    private sealed record LineParts(UsageBar Bar, TextBlock Value, TextBlock Reset);

    private readonly UsageStore _store;
    private readonly Dictionary<Provider, WidgetHeader> _headers = [];
    private readonly Dictionary<(Provider, WindowKind), LineParts> _lines = [];

    public MediumWidgetView(UsageStore store)
    {
        _store = store;
        Width = WidgetMetrics.Medium.Width;
        Height = WidgetMetrics.Medium.Height;
        Margin = new Thickness(0);
        var columns = new Grid { Margin = new Thickness(16, 14, 16, 14) };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());

        for (var i = 0; i < Names.Providers.Length; i++)
        {
            var provider = Names.Providers[i];
            var column = new Grid();
            foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                column.RowDefinitions.Add(new RowDefinition { Height = height });

            var header = new WidgetHeader(provider);
            column.Children.Add(header);
            _headers[provider] = header;

            var row = 2;
            foreach (var kind in Names.Windows)
            {
                var line = Line(provider, kind);
                Grid.SetRow(line, row);
                column.Children.Add(line);
                row += 2;
            }
            Grid.SetColumn(column, i * 2);
            columns.Children.Add(column);
        }
        Children.Add(columns);
        store.Changed += Update;
        Update();
    }

    private UIElement Line(Provider provider, WindowKind kind)
    {
        var top = new DockPanel();
        var label = Ui.Text(kind.RowLabel(), 11, Theme.Secondary);
        var value = Ui.Text("—", 11, Theme.Secondary, FontWeights.SemiBold);
        DockPanel.SetDock(value, Dock.Right);
        top.Children.Add(value);
        top.Children.Add(label);

        var bar = new UsageBar { Margin = new Thickness(0, 3, 0, 0) };
        var reset = Ui.Text(" ", 10.5, Theme.Tertiary);
        reset.Margin = new Thickness(0, 3, 0, 0);
        _lines[(provider, kind)] = new LineParts(bar, value, reset);

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(bar);
        stack.Children.Add(reset);
        return stack;
    }

    private void Update()
    {
        foreach (var provider in Names.Providers)
        {
            _headers[provider].SetIssue(_store.State(provider).Issue);
            foreach (var kind in Names.Windows)
            {
                var parts = _lines[(provider, kind)];
                var window = _store.Window(provider, kind);
                parts.Bar.SetPercent(window?.Percent);
                parts.Value.Text = window is null ? "—" : UsageFormat.Percent(window.Percent);
                parts.Value.SetBrush(window is null ? Theme.Secondary : Theme.TextKey(Levels.From(window.Percent)));
                parts.Reset.Text = window switch
                {
                    null => " ",
                    { ResetsAt: null } => "Not started",
                    { ResetsAt: { } at } => kind == WindowKind.FiveHour ? $"Resets {UsageFormat.Time(at)}" : $"Resets {UsageFormat.DayAndTime(at)}",
                };
            }
        }
    }
}

/// <summary>Small desktop widget: the 5-hour figure big, weekly underneath. Mirrors SmallWidgetView in WidgetViews.swift.</summary>
internal sealed class SmallWidgetView : Grid
{
    private readonly UsageStore _store;
    private readonly Provider _provider;
    private readonly WidgetHeader _header;
    private readonly TextBlock _big = Ui.Text("—", 30, Theme.Secondary, FontWeights.Bold);
    private readonly TextBlock _caption = Ui.Text("5h", 10.5, Theme.Tertiary);
    private readonly UsageBar _fiveBar = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _weeklyLabel = Ui.Text("Weekly", 10.5, Theme.Secondary);
    private readonly TextBlock _weeklyValue = Ui.Text("—", 10.5, Theme.Secondary, FontWeights.SemiBold);
    private readonly UsageBar _weeklyBar = new() { Margin = new Thickness(0, 3, 0, 0) };

    public SmallWidgetView(UsageStore store, Provider provider)
    {
        _store = store;
        _provider = provider;
        _header = new WidgetHeader(provider);
        Width = WidgetMetrics.Small.Width;
        Height = WidgetMetrics.Small.Height;

        var layout = new Grid { Margin = new Thickness(16, 14, 16, 14) };
        foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            layout.RowDefinitions.Add(new RowDefinition { Height = height });

        layout.Children.Add(_header);

        _big.LineHeight = 30;
        _big.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _caption.Margin = new Thickness(0, 2, 0, 0);
        var five = new StackPanel();
        five.Children.Add(_big);
        five.Children.Add(_caption);
        five.Children.Add(_fiveBar);
        Grid.SetRow(five, 2);
        layout.Children.Add(five);

        var weeklyTop = new DockPanel();
        DockPanel.SetDock(_weeklyValue, Dock.Right);
        _weeklyValue.Margin = new Thickness(4, 0, 0, 0);
        weeklyTop.Children.Add(_weeklyValue);
        weeklyTop.Children.Add(_weeklyLabel);
        var weekly = new StackPanel();
        weekly.Children.Add(weeklyTop);
        weekly.Children.Add(_weeklyBar);
        Grid.SetRow(weekly, 4);
        layout.Children.Add(weekly);

        Children.Add(layout);
        store.Changed += Update;
        Update();
    }

    private void Update()
    {
        _header.SetIssue(_store.State(_provider).Issue);
        var five = _store.Window(_provider, WindowKind.FiveHour);
        _big.Text = five is null ? "—" : UsageFormat.Percent(five.Percent);
        _big.SetBrush(five is null ? Theme.Secondary : Theme.TextKey(Levels.From(five.Percent)));
        _caption.Text = five switch
        {
            null => "5h",
            { ResetsAt: null } => "5h · not started",
            { ResetsAt: { } at } => $"5h · resets {UsageFormat.Time(at)}",
        };
        _fiveBar.SetPercent(five?.Percent);

        var weekly = _store.Window(_provider, WindowKind.Weekly);
        _weeklyLabel.Text = weekly?.ResetsAt is { } reset ? $"Weekly · {UsageFormat.WeekdayAndTime(reset)}" : "Weekly";
        _weeklyValue.Text = weekly is null ? "—" : UsageFormat.Percent(weekly.Percent);
        _weeklyValue.SetBrush(weekly is null ? Theme.Secondary : Theme.TextKey(Levels.From(weekly.Percent)));
        _weeklyBar.SetPercent(weekly?.Percent);
    }
}
