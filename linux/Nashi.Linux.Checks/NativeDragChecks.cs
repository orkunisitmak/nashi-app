using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Nashi.Linux;

/// Real X11 target for scripts/test-linux-native-drop.py. This bypasses the headless platform and its fake XDND
/// callbacks, so native XEvent layout, pointer coordinates and an actual PDF-to-JPG export are exercised together.
static class NativeDragChecks
{
    public static int Run(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (!File.Exists(Path.Combine(directory, "source.pdf"))) throw new ArgumentException("The native drag fixture PDF is missing.");
        AppPaths.DataOverride = Path.Combine(directory, "data");
        L10n.Language = "en";
        WheelFeedback.Player = _ => { };
        WheelFeedback.ReduceMotion = true;
        App.Launch = new(LaunchMode.Home, []);
        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().AfterSetup(_ =>
            Dispatcher.UIThread.Post(() =>
            {
                var app = (App)Application.Current!;
                var desktop = (IClassicDesktopStyleApplicationLifetime)app.ApplicationLifetime!;
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                desktop.MainWindow?.Hide();
                app.Flow.ShowForDrag(new PixelPoint(1100, 550));
                var wheel = app.Flow.LastWheel!;
                wheel.Wheel.Turned += () =>
                {
                    if (wheel.Wheel.OpenGroupIndex >= 0)
                    {
                        Console.WriteLine($"GROUP|{wheel.Wheel.OpenGroupIndex}");
                        Console.Out.Flush();
                    }
                };
                void Announce()
                {
                    if (!wheel.Known) { DispatcherTimer.RunOnce(Announce, TimeSpan.FromMilliseconds(50)); return; }
                    var (rect, scale) = wheel.ScreenRect();
                    var items = wheel.Wheel.Items;
                    var images = items.ToList().FindIndex(item => item.Group?.Any(child => child.Id == "convert:jpg") == true);
                    var children = items[images].Group!;
                    var jpg = children.ToList().FindIndex(item => item.Id == "convert:jpg");
                    Console.WriteLine(FormattableString.Invariant($"WHEEL|{wheel.TryGetPlatformHandle()!.Handle.ToInt64()}|{rect.X}|{rect.Y}|{rect.Width}|{rect.Height}|{scale}|{items.Count}|{images}|{jpg}|{children.Count}|{Wheel.SubInner}|{Wheel.SubOuter}|{Wheel.SubSpan(360.0 / items.Count, children.Count)}"));
                    Console.Out.Flush();
                }
                DispatcherTimer.RunOnce(Announce, TimeSpan.FromMilliseconds(250));
                DispatcherTimer.RunOnce(() =>
                {
                    var outputs = Directory.GetFiles(directory, "*.jpg");
                    Console.WriteLine($"RESULT|known={wheel.Known}|visible={wheel.IsVisible}|files={outputs.Length}");
                    desktop.Shutdown(outputs.Length == 1 && !wheel.IsVisible ? 0 : 1);
                }, TimeSpan.FromSeconds(7));
            })).StartWithClassicDesktopLifetime([]);
    }
}
