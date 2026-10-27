using Avalonia;
using Nashi.Linux;

if(Cli.Handles(args))return await Cli.RunAsync(args);
if(args.Length>0&&args[0]=="--render")return await Nashi.Linux.UI.Snapshot.RunAsync(args[1..]);
return AppBuilder.Configure<Nashi.Linux.UI.App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
