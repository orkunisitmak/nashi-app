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
        public const string WatchFailed = "Couldn’t convert in {0}: {1}";
        public const string Wayland = "Drag gestures need an X11 session. The right-click menu and Open With still work.";
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
                StartGestures();
                StartWatching();
                if (Settings.Current.Shelf.Count > 0) FloatShelf.Open([], Flow.ShowFormats); // Float comes back as it was
            }
            Instance?.Start(message => Dispatcher.UIThread.Post(() => Handle(message)));
        }
        base.OnFrameworkInitializationCompleted();
    }

    private PointerWatch? pointerWatch;

    /// The background instance hosts the drag gesture: holding the gesture key while dragging files opens the wheel at
    /// the pointer. On Wayland there is no global pointer: a one-time card says so.
    private void StartGestures()
    {
        if (!PointerWatch.Available)
        {
            if (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland" && !Settings.Current.WaylandNoticeShown)
            {
                Dispatcher.UIThread.Post(() => SuccessToast.Notice(L10n.T(Copy.Wayland)).Present());
                Settings.Update(s => s with { WaylandNoticeShown = true });
            }
            return;
        }
        var detector = new GestureDetector { Key = GestureDetector.Parse(Settings.Current.GestureKey) };
        Settings.Changed += s => detector.Key = GestureDetector.Parse(s.GestureKey);
        pointerWatch = new PointerWatch(detector);
        Flow.Sampler = pointerWatch.Sampler;
        detector.DragStarted += (x, y) => Dispatcher.UIThread.Post(() => Flow.ShowForDrag(new PixelPoint(x, y)));
        detector.DragEnded += (_, _) => Dispatcher.UIThread.Post(Flow.DragEnded);
        pointerWatch.Sampler.Flip += () => Dispatcher.UIThread.Post(Flow.FlipDrag);
        pointerWatch.DragChanged += running => Dispatcher.UIThread.Post(() => FloatShelf.Current?.DragRunning(running));
    }

    private WatchService? watching;

    /// The background instance runs the watch folders from Settings, and follows its changes.
    private void StartWatching()
    {
        var runner = new OperationRunner(tools);
        watching = new WatchService(async (route, files, token) =>
            await runner.RunBesideSourcesAsync(route, files, folder => Task.FromResult<string?>(folder), new Progress<OperationProgress>(), token) ?? []);
        watching.Ran += (folder, inputs, outputs, error) => Dispatcher.UIThread.Post(() =>
        {
            if (error is not null) SuccessToast.Notice(L10n.F(Copy.WatchFailed, Path.GetFileName(folder.Path), L10n.T(error.Message))).Present();
            else if (outputs.Count > 0) SuccessToast.Done(outputs, folder.Path).Present();
        });
        watching.Sync(Settings.Current.WatchFolders, startup: true);
        Settings.Changed += s => Dispatcher.UIThread.Post(() => watching.Sync(s.WatchFolders));
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
            case "actions":
                Flow.StartupTime = Desktop.StartupTime(message.Startup);
                ShowWheel(message.Files ?? [], actions: true);
                break;
            case "quit":
                desktop?.Shutdown();
                break;
        }
    }

    /// The wheel at the pointer for the files that exist (Selection's rules), on its front face (or, for --actions, its
    /// Actions face), or a "Nothing to convert" card.
    public void ShowWheel(IEnumerable<string> paths, bool actions = false)
    {
        var selection = new Selection();
        selection.Add(paths);
        if (selection.Files.Count == 0) SuccessToast.Notice(L10n.T(Copy.Nothing)).Present();
        else if (actions) Flow.ShowActions(selection.Files);
        else Flow.ShowFormats(selection.Files);
    }
}
