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
    private readonly StackPanel shortcutPanel=new(){Margin=new(28,0,4,8)};private WindowPlacement? recording;private string? shortcutMessage;
    private readonly StackPanel page=new(){Margin=new(28)};private readonly ListBox sections=new(){Width=175,ItemsSource=new[]{"General","Formats","Compatibility","Folders","Updates","About"},SelectedIndex=0};
    public SettingsWindow(Window owner,PreferencesStore store,Action changed,Func<Task> update,LibraryStore? appearanceStore=null)
    {
        this.appearanceStore=appearanceStore;L10n.Choices(sections);
        this.store=store;this.changed=changed;Owner=owner;Theme.Attach(this);Title="Make it yours · Nashi";Width=850;Height=680;MinWidth=740;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;var root=new DockPanel();Content=root;DockPanel.SetDock(sections,Dock.Left);root.Children.Add(sections);root.Children.Add(new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});sections.SelectionChanged+=(_,_)=>ShowSection(update);ShowSection(update);PreviewKeyDown+=(_,e)=>{if(recording is {} placement){RecordShortcut(placement,e);e.Handled=true;return;}if(e.Key==System.Windows.Input.Key.Escape){Close();e.Handled=true;}};Deactivated+=(_,_)=>StopRecording();Closed+=(_,_)=>StopRecording();
    }
    private void Save(Preferences value){try{store.Save(value);changed();}catch(Exception error)when(error is IOException or ArgumentException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Settings could not be saved"));}}
    private void Heading(string title,string hint){page.Children.Add(L10n.Label(new TextBlock{Text=title,FontSize=28,FontWeight=FontWeights.Bold}));page.Children.Add(L10n.Label(new TextBlock{Text=hint,TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,20),Foreground=Theme.Brush("Muted")}));}
    private void Toggle(string title,bool value,Action<bool> save){var box=L10n.Content(new CheckBox{Content=title,IsChecked=value,Margin=new(4,12,4,12)});box.Checked+=(_,_)=>save(true);box.Unchecked+=(_,_)=>save(false);page.Children.Add(box);}
    private void Pick(string title,string[] values,string value,Action<string> save){page.Children.Add(L10n.Label(new TextBlock{Text=title,Margin=new(4,12,4,0)}));var combo=new ComboBox{ItemsSource=values,SelectedItem=value};combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is string selected)save(selected);};page.Children.Add(combo);}
    private void LanguagePicker()
    {
        page.Children.Add(L10n.Label(new TextBlock{Text="Language",Margin=new(4,12,4,0)}));
        var language=new ComboBox{ItemsSource=new[]{"English","Türkçe"},SelectedIndex=store.Value.Language=="tr"?1:0};
        System.Windows.Automation.AutomationProperties.SetAutomationId(language,"AppLanguage");
        language.SelectionChanged+=(_,_)=>Save(store.Value with{Language=language.SelectedIndex==1?"tr":"en"});page.Children.Add(language);
    }
    /// Every layout row has a Record button that captures the next key combination.
    private void RenderShortcuts(WindowPlacement? focus=null)
    {
        // Rebuilt buttons would otherwise drop keyboard and screen reader focus from the row.
        var refocus=shortcutPanel.IsKeyboardFocusWithin?focus:null;shortcutPanel.Children.Clear();shortcutPanel.IsEnabled=appearanceStore?.WindowManagementEnabled!=false&&store.Value.WindowShortcutsEnabled;
        var rows=new Grid();foreach(var width in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})rows.ColumnDefinitions.Add(new ColumnDefinition{Width=width});
        foreach(var placement in Enum.GetValues<WindowPlacement>())
        {
            var row=rows.RowDefinitions.Count;rows.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var active=recording==placement;
            var glyph=WheelPresentation.Glyph(placement);glyph.Margin=new(0,5,12,5);glyph.VerticalAlignment=VerticalAlignment.Center;
            var title=L10n.Label(new TextBlock{Text=placement.Title(),VerticalAlignment=VerticalAlignment.Center});
            // Key names stay as typed; the placeholder copy follows the language picker.
            var shortcut=store.Value.Shortcut(placement);var text=new TextBlock{Text=active?"Type a shortcut…":shortcut?.Text??"None",FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center};if(active||shortcut is null)L10n.Label(text);
            var combo=new Border{MinWidth=130,Margin=new(12,4,8,4),Padding=new(8,4,8,4),CornerRadius=new(7),Background=Theme.Brush(active?"Sand":"Inset"),Child=text};
            var button=MainWindow.Button(active?"Cancel":"Record",()=>{if(recording==placement)StopRecording();else StartRecording(placement);});button.MinWidth=80;button.Margin=new(0,4,0,4);if(placement==refocus)_=Dispatcher.InvokeAsync(()=>button.Focus(),System.Windows.Threading.DispatcherPriority.Loaded);
            System.Windows.Automation.AutomationProperties.SetName(button,L10n.Text(active?"Cancel":"Record")+" "+L10n.Text(placement.Title()));
            foreach(var (element,column) in new (UIElement,int)[]{(glyph,0),(title,1),(combo,2),(button,3)}){Grid.SetRow(element,row);Grid.SetColumn(element,column);rows.Children.Add(element);}
        }
        shortcutPanel.Children.Add(rows);
        var footer=new DockPanel{Margin=new(0,8,0,0)};var reset=MainWindow.Button("Restore defaults",()=>{StopRecording();shortcutMessage=null;Save(store.Value with{WindowShortcuts=[]});RenderShortcuts();});DockPanel.SetDock(reset,Dock.Right);footer.Children.Add(reset);
        // Wrap beside the button instead of truncating the longer Turkish copy.
        var help=new TextBlock{Text=shortcutMessage??"Press Record, then type a combination with Ctrl, Alt or Win. Backspace clears it; Escape cancels.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,12,0),Foreground=Theme.Brush("Muted")};if(shortcutMessage is null)L10n.Label(help);footer.Children.Add(help);
        shortcutPanel.Children.Add(footer);
    }
    private void StartRecording(WindowPlacement placement){recording=placement;shortcutMessage=null;WindowShortcutHost.Recording=true;RenderShortcuts(placement);}
    private void StopRecording(){if(recording is not {} placement)return;recording=null;WindowShortcutHost.Recording=false;RenderShortcuts(placement);}
    private void RecordShortcut(WindowPlacement placement,System.Windows.Input.KeyEventArgs e)
    {
        // Alt combinations arrive as a system key.
        var key=e.Key==System.Windows.Input.Key.System?e.SystemKey:e.Key;var code=System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if(code==0||WindowShortcut.IsModifierKey(code))return;
        var held=System.Windows.Input.Keyboard.Modifiers;var modifiers=0;
        if(held.HasFlag(System.Windows.Input.ModifierKeys.Control))modifiers|=WindowShortcut.Control;if(held.HasFlag(System.Windows.Input.ModifierKeys.Alt))modifiers|=WindowShortcut.Alt;
        if(held.HasFlag(System.Windows.Input.ModifierKeys.Shift))modifiers|=WindowShortcut.Shift;if(held.HasFlag(System.Windows.Input.ModifierKeys.Windows))modifiers|=WindowShortcut.Win;
        if(modifiers==0&&key==System.Windows.Input.Key.Escape){StopRecording();return;}
        if(modifiers==0&&key is System.Windows.Input.Key.Back or System.Windows.Input.Key.Delete){var (cleared,_)=store.Value.WithShortcut(placement,null);if(cleared is not null)Save(cleared);StopRecording();return;}
        var shortcut=new WindowShortcut(modifiers,code);var (value,conflict)=store.Value.WithShortcut(placement,shortcut);
        if(value is not null){Save(value);StopRecording();return;}
        shortcutMessage=conflict is null?L10n.Text("Include Ctrl, Alt or Win in the shortcut."):L10n.Text(shortcut.Text+" is already used by "+L10n.Text(conflict)+".");
        System.Media.SystemSounds.Beep.Play();RenderShortcuts(placement);
    }
    private void ShowSection(Func<Task> update)
    {
        StopRecording();page.Children.Clear();switch(sections.SelectedItem as string)
        {
            case "General":
                Heading("Your kind of flow.","A few small preferences. A smoother everyday.");LanguagePicker();if(appearanceStore is not null)Pick("Appearance",["system","light","dark"],appearanceStore.Appearance,v=>{try{appearanceStore.SetAppearance(v);Theme.Apply(v);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Appearance could not be saved"));}});Toggle("Enable Nashi drag shortcuts",store.Value.Enabled,v=>Save(store.Value with{Enabled=v}));if(appearanceStore is not null){Toggle("Window management · drag a title bar, then Shift or Alt",appearanceStore.WindowManagementEnabled,v=>{appearanceStore.SetWindowManagement(v);changed();RenderShortcuts();});Toggle("Arrange the front window with keyboard shortcuts",store.Value.WindowShortcutsEnabled,v=>{Save(store.Value with{WindowShortcutsEnabled=v});RenderShortcuts();});page.Children.Add(shortcutPanel);RenderShortcuts();}Toggle("Sound feedback",store.Value.Feedback,v=>Save(store.Value with{Feedback=v}));page.Children.Add(MainWindow.Button("Try feedback",Feedback.Play));Toggle("Subtle animations",store.Value.Animations,v=>Save(store.Value with{Animations=v}));Toggle("Keep running in the system tray",store.Value.KeepInTray,v=>Save(store.Value with{KeepInTray=v}));Toggle("Reveal finished files in Explorer",store.Value.RevealFinished,v=>Save(store.Value with{RevealFinished=v}));
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
