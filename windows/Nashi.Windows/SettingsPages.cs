using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class SettingsWindow
{
    private void MotionAndSound()
    {
        Heading("Motion and sound.","How Nashi moves and sounds while you work. Tune it, or turn it off.");
        Caption("Animation");
        Toggle("Animations",store.Value.Animations,v=>Save(store.Value with{Animations=v}),"Pages slide in, files shake while they work, and new files pop out beside them.");
        var speed=new Slider{Minimum=.5,Maximum=2,Value=store.Value.AnimationSpeed,TickFrequency=.25,IsSnapToTickEnabled=true};AutomationProperties.SetName(speed,"Animation speed");speed.ValueChanged+=(_,_)=>Save(store.Value with{AnimationSpeed=speed.Value});
        Row("Speed",Scale(speed,"Slower","Faster"),"Slow it down to follow along, or speed it up to stay out of the way.");
        // The same shake-then-pop a conversion plays, on two sample tiles.
        Tile(out var before,"image");Tile(out var after,"sparkle");after.Visibility=Visibility.Hidden;after.Margin=new(10,0,14,0);
        var demo=new StackPanel{Orientation=Orientation.Horizontal};demo.Children.Add(before);demo.Children.Add(after);
        demo.Children.Add(MainWindow.Button("Play",()=>{after.Visibility=Visibility.Hidden;Motion.Shake(before);var wait=new System.Windows.Threading.DispatcherTimer{Interval=Motion.On?Motion.Time(480):TimeSpan.FromMilliseconds(150)};wait.Tick+=(_,_)=>{wait.Stop();after.Visibility=Visibility.Visible;Motion.Pop(after);Sounds.Preview("success");};wait.Start();}));
        Row("Try it",demo,"Plays what you see when a file is converted.");
        Caption("Sound");
        Toggle("Sound effects",store.Value.SoundEffects,v=>Save(store.Value with{SoundEffects=v}),"A soft chime when files are ready, and a low note when something fails.");
        Toggle("Page sounds",store.Value.PageSounds,v=>Save(store.Value with{PageSounds=v}),"A quiet tap as you move between pages, a little different each time.");
        Toggle("Wheel click",store.Value.Feedback,v=>Save(store.Value with{Feedback=v}),"A tick as the wheel picks a slice.");
        var volume=new Slider{Minimum=0,Maximum=1,Value=store.Value.SoundVolume,SmallChange=.05,LargeChange=.1};AutomationProperties.SetName(volume,"Volume");volume.ValueChanged+=(_,_)=>Save(store.Value with{SoundVolume=Math.Round(volume.Value,2)});
        Row("Volume",Scale(volume,"Quiet","Loud"),"Applies to every Nashi sound.");
        var listen=new WrapPanel();foreach(var (label,kind) in new[]{("Page","page"),("Done","success"),("Failed","failure"),("Click","click")}){var button=MainWindow.Button(label,()=>Sounds.Preview(kind));button.Margin=new(listen.Children.Count==0?0:8,0,0,0);listen.Children.Add(button);}
        Row("Listen",listen,"Each press plays another variation.");
        Caption("Defaults");
        var reset=MainWindow.Button("",()=>
        {
            var defaults=new Preferences();
            Save(store.Value with{Animations=defaults.Animations,AnimationSpeed=defaults.AnimationSpeed,SoundEffects=defaults.SoundEffects,PageSounds=defaults.PageSounds,Feedback=defaults.Feedback,SoundVolume=defaults.SoundVolume});
            ShowSection();Motion.Enter(page,0,12);
        });
        reset.Content=Icons.Labeled("arrow-counter-clockwise","Restore defaults",14);AutomationProperties.SetName(reset,"Restore defaults");
        Row("Motion and sound defaults",reset,"Animations on at normal speed, every sound on at 60% volume.");
    }
    /// Every keyboard command with the key it answers to. Choose a row's key, then press the new
    /// combination; Esc cancels. A combination another command used moves over to this one.
    private void ShortcutsPage()
    {
        Heading("Your keys, your way.","Pick any key combination for Nashi's commands. Use Ctrl or Alt with a key, or a function key on its own.");
        Caption("Commands");
        foreach(var command in Shortcuts.All)
        {
            var key=new TextBlock{Text=Shortcuts.Display(Shortcuts.Gesture(store.Value,command.Id)),FontFamily=new("Cascadia Mono, Consolas"),FontSize=12.5,FontWeight=FontWeights.SemiBold};
            var chooser=new Button{Content=key,MinWidth=150,Padding=new(12,7,12,7),Margin=new(0),HorizontalContentAlignment=HorizontalAlignment.Center};AutomationProperties.SetName(chooser,"Shortcut for "+command.Title);L10n.Bind(chooser,ToolTipProperty,"Click, then press the new keys");
            var undo=new Button{Style=(Style)FindResource("IconButton"),Content=Icons.Create("arrow-counter-clockwise",14),Width=32,Height=32,Margin=new(8,0,0,0),IsEnabled=store.Value.Shortcuts.ContainsKey(command.Id)};L10n.Bind(undo,ToolTipProperty,"Use the default");AutomationProperties.SetName(undo,"Use the default for "+command.Title);
            undo.Click+=(_,_)=>{var chosen=new Dictionary<string,string>(store.Value.Shortcuts);chosen.Remove(command.Id);Save(store.Value with{Shortcuts=chosen});ShowSection();};
            chooser.Click+=(_,_)=>Record(chooser,key,command);
            var controls=new StackPanel{Orientation=Orientation.Horizontal};controls.Children.Add(chooser);controls.Children.Add(undo);
            Row(command.Title,controls,command.Hint);
        }
        Caption("Defaults");
        var reset=MainWindow.Button("",()=>{Save(store.Value with{Shortcuts=[]});ShowSection();Motion.Enter(page,0,12);});reset.Content=Icons.Labeled("arrow-counter-clockwise","Restore defaults",14);AutomationProperties.SetName(reset,"Restore default shortcuts");reset.IsEnabled=store.Value.Shortcuts.Count>0;
        Row("All shortcuts",reset,"Puts every command back on its original keys.");
    }
    /// Listens for the next key combination for one command.
    private void Record(Button chooser,TextBlock key,Shortcuts.Command command)
    {
        if(recording is not null)return;
        L10n.SetText(key,"Press keys…");chooser.SetResourceReference(Control.BorderBrushProperty,"Gold");chooser.BorderThickness=new(2);
        void Done(){PreviewKeyDown-=recording;recording=null;chooser.LostKeyboardFocus-=Lost;chooser.ClearValue(Control.BorderBrushProperty);chooser.BorderThickness=new(1);key.Text=Shortcuts.Display(Shortcuts.Gesture(store.Value,command.Id));}
        void Lost(object? sender,System.Windows.Input.KeyboardFocusChangedEventArgs e)=>Done();
        recording=(_,e)=>
        {
            var pressed=e.Key==System.Windows.Input.Key.System?e.SystemKey:e.Key==System.Windows.Input.Key.ImeProcessed?e.ImeProcessedKey:e.Key;var modifiers=System.Windows.Input.Keyboard.Modifiers;e.Handled=true;
            if(pressed==System.Windows.Input.Key.Escape&&modifiers==System.Windows.Input.ModifierKeys.None){Done();return;}
            if(Shortcuts.IsModifier(pressed))return;
            if(!Shortcuts.Allowed(pressed,modifiers)){L10n.SetText(key,"Add Ctrl or Alt");return;}
            var text=Shortcuts.Format(pressed,modifiers);var chosen=new Dictionary<string,string>(store.Value.Shortcuts){[command.Id]=text};
            // One combination, one command: whoever had it before lets go.
            foreach(var other in Shortcuts.All.Where(c=>c.Id!=command.Id&&Shortcuts.Gesture(store.Value,c.Id)==text))chosen[other.Id]="";
            Done();Save(store.Value with{Shortcuts=chosen});ShowSection();
        };
        PreviewKeyDown+=recording;chooser.LostKeyboardFocus+=Lost;chooser.Focus();
    }
    private System.Windows.Input.KeyEventHandler? recording;
    internal bool Recording=>recording is not null;
    /// A slider with its two ends named underneath.
    private static StackPanel Scale(Slider slider,string low,string high)
    {
        slider.Width=220;var ends=new DockPanel{Margin=new(2,2,2,0)};var right=Muted(high,11,new(0));DockPanel.SetDock(right,Dock.Right);ends.Children.Add(right);ends.Children.Add(Muted(low,11,new(0)));
        var stack=new StackPanel{Width=220};stack.Children.Add(slider);stack.Children.Add(ends);return stack;
    }
    private static void Tile(out Border tile,string icon){tile=new Border{Width=52,Height=52,CornerRadius=new(10),Child=Icons.Create(icon,22,"Gold"),VerticalAlignment=VerticalAlignment.Center};tile.SetResourceReference(Border.BackgroundProperty,"Sage");}
    private void Folders()
    {
        Heading("A place for fresh files.","Originals are never touched. Choose where the new copies go.");
        Caption("New copies");
        var modes=new UniformGrid{Columns=3,Margin=new(0,10,0,10)};
        foreach(var (mode,icon,title,hint) in new[]{("beside","copy-simple","Next to the original","The same folder as the file you started with."),("ask","folder-open","Ask every time","Pick a folder for each job."),("folder","push-pin","Always one folder","Everything lands in the folder below.")})
        {
            var on=store.Value.OutputMode==mode;var body=new StackPanel();var glyph=Icons.Create(icon,20,on?"Gold":"Ink");glyph.HorizontalAlignment=HorizontalAlignment.Left;glyph.Margin=new(0,0,0,10);body.Children.Add(glyph);
            var name=L10n.Label(new TextBlock{Text=title,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});body.Children.Add(name);body.Children.Add(Muted(hint,12,new(0,4,0,0)));
            var tile=MainWindow.Button("",()=>{if(store.Value.OutputMode==mode)return;Save(store.Value with{OutputMode=mode});ShowSection();});tile.Content=body;tile.HorizontalContentAlignment=HorizontalAlignment.Stretch;tile.VerticalContentAlignment=VerticalAlignment.Top;tile.Padding=new(14,14,14,14);tile.Margin=new(modes.Children.Count==0?0:5,0,modes.Children.Count==2?0:5,0);
            tile.BorderThickness=new(on?2:1);tile.SetResourceReference(Control.BorderBrushProperty,on?"Gold":"Line");tile.SetResourceReference(Control.BackgroundProperty,on?"Sage":"Surface");AutomationProperties.SetName(tile,title);AutomationProperties.SetItemStatus(tile,on?"Selected":"");modes.Children.Add(tile);
        }
        Add(modes);Add(Divider());
        FolderRow("Output folder",store.Value.OutputFolder,"Used when “Always one folder” is on.",()=>{var picker=new OpenFolderDialog{Title="Save new copies here"};if(picker.ShowDialog(this)==true){Save(store.Value with{OutputFolder=picker.FolderName,OutputMode="folder"});ShowSection();}});
        Caption("Downloads");
        FolderRow("Downloads folder",store.Value.DownloadFolder,"Media you download from a link lands here.",()=>{var picker=new OpenFolderDialog{Title="Save downloaded media here"};if(picker.ShowDialog(this)==true){Save(store.Value with{DownloadFolder=picker.FolderName});ShowSection();}});
    }
    /// A folder: its name, the path in a quiet pill, and Change / Open on the right.
    private void FolderRow(string title,string path,string hint,Action choose)
    {
        var row=new DockPanel{Margin=new(0,12,0,12)};var badge=new Border{Width=40,Height=40,CornerRadius=new(10),Child=Icons.Create("folder-open",18,"Gold"),Margin=new(0,0,14,0),VerticalAlignment=VerticalAlignment.Top};badge.SetResourceReference(Border.BackgroundProperty,"Sage");DockPanel.SetDock(badge,Dock.Left);row.Children.Add(badge);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};buttons.Children.Add(MainWindow.Button("Change…",choose));
        var open=new Button{Style=(Style)FindResource("IconButton"),Content=Icons.Create("arrow-square-out",15),Width=34,Height=34,Margin=new(6,0,0,0),IsEnabled=Directory.Exists(path)};open.Click+=(_,_)=>Process.Start(new ProcessStartInfo("explorer.exe","\""+path+"\""){UseShellExecute=false});L10n.Bind(open,ToolTipProperty,"Open in Explorer");AutomationProperties.SetName(open,"Open in Explorer");buttons.Children.Add(open);
        DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,16,0)};text.Children.Add(L10n.Label(new TextBlock{Text=title,FontWeight=FontWeights.SemiBold}));
        var shown=new TextBlock{Text=path.Length>0?path:L10n.Text("Not chosen yet"),FontSize=12,FontFamily=new("Cascadia Mono, Consolas"),TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=path.Length>0?path:null};shown.SetResourceReference(TextBlock.ForegroundProperty,path.Length>0?"Ink":"Muted");
        var pill=new Border{CornerRadius=new(6),Padding=new(8,4,8,4),Margin=new(0,6,0,6),HorizontalAlignment=HorizontalAlignment.Left,Child=shown};pill.SetResourceReference(Border.BackgroundProperty,"Inset");text.Children.Add(pill);text.Children.Add(Muted(hint,12,new(0)));
        row.Children.Add(text);Add(row);
    }
    private static string Kind(string format)=>FeatureCatalog.IsImage("a."+format)?"Images":FeatureCatalog.IsVideo("a."+format)?"Video":FeatureCatalog.IsAudio("a."+format)?"Audio":"Documents";
    private void Compatibility()
    {
        Heading("Everything has a next form.","Every format Nashi opens, and everything it can turn each one into.");
        var all=FeatureCatalog.Conversions.OrderBy(p=>p.Key).ToArray();
        // Two headline numbers, then a search and kind filter over the grouped table.
        var stats=new UniformGrid{Columns=2,Margin=new(0,0,0,16)};foreach(var (number,label) in new[]{(all.Length,"formats it opens"),(all.Sum(p=>p.Value.Length),"ways to convert them")}){var box=new StackPanel{Margin=new(0,0,12,0)};box.Children.Add(new TextBlock{Text=number.ToString(System.Globalization.CultureInfo.CurrentCulture),FontSize=30,FontWeight=FontWeights.SemiBold});box.Children.Add(Muted(label,12,new(0)));stats.Children.Add(box);}
        page.Children.Add(stats);
        var query=new TextBox{BorderThickness=new(0),Background=System.Windows.Media.Brushes.Transparent,VerticalContentAlignment=VerticalAlignment.Center,Padding=new(0),Margin=new(0)};AutomationProperties.SetName(query,"Find a format");
        var placeholder=Muted("Find a format, like heic or mp4",13,new(2,0,0,0));placeholder.IsHitTestVisible=false;placeholder.VerticalAlignment=VerticalAlignment.Center;var field=new Grid();field.Children.Add(placeholder);field.Children.Add(query);
        var glass=Icons.Create("magnifying-glass",16,"Muted");glass.Margin=new(0,0,10,0);var bar=new DockPanel();DockPanel.SetDock(glass,Dock.Left);bar.Children.Add(glass);bar.Children.Add(field);
        var search=new Border{CornerRadius=new(10),BorderThickness=new(1),Padding=new(12,9,12,9),Child=bar};search.SetResourceReference(Border.BackgroundProperty,"Surface");search.SetResourceReference(Border.BorderBrushProperty,"Line");page.Children.Add(search);
        query.IsKeyboardFocusedChanged+=(_,_)=>search.SetResourceReference(Border.BorderBrushProperty,query.IsKeyboardFocused?"Gold":"Line");search.Cursor=System.Windows.Input.Cursors.IBeam;search.MouseLeftButtonDown+=(_,_)=>query.Focus();
        var kinds=new[]{"All","Images","Video","Audio","Documents"};var filter="All";var chips=new WrapPanel{Margin=new(0,12,0,14)};page.Children.Add(chips);var results=new StackPanel();page.Children.Add(results);
        void Render()
        {
            placeholder.Visibility=query.Text.Length==0?Visibility.Visible:Visibility.Collapsed;var term=query.Text.Trim().TrimStart('.').ToLowerInvariant();
            chips.Children.Clear();foreach(var kind in kinds){var on=kind==filter;var chip=MainWindow.Button(kind,()=>{filter=kind;Render();});chip.Padding=new(14,5,14,5);chip.Margin=new(0,0,8,0);chip.BorderThickness=new(on?2:1);chip.SetResourceReference(Control.BorderBrushProperty,on?"Gold":"Line");chip.SetResourceReference(Control.BackgroundProperty,on?"Sage":"Surface");chips.Children.Add(chip);}
            results.Children.Clear();var shown=0;
            foreach(var kind in kinds.Skip(1).Where(k=>filter=="All"||filter==k))
            {
                var rows=all.Where(p=>Kind(p.Key)==kind&&(term.Length==0||p.Key.Contains(term)||p.Value.Any(v=>v.Contains(term)))).ToArray();if(rows.Length==0)continue;shown+=rows.Length;
                var header=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(4,4,0,8)};var icon=Icons.Create(kind switch{"Images"=>"image","Video"=>"film-strip","Audio"=>"music-note",_=>"file-pdf"},14,"Muted");icon.Margin=new(0,0,8,0);header.Children.Add(icon);var title=Muted(kind,12,new(0));title.FontWeight=FontWeights.SemiBold;header.Children.Add(title);header.Children.Add(Muted("  ·  "+rows.Length,12,new(0)));results.Children.Add(header);
                var list=new StackPanel();foreach(var (source,targets) in rows)
                {
                    if(list.Children.Count>0)list.Children.Add(Divider());var row=new DockPanel{Margin=new(0,9,0,9)};
                    var from=Chip(source,true,term.Length>0&&source.Contains(term));from.Width=78;from.VerticalAlignment=VerticalAlignment.Top;DockPanel.SetDock(from,Dock.Left);row.Children.Add(from);
                    var arrow=Icons.Create("arrow-right",14,"Muted");arrow.Margin=new(12,6,12,0);arrow.VerticalAlignment=VerticalAlignment.Top;DockPanel.SetDock(arrow,Dock.Left);row.Children.Add(arrow);
                    var to=new WrapPanel();foreach(var target in targets)to.Children.Add(Chip(target,false,term.Length>0&&target.Contains(term)));row.Children.Add(to);list.Children.Add(row);
                }
                results.Children.Add(Card(list));
            }
            if(shown==0)results.Children.Add(Muted("No format matches that. Try an extension like png, mp3 or pdf.",13,new(4,8,0,0)));
        }
        query.TextChanged+=(_,_)=>Render();Render();
    }
    /// A format name in a small rounded tag; the source is filled, matches get the accent edge.
    private static Border Chip(string format,bool source,bool match)
    {
        var text=new TextBlock{Text=format.ToUpperInvariant(),FontSize=11.5,FontWeight=source?FontWeights.Bold:FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center};text.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
        var chip=new Border{CornerRadius=new(6),Padding=new(8,3,8,3),Margin=new(0,2,6,2),BorderThickness=new(match?1.5:1),Child=text};chip.SetResourceReference(Border.BackgroundProperty,source?"Sage":"Inset");chip.SetResourceReference(Border.BorderBrushProperty,match?"Gold":"Line");return chip;
    }
    private Border? updatePanel;private CancellationTokenSource? installing;
    private void UpdatesPage()
    {
        Heading("Fresh things, on their way.","Updates come from the public Nashi repository and are verified before they install. No account or token needed.");
        Caption("This copy");
        Row("Installed version",new TextBlock{Text=UpdateInstaller.Current.ToString(),FontWeight=FontWeights.SemiBold});
        Toggle("Check for updates automatically",store.Value.AutomaticUpdateChecks,v=>Save(store.Value with{AutomaticUpdateChecks=v}),"Nashi looks once an hour and lets you know on Home.");
        // The space under the settings is where checking and installing play out.
        updatePanel=new Border{CornerRadius=new(12),BorderThickness=new(1),Padding=new(22,20,22,20),MinHeight=168};updatePanel.SetResourceReference(Border.BackgroundProperty,"Surface");updatePanel.SetResourceReference(Border.BorderBrushProperty,"Line");page.Children.Add(updatePanel);
        if(installing is not null)return;
        if(updates?.Known() is {} known)ShowAvailable(known);else if(checkOnOpen){checkOnOpen=false;_=CheckUpdatesAsync();}else ShowIdle();
    }
    /// One state of the update panel: a badge (spinning while busy), what is happening, and what to do next.
    private void UpdateState(string icon,string title,string detail,bool busy,params UIElement[] below)
    {
        if(updatePanel is null)return;var glyph=Icons.Create(icon,24,"Gold");var badge=new Border{Width=52,Height=52,CornerRadius=new(26),Child=glyph,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,18,0)};badge.SetResourceReference(Border.BackgroundProperty,"Sage");
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};text.Children.Add(new TextBlock{Text=title,FontSize=17,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});var note=new TextBlock{Text=detail,TextWrapping=TextWrapping.Wrap,Margin=new(0,4,0,0)};note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");text.Children.Add(note);
        foreach(var element in below){if(element is FrameworkElement framed)framed.Margin=new(0,14,0,0);text.Children.Add(element);}
        var row=new DockPanel();DockPanel.SetDock(badge,Dock.Left);row.Children.Add(badge);row.Children.Add(text);AutomationProperties.SetName(updatePanel,title);updatePanel.Child=row;
        Motion.Enter(row,0,10);if(busy)Motion.Spin(glyph);else Motion.Pop(badge);
    }
    private static StackPanel Buttons(params Button[] buttons){var row=new StackPanel{Orientation=Orientation.Horizontal};foreach(var button in buttons){button.Margin=new(row.Children.Count==0?0:8,0,0,0);row.Children.Add(button);}return row;}
    private void ShowIdle()=>UpdateState("arrows-clockwise",L10n.Text("Check whenever you like."),L10n.Text("Nashi asks the public release feed. Nothing installs until you say so."),false,Buttons(MainWindow.Primary("Check for updates",()=>_=CheckUpdatesAsync())));
    private async Task CheckUpdatesAsync()
    {
        if(updates is null){ShowUpdateFailure(L10n.Text("Updates can be checked from Nashi's main window."),null);return;}
        UpdateState("circle-notch",L10n.Text("Looking for a newer Nashi…"),L10n.Text("This takes a moment."),true,new ProgressBar{IsIndeterminate=true,Height=4,HorizontalAlignment=HorizontalAlignment.Stretch});
        try
        {
            // Keep the check on screen long enough to read, even on a fast connection.
            var check=updates.Check();await Task.WhenAll(check,Task.Delay(900));
            if(check.Result is {} found)ShowAvailable(found);
            else UpdateState("check-circle",L10n.Text("You’re up to date."),L10n.Format("Nashi {0} is the newest version.",UpdateInstaller.Current),false,Buttons(MainWindow.Button("Check again",()=>_=CheckUpdatesAsync())));
        }
        catch(Exception error){ShowUpdateFailure(error.Message,()=>_=CheckUpdatesAsync());}
    }
    private void ShowAvailable(WindowsUpdate found)=>UpdateState("sparkle",L10n.Format("Nashi {0} is ready.",found.Version),L10n.Text("It downloads, gets verified, then Nashi restarts to finish. Your desk and Library stay as they are."),false,Buttons(MainWindow.Primary("Download and install",()=>_=InstallAsync(found))));
    private async Task InstallAsync(WindowsUpdate found)
    {
        if(updates is null||installing is not null)return;installing=new CancellationTokenSource();var token=installing.Token;Closed+=(_,_)=>installing?.Cancel();
        var bar=new ProgressBar{Minimum=0,Maximum=1,IsIndeterminate=true,Height=6,HorizontalAlignment=HorizontalAlignment.Stretch};var stage=new TextBlock{Text=L10n.Text("Starting…"),FontSize=12};stage.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var cancel=MainWindow.Button("Cancel",()=>installing?.Cancel());
        UpdateState("circle-notch",L10n.Format("Installing Nashi {0}…",found.Version),L10n.Text("Keep this window open. Nashi restarts on its own."),true,bar,stage,Buttons(cancel));stage.Margin=new(0,6,0,0);
        try{await updates.Install(found,new Progress<OperationProgress>(p=>{bar.IsIndeterminate=p.Fraction is null;if(p.Fraction is {} f)bar.Value=Math.Clamp(f,0,1);stage.Text=p.Fraction is {} v?L10n.Text(p.Label)+"  ·  "+v.ToString("P0",System.Globalization.CultureInfo.CurrentCulture):L10n.Text(p.Label);}),token);}
        catch(OperationCanceledException){installing=null;ShowAvailable(found);return;}
        catch(Exception error){installing=null;ShowUpdateFailure(error.Message,()=>_=InstallAsync(found));return;}
        installing=null;bar.IsIndeterminate=false;bar.Value=1;
    }
    private void ShowUpdateFailure(string message,Action? retry)=>UpdateState("warning",L10n.Text("That didn’t work this time."),message,false,retry is null?new UIElement[0]:[Buttons(MainWindow.Button("Try again",retry))]);
}
