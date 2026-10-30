using System.Reflection;
using System.Runtime.Versioning;
using Avalonia;

[assembly: SupportedOSPlatform("linux")]

namespace Nashi.Linux;

internal static class Program
{
    private static class Copy
    {
        public const string NoAnswer = "Nashi is running but did not answer.";
    }

    [STAThread]
    public static int Main(string[] args)
    {
        var launch = Launch.Parse(args);
        switch (launch.Mode)
        {
            case LaunchMode.Version:
                var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
                Console.WriteLine(version.Split('+')[0]);
                return 0;
            case LaunchMode.InstallIntegration:
            case LaunchMode.UninstallIntegration:
                foreach (var line in Integration.Apply(launch.Mode == LaunchMode.InstallIntegration)) Console.WriteLine(line);
                return 0;
        }

        using var instance = Instance.Claim(Instance.SocketPath);
        if (instance is null) return Forward(launch);
        App.Launch = launch;
        App.Instance = instance;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// Another Nashi owns the socket: hand it this launch's command.
    private static int Forward(Launch launch)
    {
        if (launch.Message is not { } message) return 0;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (Instance.Send(Instance.SocketPath, message)) return 0;
            Thread.Sleep(100);
        }
        Console.Error.WriteLine(L10n.T(Copy.NoAnswer));
        return 1;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
