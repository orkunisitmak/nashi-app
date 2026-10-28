using Avalonia;
using Nashi.Linux;

if(Cli.Handles(args))return await Cli.RunAsync(args);
if(args.Length>0&&args[0]=="--render")return await Nashi.Linux.UI.Snapshot.RunAsync(args[1..]);
// A second launch (Open with, a Dolphin action, a shortcut) goes to the running window.
if(Nashi.Linux.UI.SingleInstance.TryForward(Nashi.Linux.UI.LaunchRequest.Parse(args)))return 0;
return AppBuilder.Configure<Nashi.Linux.UI.App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
