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
    private StackPanel? currentCard;
    private readonly StackPanel page=new(){Margin=new(30,28,34,28)};private readonly ListBox sections=new(){Width=164,ItemsSource=new[]{"General","Formats","Compatibility","Folders","Updates","About"},SelectedIndex=0};
    public SettingsWindow(Window owner,PreferencesStore store,Action changed,Func<Task> update,LibraryStore? appearanceStore=null)
    {
        this.appearanceStore=appearanceStore;L10n.IconChoices(sections);
        this.store=store;this.changed=changed;Owner=owner;Theme.Attach(this);Title="Make it yours · Nashi";Width=850;Height=680;MinWidth=740;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;var root=new DockPanel();Content=root;var nav=new Border{CornerRadius=new CornerRadius(16),Padding=new Thickness(10),Margin=new Thickness(14,18,0,18),Child=sections};nav.SetResourceReference(Border.BackgroundProperty,"Surface");nav.SetResourceReference(Border.BorderBrushProperty,"Line");nav.BorderThickness=new Thickness(1);DockPanel.SetDock(nav,Dock.Left);root.Children.Add(nav);var navStyle=new Style(typeof(ListBoxItem),(Style)Application.Current.FindResource(typeof(ListBoxItem)));navStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,0,0,4)));navStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,11,8,11)));navStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,40d));sections.ItemContainerStyle=navStyle;root.Children.Add(new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});sections.SelectionChanged+=(_,_)=>ShowSection(update);ShowSection(update);PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){Close();e.Handled=true;}};
    }
    private void Save(Preferences value){try{store.Save(value);changed();}catch(Exception error)when(error is IOException or ArgumentException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Settings could not be saved"));}}
    private void Add(UIElement item)=>(currentCard??page).Children.Add(item);
    private void Section(string label)
    {
        var panel=new StackPanel();
        panel.Children.Add(L10n.Label(new TextBlock{Text=label,FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)}));
        var card=new Border{Tag="NashiSettingsCard",CornerRadius=new CornerRadius(NashiDesignTokens.SettingsCardRadius),Padding=new Thickness(18),
            Margin=new Thickness(0,0,0,14),Child=panel};
        card.SetResourceReference(Border.BackgroundProperty,"Surface");
        card.SetResourceReference(Border.BorderBrushProperty,"Line");
        card.BorderThickness=new Thickness(1);
        page.Children.Add(card);
        currentCard=panel;
    }
    private void Heading(string title,string hint){Add(L10n.Label(new TextBlock{Text=title,FontSize=28,FontWeight=FontWeights.Bold}));Add(L10n.Label(new TextBlock{Text=hint,TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,20),Foreground=Theme.Brush("Muted")}));}
    private void Toggle(string title,bool value,Action<bool> save){var box=L10n.Content(new CheckBox{Content=title,IsChecked=value,Margin=new(2,5,2,5),MinHeight=36,MaxWidth=480,HorizontalAlignment=HorizontalAlignment.Stretch});box.Style=(Style)Application.Current.FindResource("NashiToggleSwitch");box.Checked+=(_,_)=>save(true);box.Unchecked+=(_,_)=>save(false);Add(box);}
    private void Pick(string title,string[] values,string value,Action<string> save){Add(L10n.Label(new TextBlock{Text=title,Margin=new(4,12,4,0)}));var combo=new ComboBox{ItemsSource=values,SelectedItem=value,MaxWidth=480,MinWidth=260};combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is string selected)save(selected);};Add(combo);}
    private void LanguagePicker()
    {
        Add(L10n.Label(new TextBlock{Text="Language",Margin=new(4,12,4,0)}));
        var language=new ComboBox{ItemsSource=new[]{"English","Türkçe"},SelectedIndex=store.Value.Language=="tr"?1:0,MaxWidth=480,MinWidth=260};
        System.Windows.Automation.AutomationProperties.SetAutomationId(language,"AppLanguage");
        language.SelectionChanged+=(_,_)=>Save(store.Value with{Language=language.SelectedIndex==1?"tr":"en"});Add(language);
    }
    private void ShowSection(Func<Task> update)
    {
        page.Children.Clear();currentCard=null;switch(sections.SelectedItem as string)
        {
            case "General":
                Heading("Your kind of flow.","A few small preferences. A smoother everyday.");Section("Appearance");LanguagePicker();if(appearanceStore is not null)Pick("Appearance",["system","light","dark"],appearanceStore.Appearance,v=>{try{appearanceStore.SetAppearance(v);Theme.Apply(v);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Appearance could not be saved"));}});Section("General");Toggle("Enable Nashi drag shortcuts",store.Value.Enabled,v=>Save(store.Value with{Enabled=v}));if(appearanceStore is not null)Toggle("Window management · drag a title bar, then Shift or Alt",appearanceStore.WindowManagementEnabled,v=>{appearanceStore.SetWindowManagement(v);changed();});Toggle("Sound feedback",store.Value.Feedback,v=>Save(store.Value with{Feedback=v}));Add(MainWindow.Button("Try feedback",Feedback.Play));Toggle("Subtle animations",store.Value.Animations,v=>Save(store.Value with{Animations=v}));Toggle("Keep running in the system tray",store.Value.KeepInTray,v=>Save(store.Value with{KeepInTray=v}));Toggle("Reveal finished files in Explorer",store.Value.RevealFinished,v=>Save(store.Value with{RevealFinished=v}));
                Section("Wheel finish");Pick("Wheel finish",["glass","solid"],store.Value.WheelFinish,v=>Save(store.Value with{WheelFinish=v}));var size=new Slider{Minimum=.85,Maximum=1.4,Value=store.Value.WheelScale,MaxWidth=420,HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new(4,12,4,8),ToolTip="Wheel size"};size.ValueChanged+=(_,_)=>Save(store.Value with{WheelScale=size.Value});Add(L10n.Label(new TextBlock{Text="Wheel size"}));Add(size);
                var modifiers=new[]{"Ctrl","Shift","Alt","Ctrl+Shift","Ctrl+Alt","Shift+Alt"};Pick("Tools · while dragging",modifiers.Where(m=>m!=store.Value.ConvertModifier&&m!=store.Value.ActionsModifier).ToArray(),store.Value.ToolsModifier,v=>Save(store.Value with{ToolsModifier=v}));Pick("Convert · while dragging",modifiers.Where(m=>m!=store.Value.ToolsModifier&&m!=store.Value.ActionsModifier).ToArray(),store.Value.ConvertModifier,v=>Save(store.Value with{ConvertModifier=v}));Pick("File actions · while dragging",modifiers.Where(m=>m!=store.Value.ToolsModifier&&m!=store.Value.ConvertModifier).ToArray(),store.Value.ActionsModifier,v=>Save(store.Value with{ActionsModifier=v}));Pick("Float behavior",["createNew","addToExisting"],store.Value.FloatBehavior,v=>Save(store.Value with{FloatBehavior=v}));
                if(!PreviewLaunch.Enabled){Section("Launch at login");using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){var enabled=key?.GetValue("Nashi") is string;Toggle("Launch at login",enabled,v=>{try{using var run=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(v)run.SetValue("Nashi","\""+Environment.ProcessPath+"\" --background");else run.DeleteValue("Nashi",false);}catch(Exception error){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Launch at login"));}});}}break;
            case "Formats":
                FormatDefaults();break;
            case "Compatibility":Heading("Everything has a next form.","The same conversion and tool catalog as macOS.");Section("Compatibility");foreach(var pair in FeatureCatalog.Conversions.OrderBy(p=>p.Key))Add(new TextBlock{Text=pair.Key.ToUpperInvariant()+"  →  "+string.Join(", ",pair.Value.Select(v=>v.ToUpperInvariant())),TextWrapping=TextWrapping.Wrap,Margin=new(4,8,4,8)});break;
            case "Folders":Heading("A place for fresh files.","Originals remain safe. Choose where new copies should land.");Section("Folders");Pick("Save new copies",["beside","ask","folder"],store.Value.OutputMode,v=>Save(store.Value with{OutputMode=v}));Add(MainWindow.Button("Choose output folder…",()=>{var picker=new OpenFolderDialog{Title="Save new copies here"};if(picker.ShowDialog(this)==true){Save(store.Value with{OutputFolder=picker.FolderName,OutputMode="folder"});ShowSection(update);}}));Add(new TextBlock{Text=store.Value.OutputFolder,TextWrapping=TextWrapping.Wrap});Add(MainWindow.Button("Choose downloads folder…",()=>{var picker=new OpenFolderDialog{Title="Save downloaded media here"};if(picker.ShowDialog(this)==true){Save(store.Value with{DownloadFolder=picker.FolderName});ShowSection(update);}}));Add(new TextBlock{Text=store.Value.DownloadFolder,TextWrapping=TextWrapping.Wrap});break;
            case "Updates":Heading("Fresh things, on their way.","Updates are downloaded from the public Nashi repository and verified before installation. No GitHub account or token is required.");Section("Updates");Toggle("Check for updates automatically",store.Value.AutomaticUpdateChecks,v=>Save(store.Value with{AutomaticUpdateChecks=v}));Add(MainWindow.Button("Check for updates",async()=>await update()));break;
            default:Heading("Nashi", "A little pear. A lot of possibility.");Section("About");Add(L10n.Label(new TextBlock{Text="Local by design · MIT source\nVersion "+UpdateInstaller.Current,Margin=new(4,12,4,12)}));Add(MainWindow.Button("Source & third-party notices",()=>MainWindow.OpenUrl("https://github.com/orkunisitmak/nashi-app")));break;
        }
    }
}
