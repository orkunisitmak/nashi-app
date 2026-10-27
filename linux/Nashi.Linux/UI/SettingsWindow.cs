using Avalonia.Controls;
using Avalonia.Layout;
using Nashi.Core;
namespace Nashi.Linux.UI;

/// The shared preferences.json and library.json, so a home folder synced between
/// a Windows and a Linux install keeps one set of choices.
internal sealed class SettingsWindow:Window
{
    public bool Saved{get;private set;}
    public SettingsWindow(PreferencesStore preferences,LibraryStore library)
    {
        Title=L10n.T("Nashi Settings");Width=600;SizeToContent=SizeToContent.Height;MaxHeight=800;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;this.Res(BackgroundProperty,"Paper");
        var p=preferences.Value;var stack=new StackPanel{Spacing=8};Content=new ScrollViewer{Content=new Border{Padding=new(26),Child=stack}};
        stack.Children.Add(Brand.Text("Make it yours",26));stack.Children.Add(Brand.Text("A few small preferences. A smoother everyday.",13,"Muted"));
        ComboBox Combo(string label,string[] titles,int selected){var box=new ComboBox{ItemsSource=titles.Select(L10n.T).ToArray(),SelectedIndex=selected,MinWidth=220};var row=new DockPanel{Margin=new(0,6)};DockPanel.SetDock(box,Dock.Right);row.Children.Add(box);row.Children.Add(Brand.Text(label,13));stack.Children.Add(row);return box;}
        TextBox Folder(string label,string value)
        {
            var box=new TextBox{Text=value,MinWidth=200};var choose=Brand.Button("Choose…",async()=>{if(await Dialogs.FolderAsync(this,"Choose a folder") is {} folder)box.Text=folder;});choose.Margin=new(8,0,0,0);
            stack.Children.Add(Brand.Text(label,13));var row=new DockPanel();DockPanel.SetDock(choose,Dock.Right);row.Children.Add(choose);row.Children.Add(box);stack.Children.Add(row);return box;
        }
        var language=Combo("Language",["English","Türkçe"],p.Language=="tr"?1:0);
        var appearance=Combo("Appearance",["System","Light","Dark"],library.Appearance switch{"light"=>1,"dark"=>2,_=>0});
        var outputModes=new[]{"beside","ask","folder"};var output=Combo("Save new files",["Beside the original","Ask every time","Always this folder"],Math.Max(0,Array.IndexOf(outputModes,p.OutputMode)));
        var outputFolder=Folder("Output folder",p.OutputFolder);var downloads=Folder("Download folder",p.DownloadFolder);
        var reveal=new CheckBox{Content=L10n.T("Show finished files in the file manager"),IsChecked=p.RevealFinished,Margin=new(0,8,0,0)};stack.Children.Add(reveal);
        var note=Brand.Text("Updates come from your package or a new release; Nashi does not replace itself on Linux.",11,"Muted");note.Margin=new(0,10,0,0);stack.Children.Add(note);
        var error=Brand.Text("",12,"Gold",false);stack.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,8,0,0)};
        buttons.Children.Add(Brand.Button("Cancel",Close));buttons.Children.Add(Brand.Primary("Save",()=>
        {
            try
            {
                var mode=outputModes[Math.Max(0,output.SelectedIndex)];var folder=(outputFolder.Text??"").Trim();var download=(downloads.Text??"").Trim();
                if(mode=="folder"&&!Path.IsPathFullyQualified(folder))throw new ArgumentException(L10n.T("Choose a folder"));if(!Path.IsPathFullyQualified(download))throw new ArgumentException(L10n.T("Choose a folder"));
                preferences.Save(p with{Language=language.SelectedIndex==1?"tr":"en",OutputMode=mode,OutputFolder=folder,DownloadFolder=download,RevealFinished=reveal.IsChecked==true});
                library.SetAppearance(new[]{"system","light","dark"}[Math.Max(0,appearance.SelectedIndex)]);Saved=true;Close();
            }
            catch(Exception failure)when(failure is ArgumentException or IOException or UnauthorizedAccessException){error.Text=failure.Message;}
        }));
        stack.Children.Add(buttons);
    }
}
