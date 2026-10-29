using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Nashi.Core;
namespace Nashi.Linux;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args is ["--verify-operations",var root])return OperationChecks.Run(root);
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class App:Application
{
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var library=new LibraryStore(LinuxPaths.DataDirectory);
            NashiTheme.Apply(this,library.Appearance);
            desktop.MainWindow=new MainWindow(library,desktop.Args??[]);
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal static class LinuxPaths
{
    /// XDG data folder, outside the portable app folder so replacing the app keeps the Library.
    public static string DataDirectory=>Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is {Length:>0} xdg&&Path.IsPathFullyQualified(xdg)?xdg:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".local","share"),"Nashi");
}
