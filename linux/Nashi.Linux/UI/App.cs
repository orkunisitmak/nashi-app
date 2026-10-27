using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Nashi.Core;
namespace Nashi.Linux.UI;

internal sealed class App:Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());Brand.Install(this);Name="Nashi";
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var library=new LibraryStore(Paths.Data);var preferences=new PreferencesStore(Paths.Data);
            L10n.Language=preferences.Value.Language;Brand.Apply(library.Appearance);
            var files=(desktop.Args??[]).Select(Desktop.LocalPath).OfType<string>().ToArray();
            desktop.MainWindow=new MainWindow(library,preferences,files);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
