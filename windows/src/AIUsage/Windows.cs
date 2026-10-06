using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace AIUsage;

/// <summary>Borderless transparent window hosting one rounded surface with a soft shadow.</summary>
internal abstract class SurfaceWindow : Window
{
    /// <summary>Transparent room around the surface for its shadow (click-through).</summary>
    protected const double ShadowMargin = 16;

    protected SurfaceWindow(UIElement content, string surfaceKey, double cornerRadius)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = Theme.Font;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var surface = new Border
        {
            CornerRadius = new CornerRadius(cornerRadius),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(ShadowMargin),
            Child = content,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.32 },
        };
        surface.SetResourceReference(Border.BackgroundProperty, surfaceKey);
        surface.SetResourceReference(Border.BorderBrushProperty, Theme.Edge);
        Content = surface;
    }

    protected IntPtr Handle => new WindowInteropHelper(this).Handle;
}

/// <summary>The dashboard that opens from the tray icon, anchored above (or beside) the taskbar.</summary>
internal sealed class DashboardWindow : SurfaceWindow
{
    private readonly DashboardView _view;
    private DateTime _hiddenAt;

    public DashboardWindow(DashboardView view) : base(view, Theme.Surface, 12)
    {
        _view = view;
        Topmost = true;
        Opacity = 0;
        Deactivated += (_, _) => Dismiss();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Dismiss();
        };
        // Keep the panel pinned to its anchor when its height changes (an error line appears, etc.).
        SizeChanged += (_, _) =>
        {
            if (IsShown && _anchor is { } anchor) Position(anchor);
        };
    }

    public bool IsShown => IsVisible && Opacity > 0;

    private Drawing.Point? _anchor;

    public void Toggle(Drawing.Point cursor)
    {
        // The click that deactivated (and hid) the panel also reaches the tray icon: don't reopen on it.
        if (IsShown || (DateTime.Now - _hiddenAt).TotalMilliseconds < 400)
        {
            Dismiss();
            return;
        }
        ShowNear(cursor);
    }

    private void ShowNear(Drawing.Point cursor)
    {
        _anchor = cursor;
        _view.CloseMenu();
        Opacity = 0;
        Show();
        UpdateLayout();
        Position(cursor);
        Activate();
        Native.SetForegroundWindow(Handle);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.12)));
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.Now;
        var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.1));
        fade.Completed += (_, _) =>
        {
            if (Opacity == 0) Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Places the panel 12 px from the taskbar edge nearest the click, centred on the click, kept on screen.</summary>
    private void Position(Drawing.Point cursor)
    {
        var screen = Forms.Screen.FromPoint(cursor);
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        var scale = Native.ScaleForPoint(cursor);
        double width = ActualWidth * scale, height = ActualHeight * scale;
        double inset = ShadowMargin * scale, gap = 12 * scale;

        double x, y;
        if (work.Top > bounds.Top) // taskbar at the top
        {
            x = cursor.X - width / 2;
            y = work.Top + gap - inset;
        }
        else if (work.Left > bounds.Left) // taskbar on the left
        {
            x = work.Left + gap - inset;
            y = cursor.Y - height / 2;
        }
        else if (work.Right < bounds.Right) // taskbar on the right
        {
            x = work.Right - gap + inset - width;
            y = cursor.Y - height / 2;
        }
        else // bottom (default, also auto-hide)
        {
            x = cursor.X - width / 2;
            y = work.Bottom - gap + inset - height;
        }
        x = Math.Clamp(x, work.Left - inset, work.Right + inset - width);
        y = Math.Clamp(y, work.Top - inset, work.Bottom + inset - height);
        Native.MoveWindow(Handle, (int)Math.Round(x), (int)Math.Round(y));
    }
}

/// <summary>
/// Desktop widget: sits above the wallpaper and below every normal window, never takes focus,
/// stays out of the taskbar and Alt+Tab, and can be dragged anywhere (position is remembered).
/// </summary>
internal sealed class WidgetWindow : SurfaceWindow
{
    private const int WmWindowPosChanging = 0x0046;

    public WidgetWindow(FrameworkElement content, Point position, Action<Point> savePosition, AppActions actions, Action hide)
        : base(content, Theme.WidgetSurface, WidgetMetrics.CornerRadius)
    {
        // Widgets sit on photos and wallpapers: light text on dark glass stays legible on any of them.
        Theme.Apply(Resources, dark: true);
        ShowActivated = false;
        Left = position.X - ShadowMargin;
        Top = position.Y - ShadowMargin;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        LocationChanged += (_, _) => savePosition(new Point(Left + ShadowMargin, Top + ShadowMargin));

        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Refresh Now", actions.Refresh));
        menu.Items.Add(MenuItem("Hide Desktop Widget", hide));
        ContextMenu = menu;

        SourceInitialized += (_, _) =>
        {
            Native.MakeToolWindow(Handle);
            HwndSource.FromHwnd(Handle)?.AddHook(KeepAtBottom);
        };
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Every time Windows reorders this window, send it to the bottom of the z-order.</summary>
    private static IntPtr KeepAtBottom(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmWindowPosChanging)
        {
            var pos = Marshal.PtrToStructure<Native.WindowPos>(lParam);
            pos.hwndInsertAfter = Native.HwndBottom;
            pos.flags &= ~Native.SwpNoZOrder;
            Marshal.StructureToPtr(pos, lParam, fDeleteOld: false);
        }
        return IntPtr.Zero;
    }

    /// <summary>Default spot: top-right of the primary screen's work area (DIPs), like the macOS widget.</summary>
    public static Point DefaultPosition(Size size, int index, int count)
    {
        var area = SystemParameters.WorkArea;
        const double margin = 24, spacing = 16;
        return new Point(area.Right - margin - (count - index) * (size.Width + spacing) + spacing, area.Top + margin);
    }

    public static bool IsOnScreen(Point position, Size size) =>
        position.X >= SystemParameters.VirtualScreenLeft - size.Width / 2 &&
        position.Y >= SystemParameters.VirtualScreenTop &&
        position.X + size.Width / 2 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
        position.Y + size.Height / 2 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
}

internal static class Native
{
    public static readonly IntPtr HwndBottom = new(1);
    public const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoSize = 0x0001, SwpNoActivate = 0x0010;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000, WsExAppWindow = 0x00040000;

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowPos
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Drawing.Point point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);

    public static void MoveWindow(IntPtr hwnd, int x, int y) => SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);

    /// <summary>Hidden from the taskbar and Alt+Tab, and clicking it doesn't steal focus.</summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        style = (style | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
    }

    /// <summary>Pixels per DIP on the monitor under a point (1.0 at 100%, 1.5 at 150%…).</summary>
    public static double ScaleForPoint(Drawing.Point point)
    {
        try
        {
            var monitor = MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dpi, out _) == 0) return dpi / 96.0;
        }
        catch (DllNotFoundException)
        {
        }
        return 1;
    }
}
