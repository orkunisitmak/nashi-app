using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
namespace Nashi.Linux.UI;

/// Wayland never shows an app another app's drag. A global shortcut pressed mid-drag opens this
/// see-through layer on every screen; the drag that is already in progress then passes over it,
/// so Nashi finally sees the pointer and the files. The wheel opens right under the pointer; pressing
/// the shortcut again (or resting on a tab of the switcher below the wheel) moves to the next wheel:
/// Tools, Convert, File actions. Dropping on a petal runs it. Without a drag, the wheel works by
/// clicking (used for copied files).
public sealed class GestureWindow:Window
{
    /// The wheels the shortcut steps through, in order.
    internal static readonly (string Id,string Title,string Symbol)[] Order=[("tools","Tools","⚙"),("convert","Convert","⇄"),("actions","File actions","▱")];
    private static double WheelSize=>380*PetalWheel.Scale;
    private readonly MainWindow main;private string startMode;private readonly Action closeAll;private readonly LinuxSettings settings;
    private readonly Canvas canvas=new();private readonly Border hint;private readonly TextBlock hintText=new(){FontSize=15,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=560};
    private readonly PetalWheel wheel=new(WheelSize){IsVisible=false};
    private Point? origin;private string? mode;private string[]? files;private string? url;private bool clickMode;
    private readonly DispatcherTimer dwell=new(){Interval=TimeSpan.FromMilliseconds(350)};private string? pending;
    // The switcher: one tab per wheel under the wheel, the open one filled. Resting on a tab during a drag opens it.
    private readonly Border switcher;private readonly Dictionary<string,Border> tabs=[];private readonly TextBlock keyHint=new(){Classes={"keyhint"},VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center};
    private readonly DispatcherTimer tabDwell=new(){Interval=TimeSpan.FromMilliseconds(260)};private string? tabPending;
    // A drag that leaves the layer and does not come back (Esc, or a drop the compositor cancelled) closes it;
    // the short wait covers moving onto the wheel or across to another monitor's layer.
    private readonly DispatcherTimer gone=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly DispatcherTimer idle=new(){Interval=TimeSpan.FromSeconds(9)};private readonly DispatcherTimer nudge=new(){Interval=TimeSpan.FromSeconds(2.5)};
    private readonly DispatcherTimer noteClose=new(){Interval=TimeSpan.FromSeconds(2.2)};
    // Every timer stops with the window: a late tick from a closed layer would otherwise close (closeAll)
    // or hide the overlay of the next gesture.
    private bool closed;
    public PetalWheel Wheel=>wheel;
    /// The monitor this layer covers, in X pixels.
    internal PixelRect Area{get;}

    /// NASHI_DEBUG_GESTURE=1 writes what the layer receives to ~/.local/share/Nashi/gesture.log.
    private static readonly bool Debug=Environment.GetEnvironmentVariable("NASHI_DEBUG_GESTURE")=="1";
    internal static void Log(string text){if(!Debug)return;try{Directory.CreateDirectory(AppPaths.Data);File.AppendAllText(Path.Combine(AppPaths.Data,"gesture.log"),$"{DateTime.Now:HH:mm:ss.fff} {text}\n");}catch(IOException){}}

    public GestureWindow(MainWindow main,Screen screen,string? fixedMode,Action closeAll,string[]? presetFiles=null)
    {
        this.main=main;settings=main.LinuxSettings;startMode=fixedMode??settings.FirstWheel;this.closeAll=closeAll;Area=screen.Bounds;
        Title="Nashi gesture";WindowDecorations=WindowDecorations.None;Topmost=true;ShowInTaskbar=false;CanResize=false;
        TransparencyLevelHint=[WindowTransparencyLevel.Transparent];Background=Brushes.Transparent;
        WindowStartupLocation=WindowStartupLocation.Manual;Position=screen.Bounds.Position;Width=screen.Bounds.Width/screen.Scaling;Height=screen.Bounds.Height/screen.Scaling;
        // A faint veil: it shows the layer is listening, and a non-null background is needed for drops.
        // No dimming: the layer stays invisible. An almost transparent fill keeps it hit-testable for drops.
        canvas.Background=new SolidColorBrush(Color.FromArgb(1,0,0,0));Content=canvas;
        hint=new Border{Classes={"card","gesture-hint"},Padding=new(22,14),Child=hintText,IsVisible=false};canvas.Children.Add(wheel);canvas.Children.Add(hint);
        var row=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=2};
        foreach(var (id,title,symbol) in Order)
        {
            var tab=new Border{Classes={"gesture-tab"},Child=new TextBlock{Text=symbol+"  "+L10n.T(title)}};tabs[id]=tab;row.Children.Add(tab);
            tab.PointerPressed+=(_,e)=>{SwitchTo(id);e.Handled=true;};
        }
        var gesture=settings.Shortcuts.GetValueOrDefault("gesture","");keyHint.Text=gesture.Length>0?gesture+"  →":"";keyHint.Margin=new(10,0,8,0);row.Children.Add(keyHint);
        switcher=new Border{Classes={"gesture-switch"},Child=row,IsVisible=false};canvas.Children.Add(switcher);
        tabDwell.Tick+=(_,_)=>{tabDwell.Stop();if(tabPending is {} next&&next!=mode)ShowWheel(next);};
        wheel.Chosen+=async choice=>{if(clickMode)await Choose(choice);};
        DragDrop.SetAllowDrop(canvas,true);
        canvas.AddHandler(DragDrop.DragEnterEvent,OnDrag);canvas.AddHandler(DragDrop.DragOverEvent,OnDrag);canvas.AddHandler(DragDrop.DropEvent,OnDrop);
        canvas.PointerPressed+=(_,e)=>{if(!clickMode||e.Source==canvas)closeAll();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape)closeAll();else if(clickMode&&e.Key==Key.Enter)wheel.Choose();};
        Closed+=(_,_)=>{closed=true;foreach(var timer in new[]{idle,nudge,gone,dwell,tabDwell,noteClose})timer.Stop();};
        noteClose.Tick+=(_,_)=>{noteClose.Stop();if(!closed)closeAll();};
        idle.Tick+=(_,_)=>{idle.Stop();if(!closed&&origin is null&&!clickMode)closeAll();};
        gone.Tick+=(_,_)=>LeaveNow();
        canvas.AddHandler(DragDrop.DragLeaveEvent,(_,_)=>DragLeft());
        // The hot edge opens a wheel of the three wheels; resting on one for a moment opens it.
        dwell.Tick+=(_,_)=>{dwell.Stop();if(mode=="modes"&&pending is {} next&&wheel.SelectedItem?.Id==next)ShowWheel(next[5..]);};
        // Some sessions don't pass a drag from Wayland apps to this layer (KWin's XWayland bridge can get
        // stuck until the next login). Say so, and offer the copy-and-shortcut route that always works.
        // No drag after 2.5 s: drop the native overlay so clicks reach this layer again (a click closes it).
        nudge.Tick+=(_,_)=>{nudge.Stop();if(!closed&&origin is null&&!clickMode){main.HideNativeOverlay();var paste=settings.Shortcuts.GetValueOrDefault("clipboard","");SetHint(L10n.T("The drag didn't reach Nashi.")+"\n"+L10n.T("Drop the files, press Ctrl+C on them, then")+" "+(paste.Length>0?paste:"Ctrl+V")+".");}};
        Opened+=(_,_)=>
        {
            Log($"layer opened on {screen.Bounds} mode={startMode}");
            // With a drag, nothing shows until it arrives: the wheel then opens under the pointer.
            if(presetFiles is {Length:>0}){files=presetFiles;origin=new(Width/2,Height/2);clickMode=true;ShowWheel(startMode);}
            else{idle.Start();nudge.Start();}
            Activate();
        };
    }
    /// The shortcut again: the next wheel that has something for these files.
    internal void NextWheel()
    {
        var current=mode is null||mode=="modes"?startMode:mode;var ids=Order.Select(o=>o.Id).ToList();var at=ids.IndexOf(current);
        for(var step=1;step<=ids.Count;step++){var next=ids[(at+step+ids.Count)%ids.Count];if(Fits(next)){SwitchTo(next);return;}}
    }
    /// Opens this wheel now, or once the drag arrives.
    internal void SwitchTo(string id)
    {
        Log("switch to "+id);
        if(origin is null){startMode=id;return;}
        ShowWheel(id);
    }
    internal string? Mode=>mode;
    /// A drag has reached this layer.
    internal bool HasDrag=>origin is not null&&!clickMode;
    /// No drag: the wheel for the selected files, clicked, where the pointer is (screen pixels).
    internal void UseSelection(string[] paths,PixelPoint? at)
    {
        idle.Stop();nudge.Stop();files=paths;clickMode=true;origin=at is {} p?this.PointToClient(p):new Point(Width/2,Height/2);
        var ids=Order.Select(o=>o.Id).ToList();var first=Enumerable.Range(0,ids.Count).Select(i=>ids[(ids.IndexOf(startMode)+i)%ids.Count]).FirstOrDefault(Fits)??startMode;
        ShowWheel(first);Activate();
    }
    /// A short note near the pointer, then the layer goes.
    internal void ShowNote(string text,PixelPoint? at)
    {
        idle.Stop();nudge.Stop();SetHint(text,at is {} p?this.PointToClient(p)-new Point(0,WheelSize/2+30):null);
        noteClose.Start();
    }
    private bool Fits(string id)=>files is null||main.ChoicesFor(files,id).Count>0;
    private void HideHint()=>hint.IsVisible=false;
    private void SetHint(string text,Point? near=null)
    {
        hint.IsVisible=true;hintText.Text=text;hint.Measure(Size.Infinity);var size=hint.DesiredSize;
        var at=near??new Point(Width/2,Height/2);
        Canvas.SetLeft(hint,Math.Clamp(at.X-size.Width/2,12,Math.Max(12,Width-size.Width-12)));
        Canvas.SetTop(hint,Math.Clamp(near is null?at.Y-size.Height/2:at.Y+WheelSize/2+14,12,Math.Max(12,Height-size.Height-12)));
    }
    private string[]? Files(DragEventArgs e)=>e.DataTransfer.TryGetFiles()?.Select(f=>f.TryGetLocalPath()).OfType<string>().ToArray() is {Length:>0} paths?paths:null;
    /// A dragged web link (browser address bar, link or image), when no files come with the drag.
    private static string? Url(DragEventArgs e)
    {
        string? text=e.DataTransfer.TryGetText();
        if(text is null&&e.DataTransfer.TryGetValue(ClipboardFiles.UriList) is {} bytes)text=System.Text.Encoding.UTF8.GetString(bytes);
        var first=text?.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).FirstOrDefault(l=>!l.StartsWith('#'));
        return first is not null&&Uri.TryCreate(first,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https"?uri.AbsoluteUri:null;
    }
    private Point WheelOrigin=>new(Canvas.GetLeft(wheel),Canvas.GetTop(wheel));

    private void OnDrag(object? sender,DragEventArgs e)
    {
        e.DragEffects=DragDropEffects.Copy;var point=e.GetPosition(canvas);
        if(origin is null||mode is null)Log($"{e.RoutedEvent?.Name} at {point:F0} files={(files??Files(e))?.Length.ToString()??"not yet"} modifiers={e.KeyModifiers}");
        // Some toolkits only hand over the files on drop; the wheel then finishes after the drop.
        files??=Files(e);if(files is null)url??=Url(e);
        Track(point);
    }
    private void OnDrop(object? sender,DragEventArgs e)
    {
        files=Files(e)??files;if(files is null)url??=Url(e);Release(e.GetPosition(canvas));
    }
    /// The same drag, delivered straight from Wayland (see WaylandDropTarget), in screen pixels.
    internal void ExternalDrag(PixelPoint screen)
    {
        var point=this.PointToClient(screen);
        if(origin is null)Log($"Wayland enter at {point:F0}");Track(point);
    }
    /// Files (or a link) from a Wayland drag that is still in progress.
    internal void ExternalOffer(string[] uris)
    {
        var paths=uris.Where(u=>u.StartsWith("file://",StringComparison.Ordinal)&&Uri.TryCreate(u,UriKind.Absolute,out _)).Select(u=>new Uri(u).LocalPath).Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
        if(paths.Length>0)files??=paths;else url??=uris.FirstOrDefault(u=>u.StartsWith("http",StringComparison.OrdinalIgnoreCase));
        Log($"Wayland offer: {paths.Length} file(s){(url is null?"":", link")}");
        if(mode is not null&&wheel.Items.Any(i=>i.Id.StartsWith("placeholder:",StringComparison.Ordinal)))ShowWheel(mode);
    }
    internal void ExternalDrop(string[] uris,PixelPoint screen)
    {
        var paths=uris.Where(u=>u.StartsWith("file://",StringComparison.Ordinal)&&Uri.TryCreate(u,UriKind.Absolute,out _)).Select(u=>new Uri(u).LocalPath).Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
        if(paths.Length>0)files=paths;else url??=uris.FirstOrDefault(u=>u.StartsWith("http",StringComparison.OrdinalIgnoreCase));
        Log($"Wayland drop: {uris.Length} uri(s), {paths.Length} file(s)");
        // Wayland gives the files only now; an open wheel of placeholders becomes the real one to click.
        Release(this.PointToClient(screen));
    }
    /// The drag went off the layer (Wayland leave, or an X11 drag leave).
    internal void DragLeft(){if(!clickMode&&origin is not null)gone.Start();}
    internal bool LeavePending=>gone.IsEnabled;
    /// A drag reached another monitor's layer: this one stops waiting for it.
    internal void Quiet(){idle.Stop();nudge.Stop();}
    internal void LeaveNow(){gone.Stop();if(!closed&&!clickMode){Log("drag left the layer, closing");closeAll();}}
    private void Track(Point point)
    {
        idle.Stop();nudge.Stop();gone.Stop();
        if(origin is null){origin=point;main.DragArrived();ShowWheel(startMode);return;}
        if(wheel.IsVisible)wheel.Selected=wheel.HitTest(point-WheelOrigin);
        // Over a tab of the switcher: rest a moment to open that wheel.
        var over=switcher.IsVisible?tabs.FirstOrDefault(t=>t.Value.IsVisible&&Contains(t.Value,point)).Key:null;
        if(over!=tabPending){tabPending=over;tabDwell.Stop();if(over is not null&&over!=mode)tabDwell.Start();}
        if(mode=="modes"){var id=wheel.SelectedItem?.Id;if(id!=pending){pending=id;dwell.Stop();if(id is not null)dwell.Start();}}
    }
    private bool Contains(Control control,Point point)=>control.TranslatePoint(new Point(0,0),canvas) is {} at&&new Rect(at,control.Bounds.Size).Contains(point);
    private void Release(Point point)
    {
        gone.Stop();tabDwell.Stop();
        if(wheel.IsVisible&&wheel.Items.All(i=>!i.Id.StartsWith("placeholder:",StringComparison.Ordinal)))wheel.Selected=wheel.HitTest(point-WheelOrigin);
        var chosen=wheel.SelectedItem;
        Log($"Drop at {point:F0} files={files?.Length.ToString()??"none"} mode={mode} petal={chosen?.Id}");
        if(files is null&&url is null||mode is null){closeAll();return;}
        if(chosen?.Id.StartsWith("mode:",StringComparison.Ordinal)==true){clickMode=true;ShowWheel(chosen.Id[5..]);return;}
        // Without the files during the drag the petals were placeholders. Pick the same petal by position
        // when it can be found; otherwise show the real wheel to click.
        if(wheel.Items.Any(i=>i.Id.StartsWith("placeholder:",StringComparison.Ordinal)))
        {
            ShowWheel(mode);var real=wheel.HitTest(point-WheelOrigin);
            if(real>=0){_=Choose(wheel.Items[real]);return;}
            clickMode=true;return;
        }
        if(chosen is not null)_=Choose(chosen);else closeAll();
    }
    private void ShowWheel(string target)
    {
        mode=target;var center=origin??new Point(Width/2,Height/2);
        // Keep the whole wheel on screen even when the drag entered near an edge.
        var margin=WheelSize/2+8;center=new(Math.Clamp(center.X,margin,Math.Max(margin,Width-margin)),Math.Clamp(center.Y,margin,Math.Max(margin,Height-margin)));origin=center;
        var title=target switch{"convert"=>"Convert","actions"=>"File actions","modes"=>"Nashi",_=>"Tools"};
        if(target=="modes")
        {
            IReadOnlyList<ToolChoice> modes=[new("mode:tools","Tools","⚙"),new("mode:convert","Convert","⇄"),new("mode:actions","File actions","▱")];
            if(files is not null)modes=modes.Where(m=>main.ChoicesFor(files,m.Id[5..]).Count>0).ToList();
            if(files is null&&url is not null){ShowWheel("convert");return;}
            wheel.Items=modes;wheel.CenterTitle=L10n.T("Rest on one");wheel.IsVisible=true;switcher.IsVisible=false;HideHint();
            Canvas.SetLeft(wheel,center.X-WheelSize/2);Canvas.SetTop(wheel,center.Y-WheelSize/2);return;
        }
        // A web link gets the download wheel whatever the direction.
        if(files is null&&url is not null)title="Web link";
        var choices=files is not null?main.ChoicesFor(files,target):url is not null?MainWindow.WebChoices(url):[new ToolChoice("placeholder:"+target,title,Ui.Symbol(target=="actions"?"copy":target))];
        if(choices.Count==0){wheel.IsVisible=false;switcher.IsVisible=false;SetHint(L10n.T("Nothing in this wheel fits these files."),center);return;}
        wheel.Items=choices;wheel.CenterTitle=L10n.T(title);wheel.IsVisible=true;HideHint();
        Canvas.SetLeft(wheel,center.X-WheelSize/2);Canvas.SetTop(wheel,center.Y-WheelSize/2);
        ShowSwitcher(center);
    }
    /// The tabs under the wheel; a web link has only its download wheel, so no tabs then.
    private void ShowSwitcher(Point center)
    {
        if(files is null&&url is not null){switcher.IsVisible=false;return;}
        foreach(var (id,tab) in tabs){tab.Classes.Set("active",id==mode);tab.IsVisible=Fits(id);}
        switcher.IsVisible=true;switcher.Measure(Size.Infinity);var size=switcher.DesiredSize;
        Canvas.SetLeft(switcher,Math.Clamp(center.X-size.Width/2,12,Math.Max(12,Width-size.Width-12)));
        Canvas.SetTop(switcher,Math.Clamp(center.Y+WheelSize/2+12,12,Math.Max(12,Height-size.Height-12)));
    }
    private async Task Choose(ToolChoice choice)
    {
        if(url is not null&&choice.Id.StartsWith("web:",StringComparison.Ordinal)){var link=url;closeAll();await main.RunWebGesture(link,choice);return;}
        if(files is null){closeAll();return;}
        // Tools offers Convert as one petal; it opens the format wheel in place.
        if(choice.Id=="convert"){clickMode=true;ShowWheel("convert");return;}
        var chosen=files;closeAll();await main.RunGesture(chosen,choice);
    }
}
