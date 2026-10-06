using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AIUsage;

/// <summary>Tray app lifecycle: tray icon, dashboard, desktop widgets, polling and notifications. Mirrors AppDelegate.swift.</summary>
internal sealed class App : Application
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly List<WidgetWindow> _widgets = [];
    private TrayIcon _tray = null!;
    private UsageStore _store = null!;
    private DashboardWindow _dashboard = null!;
    private AppActions _actions = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Theme.Apply(Resources, Theme.AppsUseDark);

        _tray = new TrayIcon();
        _store = new UsageStore(_tray, Path.Combine(AppSettings.Directory, "state.json"));
        _actions = new AppActions(
            Refresh: () => _ = _store.RefreshAsync(),
            Quit: Quit,
            TestNotification: SendTestNotification,
            GetWidgetStyle: () => _settings.WidgetStyle,
            SetWidgetStyle: SetWidgetStyle,
            GetLaunchAtLogin: () => LaunchAtLogin.IsEnabled,
            SetLaunchAtLogin: LaunchAtLogin.Set);

        _dashboard = new DashboardWindow(new DashboardView(_store, _actions));
        _tray.Clicked += cursor =>
        {
            if (!_dashboard.IsShown && _store.IsStale) _actions.Refresh();
            _dashboard.Toggle(cursor);
        };
        _store.Changed += UpdateTray;
        UpdateTray();
        ApplyWidgets();

        // First run: start with Windows by default, like the macOS app.
        if (!_settings.LaunchAtLoginConfigured)
        {
            LaunchAtLogin.Set(true);
            _settings.LaunchAtLoginConfigured = true;
            _settings.Save();
        }
        LaunchAtLogin.RefreshPath();

        new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => _store.Tick(), Dispatcher).Start();
        _ = PollAsync();

        SystemEvents.PowerModeChanged += (_, args) =>
        {
            if (args.Mode == PowerModes.Resume)
                Dispatcher.BeginInvoke(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)); // let the network come back first
                    await _store.RefreshAsync();
                });
        };
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            Theme.Apply(Resources, Theme.AppsUseDark);
            UpdateTray();
        });
    }

    /// <summary>Refresh every 5–7 minutes.</summary>
    private async Task PollAsync()
    {
        while (true)
        {
            await _store.RefreshAsync();
            await Task.Delay(UsageStore.NextPollDelay());
        }
    }

    /// <summary>Redraw the tray bars and tooltip from the 5-hour figures.</summary>
    private void UpdateTray()
    {
        var claude = _store.Window(Provider.Claude, WindowKind.FiveHour);
        var codex = _store.Window(Provider.Codex, WindowKind.FiveHour);
        string Label(UsageWindow? w) => w is null ? "—" : UsageFormat.Percent(w.Percent);
        _tray.Update(claude?.Percent, codex?.Percent, $"Claude 5h {Label(claude)} · Codex 5h {Label(codex)}");
    }

    private void SetWidgetStyle(WidgetStyle style)
    {
        _settings.WidgetStyle = style;
        _settings.Save();
        ApplyWidgets();
    }

    private void ApplyWidgets()
    {
        foreach (var widget in _widgets) widget.Close();
        _widgets.Clear();

        switch (_settings.WidgetStyle)
        {
            case WidgetStyle.Medium:
                AddWidget(new MediumWidgetView(_store), "medium", WidgetMetrics.Medium, 0, 1);
                break;
            case WidgetStyle.Small:
                for (var i = 0; i < Names.Providers.Length; i++)
                {
                    var provider = Names.Providers[i];
                    AddWidget(new SmallWidgetView(_store, provider), $"small.{provider.Key()}", WidgetMetrics.Small, i, Names.Providers.Length);
                }
                break;
        }
    }

    private void AddWidget(FrameworkElement view, string key, Size size, int index, int count)
    {
        var position = _settings.Position(key) is { } saved && WidgetWindow.IsOnScreen(saved, size)
            ? saved
            : WidgetWindow.DefaultPosition(size, index, count);
        var window = new WidgetWindow(view, position, p =>
        {
            _settings.SetPosition(key, p);
            _settings.Save();
        }, _actions, () => SetWidgetStyle(WidgetStyle.Off));
        window.Show();
        _widgets.Add(window);
    }

    private void SendTestNotification()
    {
        var sample = new UsageWindow(75, DateTimeOffset.Now.AddMinutes(101));
        _tray.Post("test", UsageFormat.NotificationTitle(Provider.Codex, WindowKind.FiveHour),
                   UsageFormat.NotificationBody(WindowKind.FiveHour, sample, DateTimeOffset.Now));
    }

    private void Quit()
    {
        _tray.Dispose();
        Shutdown();
    }
}
