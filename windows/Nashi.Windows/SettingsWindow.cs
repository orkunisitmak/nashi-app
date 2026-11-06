using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;
using Nashi.Core;
namespace Nashi.Windows;
// A sidebar of sections and grouped rows: the label on the left, its control on the right.
public sealed partial class SettingsWindow:Window
{
    private readonly LibraryStore? appearanceStore;
    private readonly PreferencesStore store;private readonly Action changed;
    private readonly StackPanel page=new(){Margin=new(22,18,28,24),MaxWidth=600,HorizontalAlignment=HorizontalAlignment.Stretch};private Panel target;private StackPanel? group;
    private readonly Grid formats=new();private readonly ScrollViewer scroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    private readonly ListBox sections=new(){ItemsSource=new[]{"General","Formats","Compatibility","Folders","Updates","About"},SelectedIndex=0};
    public SettingsWindow(Window owner,PreferencesStore store,Action changed,Func<Task> update,LibraryStore? appearanceStore=null)
    {
        this.appearanceStore=appearanceStore;target=page;
        this.store=store;this.changed=changed;Owner=owner;Theme.Attach(this,toolbar:true);Title="Settings · Nashi";Width=850;Height=680;MinWidth=740;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid();root.ColumnDefinitions.Add(new(){Width=new(232)});root.ColumnDefinitions.Add(new());Content=root;
        var side=new DockPanel();var top=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(10,14,0,14)};top.Children.Add(Ui.Pear(16));var name=Ui.Text("Settings",12,"Muted");name.Margin=new(8,0,0,0);top.Children.Add(name);DockPanel.SetDock(top,Dock.Top);side.Children.Add(top);
        sections.ItemContainerStyle=Ui.Style("SideItem");sections.ItemTemplate=SectionTemplate();WindowChrome.SetIsHitTestVisibleInChrome(sections,true);side.Children.Add(sections);
        var sidebar=Ui.Card(side,16,new(8,0,8,10));sidebar.Margin=new(9);root.Children.Add(sidebar);
        // Most sections scroll as one page; Formats has its own list column.
        scroll.Content=page;WindowChrome.SetIsHitTestVisibleInChrome(page,true);WindowChrome.SetIsHitTestVisibleInChrome(formats,true);var main=new Grid();main.Children.Add(scroll);main.Children.Add(formats);Grid.SetColumn(main,1);root.Children.Add(main);
        sections.SelectionChanged+=(_,_)=>{ShowSection(update);scroll.ScrollToTop();};ShowSection(update);PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){Close();e.Handled=true;}};
    }
    // Section names stay the raw item strings; the template adds an icon and the localized label.
    private static DataTemplate SectionTemplate()
    {
        var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
        var icon=new FrameworkElementFactory(typeof(ContentControl));icon.SetBinding(ContentControl.ContentProperty,new Binding{Converter=new SectionGlyph()});icon.SetValue(FrameworkElement.WidthProperty,27d);icon.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left);icon.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);icon.SetResourceReference(TextElement.ForegroundProperty,"Muted");row.AppendChild(icon);
        var label=new FrameworkElementFactory(typeof(TextBlock));var text=new MultiBinding{Converter=(IMultiValueConverter)Application.Current.FindResource("ChoiceText")};text.Bindings.Add(new Binding());text.Bindings.Add(new Binding(nameof(LanguageState.Language)){Source=LanguageState.Shared});label.SetBinding(TextBlock.TextProperty,text);label.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(label);
        return new DataTemplate{VisualTree=row};
    }
    private sealed class SectionGlyph:IValueConverter
    {
        public object Convert(object value,Type type,object parameter,CultureInfo culture)=>Icons.Make(value switch{"General"=>"gear","Formats"=>"docs","Compatibility"=>"grid","Folders"=>"folder","Updates"=>"refresh",_=>"info"},17);
        public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
    private void Save(Preferences value){try{store.Save(value);changed();}catch(Exception error)when(error is IOException or ArgumentException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Settings could not be saved"));}}
    private void Heading(string title,string? hint=null)
    {
        group=null;var heading=Ui.Text(title,30,weight:FontWeights.Bold);heading.Margin=new(4,0,4,hint is null?14:4);page.Children.Add(heading);
        if(hint is not null){var text=Ui.Text(hint,12,"Muted");text.Margin=new(4,0,4,16);page.Children.Add(text);}
    }
    private void Group(string? title=null)
    {
        if(title is not null){var label=Ui.Text(title,12,"Muted",FontWeights.SemiBold);label.Margin=new(4,8,4,6);target.Children.Add(label);}
        group=new StackPanel();var box=new Border{CornerRadius=new(16),Padding=new(16,2,16,2),BorderThickness=new(1),Margin=new(0,0,0,12),Child=group};box.SetResourceReference(Border.BackgroundProperty,"Well");box.SetResourceReference(Border.BorderBrushProperty,"Line");target.Children.Add(box);
    }
    private void Row(UIElement content)
    {
        if(group is null)Group();if(group!.Children.Count>0){var line=new Border{Height=1};line.SetResourceReference(Border.BackgroundProperty,"Line");group.Children.Add(line);}group.Children.Add(content);
    }
    private void Row(string title,FrameworkElement control,string? sub=null)
    {
        var row=new SettingRow{MinHeight=46};control.VerticalAlignment=VerticalAlignment.Center;control.Margin=new(0);row.Children.Add(control);
        var label=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,8,16,8)};label.Children.Add(Ui.Text(title));if(sub is not null){var note=Ui.Text(sub,11,"Muted");note.Margin=new(0,2,0,0);label.Children.Add(note);}row.Children.Add(label);Row((UIElement)row);
    }
    // A settings row: the label on the left, its control on the right. When the label would be squeezed below its
    // own width (a long label, a wide segmented control, a narrow window), the control moves under the label instead.
    private sealed class SettingRow:Panel
    {
        private bool stacked;
        protected override Size MeasureOverride(Size available)
        {
            var control=InternalChildren[0];var label=InternalChildren[1];var free=new Size(double.PositiveInfinity,double.PositiveInfinity);control.Measure(free);label.Measure(free);
            var width=double.IsFinite(available.Width)?available.Width:label.DesiredSize.Width+control.DesiredSize.Width;var side=control.DesiredSize.Width;
            stacked=label.DesiredSize.Width+side>width&&width-side<Math.Min(label.DesiredSize.Width,140);
            if(stacked){label.Measure(new Size(width,double.PositiveInfinity));return new(width,label.DesiredSize.Height+control.DesiredSize.Height+10);}
            label.Measure(new Size(Math.Max(0,width-side),double.PositiveInfinity));return new(width,Math.Max(label.DesiredSize.Height,control.DesiredSize.Height));
        }
        protected override Size ArrangeOverride(Size size)
        {
            var control=InternalChildren[0];var label=InternalChildren[1];var c=control.DesiredSize;var l=label.DesiredSize;
            if(stacked){label.Arrange(new Rect(0,0,size.Width,l.Height));control.Arrange(new Rect(0,l.Height,Math.Min(size.Width,c.Width),c.Height));return size;}
            control.Arrange(new Rect(Math.Max(0,size.Width-c.Width),(size.Height-c.Height)/2,c.Width,c.Height));label.Arrange(new Rect(0,(size.Height-l.Height)/2,Math.Max(0,size.Width-c.Width),l.Height));return size;
        }
    }
    private static CheckBox Switch(string title,bool value,Action<bool> save)
    {
        var box=L10n.Content(new CheckBox{Content=title,IsChecked=value,MinHeight=46,Padding=new(0,0,16,0)});box.Checked+=(_,_)=>save(true);box.Unchecked+=(_,_)=>save(false);return box;
    }
    private void Toggle(string title,bool value,Action<bool> save,string? sub=null)
    {
        // With a note, the label and note sit on the left and the bare switch is centered on the right, as in the design.
        if(sub is null){Row(Switch(title,value,save));return;}
        var bare=Switch("",value,save);bare.Padding=new(0);System.Windows.Automation.AutomationProperties.SetName(bare,L10n.Text(title));Row(title,bare,sub);
    }
    private ComboBox Pick(string title,string[] values,string value,Action<string> save,bool segmented=false)
    {
        var combo=new ComboBox{ItemsSource=values,SelectedItem=value,MinWidth=140};if(segmented){combo.Style=Ui.Style("SegmentedCombo");combo.DropDownOpened+=(_,_)=>combo.IsDropDownOpen=false;}combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is string selected)save(selected);};Row(title,combo);return combo;
    }
    private static Button Action(string title,Action action){var button=MainWindow.Button(title,action);button.MinHeight=28;button.FontSize=12;button.Padding=new(12,0,12,0);return button;}
    private void LanguagePicker()
    {
        var language=new ComboBox{ItemsSource=new[]{"English","Türkçe"},SelectedIndex=store.Value.Language=="tr"?1:0,MinWidth=140};
        System.Windows.Automation.AutomationProperties.SetAutomationId(language,"AppLanguage");
        language.SelectionChanged+=(_,_)=>Save(store.Value with{Language=language.SelectedIndex==1?"tr":"en"});Row("Language",language);
    }
    private void ShowSection(Func<Task> update)
    {
        page.Children.Clear();target=page;group=null;var formatsShown=sections.SelectedItem as string=="Formats";formats.Visibility=formatsShown?Visibility.Visible:Visibility.Collapsed;scroll.Visibility=formatsShown?Visibility.Collapsed:Visibility.Visible;switch(sections.SelectedItem as string)
        {
            case "General":
                Heading("General");LanguagePicker();if(appearanceStore is not null)Pick("Appearance",["system","light","dark"],appearanceStore.Appearance,v=>{try{appearanceStore.SetAppearance(v);Theme.Apply(v);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Appearance could not be saved"));}},segmented:true);
                var finish=new Segmented(small:true);finish.Add("Glass",()=>Save(store.Value with{WheelFinish="glass"}));finish.Add("Solid",()=>Save(store.Value with{WheelFinish="solid"}));finish.Select(store.Value.WheelFinish=="solid"?1:0);Row("Wheel style",finish);
                var size=new Slider{Minimum=.85,Maximum=1.4,Value=store.Value.WheelScale,Width=140,ToolTip="Wheel size"};size.ValueChanged+=(_,_)=>Save(store.Value with{WheelScale=size.Value});Row("Wheel size",size);
                Group();using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){var enabled=key?.GetValue("Nashi") is string;Toggle("Launch at login",enabled,v=>{try{using var run=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(v)run.SetValue("Nashi","\""+Environment.ProcessPath+"\" --background");else run.DeleteValue("Nashi",false);}catch(Exception error){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Launch at login"));}});}
                var sound=new StackPanel{Orientation=Orientation.Horizontal};var tryIt=Action("Try",Feedback.Play);tryIt.Margin=new(0,0,10,0);sound.Children.Add(tryIt);var feedback=Switch("",store.Value.Feedback,v=>Save(store.Value with{Feedback=v}));feedback.Padding=new(0);System.Windows.Automation.AutomationProperties.SetName(feedback,L10n.Text("Sound and haptic feedback"));sound.Children.Add(feedback);Row("Sound and haptic feedback",sound,"Follows the system volume.");
                Toggle("Animations",store.Value.Animations,v=>Save(store.Value with{Animations=v}));Toggle("Keep running in the system tray",store.Value.KeepInTray,v=>Save(store.Value with{KeepInTray=v}));
                Group("Drag Shortcuts");var modifiers=new[]{"Ctrl","Shift","Alt","Ctrl+Shift","Ctrl+Alt","Shift+Alt"};Pick("Convert",modifiers.Where(m=>m!=store.Value.ToolsModifier&&m!=store.Value.ActionsModifier).ToArray(),store.Value.ConvertModifier,v=>Save(store.Value with{ConvertModifier=v}));Pick("Tools",modifiers.Where(m=>m!=store.Value.ConvertModifier&&m!=store.Value.ActionsModifier).ToArray(),store.Value.ToolsModifier,v=>Save(store.Value with{ToolsModifier=v}));Pick("Actions",modifiers.Where(m=>m!=store.Value.ToolsModifier&&m!=store.Value.ConvertModifier).ToArray(),store.Value.ActionsModifier,v=>Save(store.Value with{ActionsModifier=v}));
                Toggle("Enable Nashi drag shortcuts",store.Value.Enabled,v=>Save(store.Value with{Enabled=v}));Pick("Float behavior",["createNew","addToExisting"],store.Value.FloatBehavior,v=>Save(store.Value with{FloatBehavior=v}));
                if(appearanceStore is not null){Group("Window Management");Toggle("Arrange windows while dragging",appearanceStore.WindowManagementEnabled,v=>{appearanceStore.SetWindowManagement(v);changed();},"Drag a window by its title bar, then press Shift or Alt and choose a layout.");}
                break;
            case "Formats":
                FormatDefaults();break;
            case "Compatibility":
                Heading("Compatibility","The same conversion and tool catalog as macOS.");foreach(var pair in FeatureCatalog.Conversions.OrderBy(p=>p.Key)){var targets=Ui.Text(string.Join(", ",pair.Value.Select(v=>v.ToUpperInvariant())),12,"Muted",localize:false);targets.MaxWidth=360;targets.TextAlignment=TextAlignment.Right;Row(pair.Key.ToUpperInvariant(),targets);}break;
            case "Folders":
                Heading("Folders","Originals remain safe. Choose where new copies should land.");Pick("Save new copies",["beside","ask","folder"],store.Value.OutputMode,v=>Save(store.Value with{OutputMode=v}));Toggle("Reveal finished files in Explorer",store.Value.RevealFinished,v=>Save(store.Value with{RevealFinished=v}));
                Row("New copies folder",Action("Choose…",()=>{var picker=new OpenFolderDialog{Title=L10n.Text("Save new copies here")};if(picker.ShowDialog(this)==true){Save(store.Value with{OutputFolder=picker.FolderName,OutputMode="folder"});ShowSection(update);}}),string.IsNullOrWhiteSpace(store.Value.OutputFolder)?null:store.Value.OutputFolder);
                Row("Downloads folder",Action("Choose…",()=>{var picker=new OpenFolderDialog{Title=L10n.Text("Save downloaded media here")};if(picker.ShowDialog(this)==true){Save(store.Value with{DownloadFolder=picker.FolderName});ShowSection(update);}}),store.Value.DownloadFolder);break;
            case "Updates":
                Heading("Updates","Updates are downloaded from the public Nashi repository and verified before installation. No GitHub account or token is required.");Toggle("Check for updates automatically",store.Value.AutomaticUpdateChecks,v=>Save(store.Value with{AutomaticUpdateChecks=v}));Row("Version "+UpdateInstaller.Current,Action("Check for updates",async()=>await update()));break;
            default:
                // About, as in the design: the icon, the name, the version, one line about Nashi, three links.
                var about=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,20,0,0)};about.Children.Add(new Image{Source=BitmapFrame.Create(new Uri("pack://application:,,,/Assets/NashiIcon.png")),Width=148,Height=148,Margin=new(0,-6,0,-2)});
                var name=Ui.Text("Nashi",30,weight:FontWeights.Bold,localize:false);name.HorizontalAlignment=HorizontalAlignment.Center;about.Children.Add(name);
                var version=Ui.Text("Version "+UpdateInstaller.Current,12,"Muted");version.HorizontalAlignment=HorizontalAlignment.Center;version.Margin=new(0,4,0,0);about.Children.Add(version);
                var line=Ui.Text("Convert, edit and send files. All on your computer.",13);line.TextAlignment=TextAlignment.Center;line.HorizontalAlignment=HorizontalAlignment.Center;line.MaxWidth=340;line.Margin=new(0,10,0,0);about.Children.Add(line);
                var links=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,18,0,0)};
                foreach(var (label,url) in new[]{("Release Notes","https://github.com/orkunisitmak/nashi-app/releases"),("Licenses","https://github.com/orkunisitmak/nashi-app/blob/main/docs/dependencies.md"),("GitHub","https://github.com/orkunisitmak/nashi-app")}){var link=MainWindow.Button(label,()=>MainWindow.OpenUrl(url));link.Margin=new(4,0,4,0);links.Children.Add(link);}
                about.Children.Add(links);var license=Ui.Text("MIT · © 2026",11,"Faint",localize:false);license.HorizontalAlignment=HorizontalAlignment.Center;license.Margin=new(0,22,0,0);about.Children.Add(license);page.Children.Add(about);break;
        }
    }
}
