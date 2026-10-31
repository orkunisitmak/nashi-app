using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using Microsoft.Win32;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class SettingsWindow:Window
{
    private readonly LibraryStore? appearanceStore;
    private readonly PreferencesStore store;private readonly Action changed;
    private readonly StackPanel page=new(){Margin=new(28)};private readonly ListBox sections=new(){Width=175,ItemsSource=new[]{"General","Formats","Compatibility","Folders","Updates","About"},SelectedIndex=0};
    public SettingsWindow(Window owner,PreferencesStore store,Action changed,Func<Task> update,LibraryStore? appearanceStore=null)
    {
        this.appearanceStore=appearanceStore;L10n.Choices(sections);
        this.store=store;this.changed=changed;Owner=owner;Theme.Attach(this);Title="Make it yours · Nashi";Width=850;Height=680;MinWidth=740;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;var root=new DockPanel();Content=root;DockPanel.SetDock(sections,Dock.Left);root.Children.Add(sections);root.Children.Add(new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});sections.SelectionChanged+=(_,_)=>ShowSection(update);ShowSection(update);PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){Close();e.Handled=true;}};
    }
    private void Save(Preferences value){try{store.Save(value);changed();}catch(Exception error)when(error is IOException or ArgumentException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Settings could not be saved"));}}
    private void Heading(string title,string hint){page.Children.Add(L10n.Label(new TextBlock{Text=title,FontSize=28,FontWeight=FontWeights.Bold}));page.Children.Add(L10n.Label(new TextBlock{Text=hint,TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,20),Foreground=Theme.Brush("Muted")}));}
    private void Toggle(string title,bool value,Action<bool> save){var box=L10n.Content(new CheckBox{Content=title,IsChecked=value,Margin=new(4,12,4,12)});box.Checked+=(_,_)=>save(true);box.Unchecked+=(_,_)=>save(false);page.Children.Add(box);}
    private void Pick(string title,string[] values,string value,Action<string> save){page.Children.Add(L10n.Label(new TextBlock{Text=title,Margin=new(4,12,4,0)}));var combo=new ComboBox{ItemsSource=values,SelectedItem=value};combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is string selected)save(selected);};page.Children.Add(combo);}
    private void LanguagePicker()
    {
        page.Children.Add(L10n.Label(new TextBlock{Text="Language",Margin=new(4,12,4,0)}));
        string[] codes=["en","tr","fr"];var language=new ComboBox{ItemsSource=new[]{"English","Türkçe","Français"},SelectedIndex=Math.Max(0,Array.IndexOf(codes,store.Value.Language))};
        System.Windows.Automation.AutomationProperties.SetAutomationId(language,"AppLanguage");
        language.SelectionChanged+=(_,_)=>Save(store.Value with{Language=codes[Math.Max(0,language.SelectedIndex)]});page.Children.Add(language);
    }
    private void ShowSection(Func<Task> update)
    {
        page.Children.Clear();switch(sections.SelectedItem as string)
        {
            case "General":
                Heading("Your kind of flow.","A few small preferences. A smoother everyday.");LanguagePicker();if(appearanceStore is not null)Pick("Appearance",["system","light","dark"],appearanceStore.Appearance,v=>{try{appearanceStore.SetAppearance(v);Theme.Apply(v);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Appearance could not be saved"));}});Toggle("Enable Nashi drag shortcuts",store.Value.Enabled,v=>Save(store.Value with{Enabled=v}));if(appearanceStore is not null)Toggle("Window management · drag a title bar, then Shift or Alt",appearanceStore.WindowManagementEnabled,v=>{appearanceStore.SetWindowManagement(v);changed();});Toggle("Sound feedback",store.Value.Feedback,v=>Save(store.Value with{Feedback=v}));page.Children.Add(MainWindow.Button("Try feedback",Feedback.Play));Toggle("Subtle animations",store.Value.Animations,v=>Save(store.Value with{Animations=v}));Toggle("Keep running in the system tray",store.Value.KeepInTray,v=>Save(store.Value with{KeepInTray=v}));Toggle("Reveal finished files in Explorer",store.Value.RevealFinished,v=>Save(store.Value with{RevealFinished=v}));
                Pick("Wheel finish",["glass","solid"],store.Value.WheelFinish,v=>Save(store.Value with{WheelFinish=v}));var size=new Slider{Minimum=.85,Maximum=1.4,Value=store.Value.WheelScale,Margin=new(4,12,4,8),ToolTip="Wheel size"};size.ValueChanged+=(_,_)=>Save(store.Value with{WheelScale=size.Value});page.Children.Add(L10n.Label(new TextBlock{Text="Wheel size"}));page.Children.Add(size);
                var modifiers=new[]{"Ctrl","Shift","Alt","Ctrl+Shift","Ctrl+Alt","Shift+Alt"};Pick("Tools · while dragging",modifiers.Where(m=>m!=store.Value.ConvertModifier&&m!=store.Value.ActionsModifier).ToArray(),store.Value.ToolsModifier,v=>Save(store.Value with{ToolsModifier=v}));Pick("Convert · while dragging",modifiers.Where(m=>m!=store.Value.ToolsModifier&&m!=store.Value.ActionsModifier).ToArray(),store.Value.ConvertModifier,v=>Save(store.Value with{ConvertModifier=v}));Pick("File actions · while dragging",modifiers.Where(m=>m!=store.Value.ToolsModifier&&m!=store.Value.ConvertModifier).ToArray(),store.Value.ActionsModifier,v=>Save(store.Value with{ActionsModifier=v}));Pick("Float behavior",["createNew","addToExisting"],store.Value.FloatBehavior,v=>Save(store.Value with{FloatBehavior=v}));
                using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){var enabled=key?.GetValue("Nashi") is string;Toggle("Launch at login",enabled,v=>{try{using var run=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(v)run.SetValue("Nashi","\""+Environment.ProcessPath+"\" --background");else run.DeleteValue("Nashi",false);}catch(Exception error){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Launch at login"));}});}break;
            case "Formats":
                FormatDefaults();break;
            case "Compatibility":Heading("Everything has a next form.","The same conversion and tool catalog as macOS.");foreach(var pair in FeatureCatalog.Conversions.OrderBy(p=>p.Key))page.Children.Add(new TextBlock{Text=pair.Key.ToUpperInvariant()+"  →  "+string.Join(", ",pair.Value.Select(v=>v.ToUpperInvariant())),TextWrapping=TextWrapping.Wrap,Margin=new(4,8,4,8)});break;
            case "Folders":Heading("A place for fresh files.","Originals remain safe. Choose where new copies should land.");Pick("Save new copies",["beside","ask","folder"],store.Value.OutputMode,v=>Save(store.Value with{OutputMode=v}));page.Children.Add(MainWindow.Button("Choose output folder…",()=>{var picker=new OpenFolderDialog{Title="Save new copies here"};if(picker.ShowDialog(this)==true){Save(store.Value with{OutputFolder=picker.FolderName,OutputMode="folder"});ShowSection(update);}}));page.Children.Add(new TextBlock{Text=store.Value.OutputFolder,TextWrapping=TextWrapping.Wrap});page.Children.Add(MainWindow.Button("Choose downloads folder…",()=>{var picker=new OpenFolderDialog{Title="Save downloaded media here"};if(picker.ShowDialog(this)==true){Save(store.Value with{DownloadFolder=picker.FolderName});ShowSection(update);}}));page.Children.Add(new TextBlock{Text=store.Value.DownloadFolder,TextWrapping=TextWrapping.Wrap});break;
            case "Updates":Heading("Fresh things, on their way.","Updates are downloaded from the public Nashi repository and verified before installation. No GitHub account or token is required.");Toggle("Check for updates automatically",store.Value.AutomaticUpdateChecks,v=>Save(store.Value with{AutomaticUpdateChecks=v}));page.Children.Add(MainWindow.Button("Check for updates",async()=>await update()));break;
            default:Heading("Nashi", "A little pear. A lot of possibility.");page.Children.Add(L10n.Label(new TextBlock{Text="Local by design · MIT source\nVersion "+UpdateInstaller.Current,Margin=new(4,12,4,12)}));page.Children.Add(MainWindow.Button("Source & third-party notices",()=>MainWindow.OpenUrl("https://github.com/orkunisitmak/nashi-app")));break;
        }
    }
}
