using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nashi.Core;
namespace Nashi.Linux.UI;

/// Settings in three tabs: General, Appearance (live theme preview), and Shortcuts & gestures
/// (record keys by pressing them, see conflicts with other apps, pick a wheel per direction).
public sealed class SettingsWindow:Window
{
    private readonly MainWindow main;private readonly string originalTheme;private string theme;private bool saved;
    private readonly Dictionary<string,ShortcutBox> boxes=[];private readonly TextBlock problem=new(){Foreground=Brushes.IndianRed,TextWrapping=TextWrapping.Wrap};
    public SettingsWindow(MainWindow main)
    {
        this.main=main;originalTheme=theme=main.LinuxSettings.Theme;
        Title=L10n.T("Settings")+" · Nashi";Width=680;Height=720;MinWidth=560;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var p=main.preferences.Value;var linux=main.LinuxSettings;

        // General
        var language=Ui.Choice([("en","English"),("tr","Türkçe")],p.Language);
        var output=Ui.Choice([("beside","Beside the original"),("folder","In a folder"),("ask","Ask every time")],p.OutputMode);var folder=new TextBox{Text=p.OutputFolder,PlaceholderText=L10n.T("Choose a folder")};
        var downloads=new TextBox{Text=string.IsNullOrWhiteSpace(p.DownloadFolder)||p.DownloadFolder==new Preferences().DownloadFolder&&!Directory.Exists(p.DownloadFolder)?AppPaths.Downloads:p.DownloadFolder};
        var reveal=new CheckBox{Content=L10n.T("Show new files in the file manager when finished"),IsChecked=p.RevealFinished};
        var floating=Ui.Choice([("createNew","Open a new Float"),("addToExisting","Add to the open Float")],p.FloatBehavior);
        var background=new CheckBox{Content=L10n.T("Keep Nashi running in the tray when the window is closed"),IsChecked=linux.RunInBackground};
        var login=new CheckBox{Content=L10n.T("Start Nashi in the tray when I log in"),IsChecked=linux.StartAtLogin};
        Control Browse(TextBox box)=>new DockPanel{Children={Ui.Button("Choose…",async()=>{if(await Dialogs.Folder(this,"Choose a folder") is {} chosen)box.Text=chosen;},"chip").Also(b=>{DockPanel.SetDock(b,Dock.Right);b.Margin=new(8,0,0,0);}),box}};
        var general=Ui.Stack(2,Ui.Field("Language",language),Ui.Field("Save new files",output),Ui.Field("Output folder",Browse(folder)),Ui.Field("Save downloaded media to…",Browse(downloads)),Ui.Field("Float",floating),reveal,
            Ui.Text("Background",13,"title").Also(t=>t.Margin=new(0,16,0,4)),background,login,
            Ui.Text("The tray keeps system-wide shortcuts and drag gestures ready while the window is closed.",11,"muted"),
            Ui.Text(L10n.T("Library and settings:")+" "+main.library.DirectoryPath,11,"muted",false).Also(t=>t.Margin=new(0,14,0,0)));

        // Appearance
        var appearance=Ui.Choice([("system","System"),("light","Light"),("dark","Dark")],main.library.Appearance);
        appearance.SelectionChanged+=(_,_)=>App.ApplyAppearance(Ui.Value(appearance));
        var cards=new WrapPanel();
        foreach(var (id,name,blurb) in new[]{("classic","Classic","The pear palette shared with macOS and Windows."),("glass","Glass","Clear panes; your desktop shows through."),("frosted","Frosted glass","The desktop behind Nashi is blurred, like acrylic."),("brutalist","Brutalist","Square, heavy outlines, hard shadows, loud yellow.")})
        {
            var card=ThemeCard(id,name,blurb);card.PointerPressed+=(_,_)=>{theme=id;App.ApplyTheme(id);foreach(var c in cards.Children.OfType<Border>())c.Classes.Set("chosen",(string?)c.Tag==id);};
            card.Classes.Set("chosen",id==theme);cards.Children.Add(card);
        }
        var look=Ui.Stack(2,Ui.Field("Appearance",appearance),Ui.Text("Theme",13,"title").Also(t=>t.Margin=new(0,10,0,6)),cards,
            Ui.Text("Glass and Frosted glass need a compositor (KDE Plasma, GNOME and most desktops have one). Frosted glass uses KDE's blur; elsewhere it looks like Glass.",11,"muted").Also(t=>t.Margin=new(0,8,0,0)));

        // Shortcuts & gestures
        var enabled=new CheckBox{Content=L10n.T("Use system-wide shortcuts (work in every app, also while dragging)"),IsChecked=linux.GlobalShortcuts};
        var list=new StackPanel{Spacing=8};
        foreach(var (id,title,fallback) in LinuxSettings.Actions)
        {
            var box=new ShortcutBox(linux.Shortcuts.GetValueOrDefault(id,""),main);boxes[id]=box;
            var note=new TextBlock{FontSize=11,Foreground=Brushes.IndianRed,TextWrapping=TextWrapping.Wrap,Text=main.ShortcutProblems.GetValueOrDefault(id,""),IsVisible=main.ShortcutProblems.ContainsKey(id)};
            box.Changed+=async combo=>
            {
                var other=boxes.FirstOrDefault(b=>b.Key!=id&&b.Value.Combo==combo&&combo.Length>0).Key;
                var owner=other is null&&combo.Length>0?await main.ShortcutOwnerAsync(combo):null;
                note.Text=other is not null?L10n.T("Nashi already uses these keys for")+" "+L10n.T(LinuxSettings.Actions.First(a=>a.Id==other).Title):owner is not null?L10n.T("Already used by")+" "+owner:"";note.IsVisible=note.Text.Length>0;
            };
            var reset=Ui.Button("↺",()=>box.Set(fallback),"chip",tip:"Default: "+fallback);var clear=Ui.Button("✕",()=>box.Set(""),"chip",tip:"No shortcut");
            var row=new Grid{ColumnDefinitions=new("*,170,Auto,Auto")};
            row.Children.Add(Ui.Text(title,13).Also(t=>t.VerticalAlignment=VerticalAlignment.Center));
            Grid.SetColumn(box,1);row.Children.Add(box);Grid.SetColumn(reset,2);reset.Margin=new(8,0,4,0);row.Children.Add(reset);Grid.SetColumn(clear,3);clear.Margin=new(0);row.Children.Add(clear);
            list.Children.Add(Ui.Stack(2,row,note));
        }
        var directions=new Grid{ColumnDefinitions=new("Auto,*"),RowDefinitions=new("Auto,Auto,Auto,Auto")};var pickers=new Dictionary<string,ComboBox>();
        var labels=new Dictionary<string,string>{["left"]="← Left",["right"]="→ Right",["up"]="↑ Up",["down"]="↓ Down"};
        for(var n=0;n<LinuxSettings.Directions.Length;n++)
        {
            var d=LinuxSettings.Directions[n];var picker=Ui.Choice([("actions","File actions"),("convert","Convert"),("tools","Tools"),("none","Nothing (cancel)")],linux.Gestures[d]);pickers[d]=picker;picker.Margin=new(12,0,0,8);
            var label=Ui.Text(labels[d],13).Also(t=>{t.VerticalAlignment=VerticalAlignment.Center;t.Margin=new(0,0,0,8);});Grid.SetRow(label,n);Grid.SetRow(picker,n);Grid.SetColumn(picker,1);directions.Children.Add(label);directions.Children.Add(picker);
        }
        var available=main.ShortcutsAvailable||!linux.GlobalShortcuts?null:Ui.Text("System-wide shortcuts need KDE Plasma (kglobalaccel). On other desktops, use Ctrl+Space and drop modifiers inside Nashi.",12,"muted");
        var quickRun=new CheckBox{Content=L10n.T("Conversions and simple tools chosen by gesture run at once with their saved defaults"),IsChecked=linux.GestureQuickRun};
        var keys=Ui.Stack(4,enabled,available??new Control(),Ui.Text("Click a shortcut, then press the new keys. Esc cancels, Backspace clears. Each needs at least one of Meta, Ctrl, Alt or Shift.",11,"muted").Also(t=>t.Margin=new(0,4,0,8)),list,
            Ui.Text("Gesture directions",13,"title").Also(t=>t.Margin=new(0,18,0,4)),
            Ui.Text("While dragging files anywhere, press the gesture shortcut, then move the pointer. The direction picks the wheel; drop on a slice to run it.",11,"muted").Also(t=>t.Margin=new(0,0,0,8)),directions,quickRun);

        var tabs=new TabControl{Items=
        {
            new TabItem{Header=L10n.T("General"),Content=Scroll(general)},
            new TabItem{Header=L10n.T("Appearance"),Content=Scroll(look)},
            new TabItem{Header=L10n.T("Shortcuts & gestures"),Content=Scroll(keys)},
        }};
        var save=Ui.Primary("Save",()=>
        {
            try
            {
                var mode=Ui.Value(output);if(mode=="folder"&&!Directory.Exists(folder.Text))throw new ArgumentException("Choose an existing output folder.");
                main.preferences.Save(p with{Language=Ui.Value(language),OutputMode=mode,OutputFolder=folder.Text??"",DownloadFolder=downloads.Text??AppPaths.Downloads,RevealFinished=reveal.IsChecked==true,FloatBehavior=Ui.Value(floating)});
                main.library.SetAppearance(Ui.Value(appearance));
                var combos=boxes.ToDictionary(b=>b.Key,b=>b.Value.Combo);
                var duplicate=combos.Where(c=>c.Value.Length>0).GroupBy(c=>c.Value).FirstOrDefault(g=>g.Count()>1);if(duplicate is not null)throw new ArgumentException(L10n.T("Two Nashi actions use the same keys:")+" "+duplicate.Key);
                var next=linux with{GlobalShortcuts=enabled.IsChecked==true,Shortcuts=combos,Gestures=pickers.ToDictionary(x=>x.Key,x=>Ui.Value(x.Value)),Theme=theme,RunInBackground=background.IsChecked==true,GestureQuickRun=quickRun.IsChecked==true,StartAtLogin=login.IsChecked==true};
                main.SaveLinuxSettings(next);App.SetBackground(main,next.RunInBackground);saved=true;Close(true);
            }
            catch(Exception error)when(error is ArgumentException or IOException or UnauthorizedAccessException){problem.Text=L10n.T(error.Message);}
        });
        save.IsDefault=true;var cancel=Ui.Button("Cancel",()=>Close(false));cancel.IsCancel=true;
        var footer=Ui.Stack(6,problem,Ui.Row(0,save,cancel));footer.Margin=new(24,8,24,18);DockPanel.SetDock(footer,Dock.Bottom);
        var header=Ui.Title("Settings",24);header.Margin=new(24,20,24,6);DockPanel.SetDock(header,Dock.Top);
        tabs.Margin=new(16,0,16,0);
        Content=new DockPanel{Children={header,footer,tabs}};
        // Cancel or the close button undoes the live theme and appearance preview.
        Closed+=(_,_)=>{if(!saved){App.ApplyTheme(originalTheme);App.ApplyAppearance(main.library.Appearance);}};
    }
    private static ScrollViewer Scroll(Control content)=>new(){Content=new Border{Padding=new(8,14,8,14),Child=content},HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
    /// A small sample of each theme, drawn with its own colours so it reads before it is applied.
    private static Border ThemeCard(string id,string name,string blurb)
    {
        (string Paper,string Card,string Ink,string Accent,double Radius,double Edge,string EdgeColor) look=id switch
        {
            "glass"=>("#8099B4A0","#55FFFFFF","#17261B","#E3B859",12,1,"#B3FFFFFF"),
            "frosted"=>("#B3D9E2D6","#A6FFFFFF","#17261B","#E3B859",12,1,"#B3FFFFFF"),
            "brutalist"=>("#F2EEE3","#FFFFFF","#0A0A0A","#FFE14D",0,3,"#0A0A0A"),
            _=>("#F9F6ED","#FFFEF7","#243B2B","#E3B859",10,1,"#DCDDD1")
        };
        var sample=new Border{Background=new SolidColorBrush(Color.Parse(look.Paper)),Height=86,Padding=new(10),CornerRadius=new(look.Radius),Child=new Border
        {
            Background=new SolidColorBrush(Color.Parse(look.Card)),BorderBrush=new SolidColorBrush(Color.Parse(look.EdgeColor)),BorderThickness=new(look.Edge),CornerRadius=new(look.Radius),Padding=new(8),
            BoxShadow=id=="brutalist"?BoxShadows.Parse("4 4 0 0 #0A0A0A"):default,
            Child=new StackPanel{Spacing=6,Children={new TextBlock{Text="Nashi",FontWeight=FontWeight.Bold,Foreground=new SolidColorBrush(Color.Parse(look.Ink))},new Border{Width=58,Height=16,HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new(look.Radius/2),Background=new SolidColorBrush(Color.Parse(look.Accent)),BorderBrush=new SolidColorBrush(Color.Parse(look.Ink)),BorderThickness=new(id=="brutalist"?2:0)}}}
        }};
        if(id is "glass" or "frosted")sample.Background=new LinearGradientBrush{StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(1,1,RelativeUnit.Relative),GradientStops={new GradientStop(Color.Parse("#6D8FD6"),0),new GradientStop(Color.Parse(id=="frosted"?"#C9A0DC":"#E3B859"),1)}};
        return new Border{Tag=id,Classes={"card","theme-card"},Width=148,Margin=new(0,0,10,10),Padding=new(8),Cursor=new Cursor(StandardCursorType.Hand),Child=Ui.Stack(6,sample,Ui.Text(name,13,"title"),Ui.Text(blurb,11,"muted"))};
    }
}

/// Records a key combination by pressing it. Esc cancels, Backspace or Delete clears.
public sealed class ShortcutBox:Button
{
    public string Combo{get;private set;}
    public event Action<string>? Changed;
    private bool recording;
    public ShortcutBox(string combo,MainWindow main)
    {
        Combo=combo;HorizontalAlignment=HorizontalAlignment.Stretch;HorizontalContentAlignment=HorizontalAlignment.Center;Margin=new(0);Show();
        Click+=(_,_)=>{recording=true;Content=L10n.T("Press keys…");Classes.Add("primary");Focus();};
        LostFocus+=(_,_)=>{if(recording){recording=false;Show();}};
        AddHandler(KeyDownEvent,(_,e)=>
        {
            if(!recording)return;e.Handled=true;
            if(e.Key==Key.Escape){recording=false;Show();return;}
            if(e.Key is Key.Back or Key.Delete&&e.KeyModifiers==KeyModifiers.None){recording=false;Set("");return;}
            if(KeyCombo.FromAvalonia(e.Key,e.KeyModifiers) is {} combo){recording=false;Set(combo);}
        },Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    public void Set(string combo){Combo=combo;Show();Changed?.Invoke(combo);}
    private void Show(){Classes.Remove("primary");Content=Combo.Length==0?L10n.T("None"):Combo.Replace("+"," + ");FontFamily=Combo.Length==0?FontFamily.Default:new FontFamily("monospace");}
}
