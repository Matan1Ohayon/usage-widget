using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Microsoft.Win32;

namespace AIUsage;

/// <summary>User preferences, saved to %APPDATA%\AIUsage\settings.json.</summary>
internal sealed class AppSettings
{
    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AIUsage");
    private static string FilePath => Path.Combine(Directory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public WidgetStyle WidgetStyle { get; set; } = WidgetStyle.Medium;
    public bool LaunchAtLoginConfigured { get; set; }

    /// <summary>Last dragged position (DIPs) of each widget, keyed "medium", "small.claude", "small.codex".</summary>
    public Dictionary<string, double[]> WidgetPositions { get; set; } = [];

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (IOException)
        {
        }
    }

    public Point? Position(string key) =>
        WidgetPositions.TryGetValue(key, out var p) && p.Length == 2 ? new Point(p[0], p[1]) : null;

    public void SetPosition(string key, Point position) => WidgetPositions[key] = [position.X, position.Y];
}

/// <summary>Start with Windows via the per-user Run key (no admin rights needed).</summary>
internal static class LaunchAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AIUsage";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>If the .exe was moved since login launch was turned on, point the entry at the new location.</summary>
    public static void RefreshPath()
    {
        if (IsEnabled) Set(true);
    }
}
