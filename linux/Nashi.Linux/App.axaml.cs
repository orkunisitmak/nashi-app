using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Nashi.Core;

namespace Nashi.Linux;

/// Home mode shows the main window and exits with it. Every other launch runs in the background with no window:
/// wheels appear at the pointer and toasts are the only UI, until a `show` brings Home up (closing it hides it).
public sealed class App : Application
{
    private static class Copy
    {
        public const string Nothing = "Nothing to convert";
    }

    /// Set by Program before Avalonia starts; the checks keep Home.
    public static Launch Launch { get; set; } = new(LaunchMode.Home, []);
    /// This process's single-instance socket, when it owns one.
    public static Instance? Instance { get; set; }

    private readonly Toolchain tools = AppPaths.CreateToolchain();
    private WheelFlow? flow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Launch.Mode == LaunchMode.Home) desktop.MainWindow = new MainWindow();
            else
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                if (Launch.Message is { } first) Dispatcher.UIThread.Post(() => Handle(first));
            }
            Instance?.Start(message => Dispatcher.UIThread.Post(() => Handle(message)));
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// The wheels, toasts and setup window of the background mode.
    public WheelFlow Flow => flow ??= new WheelFlow(tools, null, message => SuccessToast.Notice(message).Present(), () =>
    {
        new ToolSetupWindow(tools).Show();
        return Task.CompletedTask;
    });

    /// One command from this launch or from a later `Nashi` invocation.
    public void Handle(InstanceMessage message)
    {
        var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        switch (message.Cmd)
        {
            case "show":
                if (desktop is null) return;
                var home = desktop.MainWindow ??= new MainWindow { HideOnClose = true };
                home.Show();
                if (home.WindowState == WindowState.Minimized) home.WindowState = WindowState.Normal;
                home.Activate();
                break;
            case "wheel":
                Flow.StartupTime = Desktop.StartupTime(message.Startup);
                ShowWheel(message.Files ?? []);
                break;
            case "wheel-clipboard":
                Flow.StartupTime = Desktop.StartupTime(message.Startup);
                _ = ShowClipboardWheelAsync();
                break;
            case "quit":
                desktop?.Shutdown();
                break;
        }
    }

    /// The tools wheel at the pointer for the files that exist (Selection's rules), or a "Nothing to convert" card.
    public void ShowWheel(IEnumerable<string> paths)
    {
        var selection = new Selection();
        selection.Add(paths);
        if (selection.Files.Count == 0) SuccessToast.Notice(L10n.T(Copy.Nothing)).Present();
        else Flow.ShowTools(selection.Files);
    }

    private async Task ShowClipboardWheelAsync()
    {
        var host = new Window(); // never shown: it only lends the platform clipboard
        try { ShowWheel(host.Clipboard is { } clipboard ? await Desktop.ClipboardFilesAsync(clipboard) : []); }
        catch (Exception error) { SuccessToast.Notice(L10n.T(error.Message)).Present(); }
        finally { host.Close(); }
    }
}
