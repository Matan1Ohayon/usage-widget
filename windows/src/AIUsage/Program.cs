using System.Threading;

namespace AIUsage;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // AIUsage.exe --snapshot DIR [--demo]: render every surface to PNG and exit (no tray, no notifications).
        var snapshot = Array.IndexOf(args, "--snapshot");
        if (snapshot >= 0 && snapshot + 1 < args.Length)
            return Snapshot.Run(args[snapshot + 1], demo: args.Contains("--demo"));

        // One copy only: a second launch (Start menu, login, double-click) just exits.
        using var instance = new Mutex(initiallyOwned: true, @"Local\AIUsage.SingleInstance", out var isFirst);
        if (!isFirst) return 0;

        return new App().Run();
    }
}
