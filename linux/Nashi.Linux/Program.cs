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
        // The bundled ImageMagick has its own fontconfig, which does not find the system configuration by itself.
        if(Environment.GetEnvironmentVariable("FONTCONFIG_FILE") is null&&File.Exists("/etc/fonts/fonts.conf"))Environment.SetEnvironmentVariable("FONTCONFIG_FILE","/etc/fonts/fonts.conf");
        if(args is ["--verify-operations",var root])return OperationChecks.Run(root);
        if(args is ["--verify-editors",var folder])return EditorChecks.Run(folder);
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class App:Application
{
    public override void Initialize(){Styles.Add(new FluentTheme());NashiStyles.Install(this);}
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var library=new LibraryStore(LinuxPaths.DataDirectory);
            var forced=desktop.Args?.SkipWhile(a=>a!="--lang").Skip(1).FirstOrDefault();
            Localizer.Language=forced is "tr" or "en"?forced:Localizer.Initial(new PreferencesStore(LinuxPaths.DataDirectory),LinuxPaths.DataDirectory);Localizer.Install();
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
