using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using Microsoft.Win32;
using Nashi.Core;
namespace Nashi.Windows;
/// What Settings › Updates needs from Nashi: the update already found, a fresh check, and installing one.
internal sealed record UpdateFlow(Func<WindowsUpdate?> Known,Func<Task<WindowsUpdate?>> Check,Func<WindowsUpdate,IProgress<OperationProgress>,CancellationToken,Task> Install);
public sealed partial class SettingsWindow:Window
{
    private readonly LibraryStore? appearanceStore;
    private readonly PreferencesStore store;private readonly Action changed;private readonly UpdateFlow? updates;private bool checkOnOpen;
    private readonly StackPanel page=new(){Margin=new(32,28,32,28)};private StackPanel? group;private readonly ScrollViewer scroller=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly ListBox sections=new(){Width=200,ItemsSource=new[]{"General","Formats","Motion & sound","Shortcuts","Compatibility","Folders","Updates","About"},SelectedIndex=0};
    internal SettingsWindow(Window owner,PreferencesStore store,Action changed,UpdateFlow? updates,LibraryStore? appearanceStore=null,string? section=null,bool check=false)
    {
        this.appearanceStore=appearanceStore;this.updates=updates;checkOnOpen=check;L10n.Choices(sections);if(section is not null)sections.SelectedItem=section;
        this.store=store;this.changed=changed;Owner=owner;Theme.Attach(this);Title="Make it yours · Nashi";Width=960;Height=720;MinWidth=780;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterOwner;var root=new DockPanel();Content=root;
        // Sidebar: a tinted column with icon + label rows; the page sits on the plain background.
        var side=new Border{Padding=new(12,20,12,12),BorderThickness=new(0,0,1,0),Child=sections};side.SetResourceReference(Border.BackgroundProperty,"Inset");side.SetResourceReference(Border.BorderBrushProperty,"Line");sections.ItemTemplate=SectionTemplate();DockPanel.SetDock(side,Dock.Left);root.Children.Add(side);var column=new Grid();column.ColumnDefinitions.Add(new(){MaxWidth=784});column.ColumnDefinitions.Add(new(){Width=new(0,GridUnitType.Star)});column.Children.Add(page);scroller.Content=column;root.Children.Add(scroller);
        // Each page slides in with a soft sound (both follow Settings › Motion & sound).
        sections.SelectionChanged+=(_,_)=>{ShowSection();scroller.ScrollToTop();Motion.Enter(page,0,12);Sounds.Page();};ShowSection();PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape&&recording is null){Close();e.Handled=true;}};
    }
    private void Save(Preferences value){try{store.Save(value);changed();}catch(Exception error)when(error is IOException or ArgumentException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Settings could not be saved"));}}
    private static readonly Dictionary<string,string> SectionIcons=new(){["General"]="gear-six",["Formats"]="image",["Motion & sound"]="sparkle",["Shortcuts"]="keyboard",["Compatibility"]="arrows-left-right",["Folders"]="folder-open",["Updates"]="arrows-clockwise",["About"]="info"};
    private static DataTemplate SectionTemplate()
    {
        var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);row.SetValue(MarginProperty,new Thickness(4,3,4,3));
        var icon=new FrameworkElementFactory(typeof(ContentPresenter));icon.SetBinding(ContentPresenter.ContentProperty,new System.Windows.Data.Binding{Converter=new SectionIcon()});icon.SetValue(MarginProperty,new Thickness(0,0,12,0));icon.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(icon);
        var label=new FrameworkElementFactory(typeof(TextBlock));var binding=new System.Windows.Data.MultiBinding{Converter=L10n.ChoiceConverter.Shared};binding.Bindings.Add(new System.Windows.Data.Binding());binding.Bindings.Add(new System.Windows.Data.Binding(nameof(LanguageState.Language)){Source=LanguageState.Shared});label.SetBinding(TextBlock.TextProperty,binding);label.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(label);
        return new DataTemplate{VisualTree=row};
    }
    private sealed class SectionIcon:System.Windows.Data.IValueConverter
    {
        public object Convert(object value,Type type,object parameter,System.Globalization.CultureInfo culture)=>Icons.Create(SectionIcons.GetValueOrDefault((string)value,"gear-six"),16);
        public object ConvertBack(object value,Type type,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
    /// Each heading opens a section; the rows that follow share one card.
    private void Heading(string title,string hint){group=null;page.Children.Add(L10n.Label(new TextBlock{Text=title,FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new(0,page.Children.Count==0?0:18,0,0)}));page.Children.Add(Muted(hint,14,new(0,6,0,18)));}
    /// A small label that starts a new card below it.
    private void Caption(string text){group=null;var label=Muted(text,12,new(4,4,0,8));label.FontWeight=FontWeights.SemiBold;page.Children.Add(label);}
    private static TextBlock Muted(string text,double size,Thickness margin){var block=L10n.Label(new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=margin});block.SetResourceReference(TextBlock.ForegroundProperty,"Muted");return block;}
    private StackPanel Group()
    {
        if(group is not null)return group;group=new StackPanel();page.Children.Add(Card(group));return group;
    }
    private static Border Card(UIElement child){var card=new Border{CornerRadius=new(12),BorderThickness=new(1),Padding=new(18,6,18,6),Margin=new(0,0,0,18),Child=child};card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Line");return card;}
    private void Add(UIElement element){if(element is Button button)button.HorizontalAlignment=HorizontalAlignment.Left;Group().Children.Add(element);}
    /// Rows in one card are divided by a hairline.
    private void Row(string title,FrameworkElement control,string? hint=null){if(Group().Children.Count>0)Add(Divider());Add(FormRow(title,control,hint));}
    private static Border Divider(){var line=new Border{Height=1,Margin=new(0,2,0,2)};line.SetResourceReference(Border.BackgroundProperty,"Line");return line;}
    /// A label (and an optional explanation under it) on the left, its control on the right.
    private static DockPanel FormRow(string title,FrameworkElement control,string? hint=null)
    {
        var row=new DockPanel{Margin=new(0,10,0,10)};control.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(control,Dock.Right);row.Children.Add(control);
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,20,0)};text.Children.Add(L10n.Label(new TextBlock{Text=title,TextWrapping=TextWrapping.Wrap}));if(hint is not null)text.Children.Add(Muted(hint,12,new(0,3,0,0)));
        row.Children.Add(text);return row;
    }
    private void Toggle(string title,bool value,Action<bool> save,string? hint=null){var box=new CheckBox{IsChecked=value,Style=(Style)FindResource("Switch")};System.Windows.Automation.AutomationProperties.SetName(box,title);box.Checked+=(_,_)=>save(true);box.Unchecked+=(_,_)=>save(false);Row(title,box,hint);}
    private void Pick(string title,string[] values,string value,Action<string> save,string? hint=null){var combo=new ComboBox{ItemsSource=values,SelectedItem=value,MinWidth=190,Margin=new(0)};System.Windows.Automation.AutomationProperties.SetName(combo,title);combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is string selected)save(selected);};Row(title,combo,hint);}
    private void LanguagePicker()
    {
        var language=new ComboBox{ItemsSource=new[]{"English","Türkçe"},SelectedIndex=store.Value.Language=="tr"?1:0,MinWidth=190,Margin=new(0)};
        System.Windows.Automation.AutomationProperties.SetAutomationId(language,"AppLanguage");
        language.SelectionChanged+=(_,_)=>Save(store.Value with{Language=language.SelectedIndex==1?"tr":"en"});Row("Language",language,"Menus, messages and tool names.");
    }
    private void ShowSection()
    {
        page.Children.Clear();group=null;switch(sections.SelectedItem as string)
        {
            case "General":General();break;
            case "Formats":FormatDefaults();break;
            case "Motion & sound":MotionAndSound();break;
            case "Shortcuts":ShortcutsPage();break;
            case "Compatibility":Compatibility();break;
            case "Folders":Folders();break;
            case "Updates":UpdatesPage();break;
            default:Heading("Nashi","A little pear. A lot of possibility.");Row("Version",new TextBlock{Text=UpdateInstaller.Current.ToString(),FontWeight=FontWeights.SemiBold},"Local by design · MIT source");Add(Divider());var source=MainWindow.Button("Source & third-party notices",()=>MainWindow.OpenUrl("https://github.com/orkunisitmak/nashi-app"));source.Margin=new(0,12,0,12);Add(source);break;
        }
    }
    private void General()
    {
        Heading("Your kind of flow.","Small preferences that shape how Nashi feels every day.");
        Caption("Language and look");LanguagePicker();
        if(appearanceStore is not null)Pick("Appearance",["system","light","dark"],appearanceStore.Appearance,v=>{try{appearanceStore.SetAppearance(v);Theme.Apply(v);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Appearance could not be saved"));}},"Follow Windows, or always stay light or dark.");
        Caption("Dragging and the wheel");
        Toggle("Enable Nashi drag shortcuts",store.Value.Enabled,v=>Save(store.Value with{Enabled=v}),"Drag files from anywhere, then hold a key to open a wheel of choices.");
        var modifiers=new[]{"Ctrl","Shift","Alt","Ctrl+Shift","Ctrl+Alt","Shift+Alt"};
        Pick("Convert · while dragging",modifiers.Where(m=>m!=store.Value.ToolsModifier&&m!=store.Value.ActionsModifier).ToArray(),store.Value.ConvertModifier,v=>Save(store.Value with{ConvertModifier=v}),"Opens the formats a file can become.");
        Pick("Tools · while dragging",modifiers.Where(m=>m!=store.Value.ConvertModifier&&m!=store.Value.ActionsModifier).ToArray(),store.Value.ToolsModifier,v=>Save(store.Value with{ToolsModifier=v}),"Opens crop, resize, compress and the other tools.");
        Pick("File actions · while dragging",modifiers.Where(m=>m!=store.Value.ToolsModifier&&m!=store.Value.ConvertModifier).ToArray(),store.Value.ActionsModifier,v=>Save(store.Value with{ActionsModifier=v}),"Copy, move, share and more.");
        Pick("Wheel finish",["glass","solid"],store.Value.WheelFinish,v=>Save(store.Value with{WheelFinish=v}),"Glass lets a little of what is behind show through.");
        var size=new Slider{Minimum=.85,Maximum=1.4,Value=store.Value.WheelScale,Width=190,ToolTip="Wheel size"};size.ValueChanged+=(_,_)=>Save(store.Value with{WheelScale=size.Value});Row("Wheel size",size,"How large the wheel opens around the pointer.");
        if(appearanceStore is not null)Toggle("Window management · drag a title bar, then Shift or Alt",appearanceStore.WindowManagementEnabled,v=>{appearanceStore.SetWindowManagement(v);changed();},"Snap and arrange any window with the same wheel.");
        Pick("Float behavior",["createNew","addToExisting"],store.Value.FloatBehavior,v=>Save(store.Value with{FloatBehavior=v}),"What happens when more files are dropped on a floating shelf.");
        Caption("Everyday");
        Toggle("Reveal finished files in Explorer",store.Value.RevealFinished,v=>Save(store.Value with{RevealFinished=v}),"Opens the folder with the new file when a job finishes.");
        Toggle("Keep running in the system tray",store.Value.KeepInTray,v=>Save(store.Value with{KeepInTray=v}),"Closing the window keeps shortcuts and downloads ready.");
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){var enabled=key?.GetValue("Nashi") is string;Toggle("Launch at login",enabled,v=>{try{using var run=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(v)run.SetValue("Nashi","\""+Environment.ProcessPath+"\" --background");else run.DeleteValue("Nashi",false);}catch(Exception error){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Launch at login"));}},"Starts quietly in the tray when you sign in.");}
    }
}
