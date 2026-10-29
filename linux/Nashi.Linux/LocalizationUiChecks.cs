using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Nashi.Core;
using Nashi.Windows;
namespace Nashi.Linux;

internal static class LocalizationUiChecks
{
    internal static async Task Run(Window owner,string image,string directory,Action<bool,string> check,Action<Window,string> capture)
    {
        var language=NashiTheme.Language;var appearance=NashiTheme.Dark?"dark":"light";
        var state=Path.Combine(directory,"french-state");var store=new LibraryStore(state);store.SetAppearance("light");
        var preferences=new PreferencesStore(state);preferences.Save(preferences.Value with{Language="fr"});
        var screen=new MainWindow([],store,false);screen.Show(owner);await Task.Delay(100);
        try{
            check(screen.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Vos fichiers, sous leur prochaine forme."),"French preference selects the original French Home catalog");
            check(screen.GetVisualDescendants().OfType<Button>().Any(b=>b.Content as string=="Bibliothèque"),"French original navigation copy reaches native buttons");capture(screen,Path.Combine(directory,"home-french.png"));
            screen.Width=640;screen.Height=520;await Task.Delay(80);screen.UpdateLayout();
            var navigation=screen.GetVisualDescendants().OfType<Button>().Where(b=>new[]{AppLocalization.Text("Set up tools","fr"),"Accueil","Bibliothèque","Réglages"}.Contains(b.Content as string)).ToArray();
            var brand=screen.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="Nashi");var brandArea=new Rect(brand.TranslatePoint(default,screen)!.Value,brand.Bounds.Size);
            check(navigation.Length==4&&navigation.All(b=>b.TranslatePoint(default,screen) is {} point&&point.X>=0&&point.X+b.Bounds.Width<=screen.Bounds.Width&&!new Rect(point,b.Bounds.Size).Intersects(brandArea)),"French compact navigation fits and does not overlap Nashi branding");capture(screen,Path.Combine(directory,"compact-french.png"));
            var editor=new ToolEditor("cropImage",null,[image],new LinuxEngine());editor.Show(screen);await Task.Delay(80);
            try{check(editor.Title=="Recadrer l’image · Nashi"&&editor.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Recadrer l’image"),"French tool titles preserve the Nashi suffix and localized heading");capture(editor,Path.Combine(directory,"crop-french.png"));}
            finally{editor.Close((LinuxOptions?)null);}
        }finally{screen.Close();NashiTheme.Language=language;NashiTheme.Apply(appearance);}
    }
}
