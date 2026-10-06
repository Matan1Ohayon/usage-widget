using Drawing2D = System.Drawing.Drawing2D;

namespace AIUsage;

/// <summary>
/// System-tray icon: two stacked mini bars, Claude on top and Codex below, filled to the 5-hour usage
/// (the Windows twin of the macOS menu-bar icon). Also delivers notifications as Windows toasts.
/// </summary>
internal sealed class TrayIcon : INotifier, IDisposable
{
    private readonly Forms.NotifyIcon _icon = new() { Text = "AI Usage", Visible = true };
    private IntPtr _iconHandle;

    /// <summary>Raised on left or right click with the cursor position in screen pixels.</summary>
    public event Action<Drawing.Point>? Clicked;

    public TrayIcon()
    {
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button is Forms.MouseButtons.Left or Forms.MouseButtons.Right) Clicked?.Invoke(Forms.Cursor.Position);
        };
    }

    public void Update(double? claude, double? codex, string tooltip)
    {
        var size = Forms.SystemInformation.SmallIconSize.Width;
        using var bitmap = Draw(size, claude, codex, Theme.TaskbarUsesDark);
        var handle = bitmap.GetHicon();
        var previous = _icon.Icon;
        _icon.Icon = Drawing.Icon.FromHandle(handle);
        previous?.Dispose();
        if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);
        _iconHandle = handle;
        _icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }

    /// <summary>Bars are 16×4 px at 100% scale with a 3 px gap, scaled with the tray icon size.</summary>
    public static Drawing.Bitmap Draw(int size, double? claude, double? codex, bool darkTaskbar)
    {
        var scale = size / 16.0;
        var barHeight = (float)Math.Max(3, Math.Round(4 * scale));
        var gap = (float)Math.Max(2, Math.Round(3 * scale));
        var top = (size - (barHeight * 2 + gap)) / 2f;

        var bitmap = new Drawing.Bitmap(size, size);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Drawing.Color.Transparent);
        DrawBar(graphics, claude, top, size, barHeight, darkTaskbar);
        DrawBar(graphics, codex, top + barHeight + gap, size, barHeight, darkTaskbar);
        return bitmap;
    }

    private static void DrawBar(Drawing.Graphics graphics, double? percent, float y, int width, float height, bool dark)
    {
        var track = dark ? Drawing.Color.FromArgb(90, 255, 255, 255) : Drawing.Color.FromArgb(64, 0, 0, 0);
        using (var brush = new Drawing.SolidBrush(track))
            graphics.FillPath(brush, Capsule(0, y, width, height));

        if (percent is not { } value || value <= 0) return;
        var fill = Math.Max(height, (float)(width * Math.Min(value, 100) / 100));
        var color = Levels.From(value) switch
        {
            UsageLevel.Green => dark ? Drawing.Color.FromArgb(0x30, 0xD1, 0x58) : Drawing.Color.FromArgb(0x34, 0xC7, 0x59),
            UsageLevel.Yellow => dark ? Drawing.Color.FromArgb(0xFF, 0xD6, 0x0A) : Drawing.Color.FromArgb(0xFF, 0xCC, 0x00),
            _ => dark ? Drawing.Color.FromArgb(0xFF, 0x45, 0x3A) : Drawing.Color.FromArgb(0xFF, 0x3B, 0x30),
        };
        using var fillBrush = new Drawing.SolidBrush(color);
        graphics.FillPath(fillBrush, Capsule(0, y, fill, height));
    }

    private static Drawing2D.GraphicsPath Capsule(float x, float y, float width, float height)
    {
        var path = new Drawing2D.GraphicsPath();
        var d = Math.Min(height, width);
        path.AddArc(x, y, d, height, 90, 180);
        path.AddArc(x + width - d, y, d, height, 270, 180);
        path.CloseFigure();
        return path;
    }

    /// <summary>On Windows 10/11 a tray balloon is shown as a native toast notification.</summary>
    public void Post(string id, string title, string body) => _icon.ShowBalloonTip(8000, title, body, Forms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);
    }
}
