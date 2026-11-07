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
/// so Nashi finally sees the pointer and the files. Moving left, right, up or down picks a wheel
/// (Settings → Shortcuts & gestures), and dropping on a petal runs it. A direct shortcut opens one
/// wheel at once. Without a drag, the wheel works by clicking (used for copied files).
public sealed class GestureWindow:Window
{
    /// How far a drag must lean after the shortcut to pick a wheel: a short push, well inside the
    /// wheel's centre disc, so the wheel opens under the pointer with no slice chosen yet.
    internal const double Threshold=36;
    private static double WheelSize=>380*PetalWheel.Scale;
    private readonly MainWindow main;private readonly string? fixedMode;private readonly Action closeAll;private readonly LinuxSettings settings;
    private readonly Canvas canvas=new();private readonly Border hint;private readonly TextBlock hintText=new(){FontSize=15,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=560};
    private readonly PetalWheel wheel=new(WheelSize){IsVisible=false};
    private Point? origin;private string? mode;private string[]? files;private string? url;private bool clickMode;
    private readonly DispatcherTimer dwell=new(){Interval=TimeSpan.FromMilliseconds(350)};private string? pending;
    // A drag that leaves the layer and does not come back (Esc, or a drop the compositor cancelled) closes it;
    // the short wait covers moving onto the wheel or across to another monitor's layer.
    private readonly DispatcherTimer gone=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly DispatcherTimer idle=new(){Interval=TimeSpan.FromSeconds(9)};private readonly DispatcherTimer nudge=new(){Interval=TimeSpan.FromSeconds(2.5)};
    public PetalWheel Wheel=>wheel;
    /// The monitor this layer covers, in X pixels.
    internal PixelRect Area{get;}

    /// NASHI_DEBUG_GESTURE=1 writes what the layer receives to ~/.local/share/Nashi/gesture.log.
    private static readonly bool Debug=Environment.GetEnvironmentVariable("NASHI_DEBUG_GESTURE")=="1";
    internal static void Log(string text){if(!Debug)return;try{Directory.CreateDirectory(AppPaths.Data);File.AppendAllText(Path.Combine(AppPaths.Data,"gesture.log"),$"{DateTime.Now:HH:mm:ss.fff} {text}\n");}catch(IOException){}}

    public GestureWindow(MainWindow main,Screen screen,string? fixedMode,Action closeAll,string[]? presetFiles=null)
    {
        this.main=main;this.fixedMode=fixedMode;this.closeAll=closeAll;settings=main.LinuxSettings;Area=screen.Bounds;
        Title="Nashi gesture";WindowDecorations=WindowDecorations.None;Topmost=true;ShowInTaskbar=false;CanResize=false;
        TransparencyLevelHint=[WindowTransparencyLevel.Transparent];Background=Brushes.Transparent;
        WindowStartupLocation=WindowStartupLocation.Manual;Position=screen.Bounds.Position;Width=screen.Bounds.Width/screen.Scaling;Height=screen.Bounds.Height/screen.Scaling;
        // A faint veil: it shows the layer is listening, and a non-null background is needed for drops.
        canvas.Background=new SolidColorBrush(Color.FromArgb(56,12,18,14));Content=canvas;
        hint=new Border{Classes={"card","gesture-hint"},Padding=new(22,14),Child=hintText};canvas.Children.Add(wheel);canvas.Children.Add(hint);
        wheel.Chosen+=async choice=>{if(clickMode)await Choose(choice);};
        wheel.SelectionChanged+=item=>{if(item is not null)SetHint(L10n.T(item.Title),origin);};
        DragDrop.SetAllowDrop(canvas,true);
        canvas.AddHandler(DragDrop.DragEnterEvent,OnDrag);canvas.AddHandler(DragDrop.DragOverEvent,OnDrag);canvas.AddHandler(DragDrop.DropEvent,OnDrop);
        canvas.PointerPressed+=(_,e)=>{if(!clickMode||e.Source==canvas)closeAll();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape)closeAll();else if(clickMode&&e.Key==Key.Enter)wheel.Choose();};
        idle.Tick+=(_,_)=>{idle.Stop();if(origin is null&&!clickMode)closeAll();};
        gone.Tick+=(_,_)=>LeaveNow();
        canvas.AddHandler(DragDrop.DragLeaveEvent,(_,_)=>DragLeft());
        // The hot edge opens a wheel of the three wheels; resting on one for a moment opens it.
        dwell.Tick+=(_,_)=>{dwell.Stop();if(mode=="modes"&&pending is {} next&&wheel.SelectedItem?.Id==next)ShowWheel(next[5..]);};
        // Some sessions don't pass a drag from Wayland apps to this layer (KWin's XWayland bridge can get
        // stuck until the next login). Say so, and offer the copy-and-shortcut route that always works.
        // No drag after 2.5 s: drop the native overlay so clicks reach this layer again (a click closes it).
        nudge.Tick+=(_,_)=>{nudge.Stop();if(origin is null&&!clickMode){main.HideNativeOverlay();var paste=settings.Shortcuts.GetValueOrDefault("clipboard","");SetHint(L10n.T("The drag didn't reach Nashi.")+"\n"+L10n.T("Drop the files, press Ctrl+C on them, then")+" "+(paste.Length>0?paste:"Ctrl+V")+".");}};
        Opened+=(_,_)=>
        {
            Log($"layer opened on {screen.Bounds} mode={fixedMode??"gesture"}");
            if(presetFiles is {Length:>0}){files=presetFiles;origin=new(Width/2,Height/2);clickMode=true;ShowWheel(fixedMode??"tools");}
            else{SetHint(fixedMode is null?Directions():fixedMode=="modes"?L10n.T("Keep dragging: rest on Tools, Convert or File actions."):L10n.T("Keep dragging here, then drop on a slice."));idle.Start();nudge.Start();}
            Activate();
        };
    }
    private string Directions()
    {
        string Name(string d)=>settings.Gestures[d] switch{"actions"=>L10n.T("File actions"),"convert"=>L10n.T("Convert"),"tools"=>L10n.T("Tools"),_=>L10n.T("Cancel")};
        return L10n.T("Keep dragging: move to pick a wheel")+$"\n←  {Name("left")}     →  {Name("right")}     ↑  {Name("up")}     ↓  {Name("down")}";
    }
    private void SetHint(string text,Point? near=null)
    {
        hintText.Text=text;hint.Measure(Size.Infinity);var size=hint.DesiredSize;
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
    internal void LeaveNow(){gone.Stop();if(!clickMode){Log("drag left the layer, closing");closeAll();}}
    private void Track(Point point)
    {
        idle.Stop();nudge.Stop();gone.Stop();
        if(origin is null){origin=point;if(fixedMode is not null)ShowWheel(fixedMode);return;}
        if(mode is null&&fixedMode is null)
        {
            var delta=point-origin.Value;if(Math.Sqrt(delta.X*delta.X+delta.Y*delta.Y)<Threshold)return;
            var direction=Math.Abs(delta.X)>=Math.Abs(delta.Y)?(delta.X<0?"left":"right"):(delta.Y<0?"up":"down");
            var target=settings.Gestures[direction];
            if(target=="none"){SetHint(L10n.T("Drop anywhere to cancel."),origin);return;}
            ShowWheel(target);return;
        }
        if(wheel.IsVisible)wheel.Selected=wheel.HitTest(point-WheelOrigin);
        if(mode=="modes"){var id=wheel.SelectedItem?.Id;if(id!=pending){pending=id;dwell.Stop();if(id is not null)dwell.Start();}}
    }
    private void Release(Point point)
    {
        gone.Stop();
        if(wheel.IsVisible&&wheel.Items.All(i=>!i.Id.StartsWith("placeholder:",StringComparison.Ordinal)))wheel.Selected=wheel.HitTest(point-WheelOrigin);
        var chosen=wheel.SelectedItem;
        Log($"Drop at {point:F0} files={files?.Length.ToString()??"none"} mode={mode} petal={chosen?.Id}");
        if(files is null&&url is null||mode is null){closeAll();return;}
        if(chosen?.Id.StartsWith("mode:",StringComparison.Ordinal)==true){clickMode=true;ShowWheel(chosen.Id[5..]);SetHint(L10n.T("Choose a slice."),origin);return;}
        // Without the files during the drag the petals were placeholders. Pick the same petal by position
        // when it can be found; otherwise show the real wheel to click.
        if(wheel.Items.Any(i=>i.Id.StartsWith("placeholder:",StringComparison.Ordinal)))
        {
            ShowWheel(mode);var real=wheel.HitTest(point-WheelOrigin);
            if(real>=0){_=Choose(wheel.Items[real]);return;}
            clickMode=true;SetHint(L10n.T("Choose a slice."),origin);return;
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
            wheel.Items=modes;wheel.CenterTitle="Nashi";wheel.IsVisible=true;
            Canvas.SetLeft(wheel,center.X-WheelSize/2);Canvas.SetTop(wheel,center.Y-WheelSize/2);SetHint(L10n.T("Rest on a wheel to open it."),center);return;
        }
        // A web link gets the download wheel whatever the direction.
        if(files is null&&url is not null)title="Web link";
        var choices=files is not null?main.ChoicesFor(files,target):url is not null?MainWindow.WebChoices(url):[new ToolChoice("placeholder:"+target,title,Ui.Symbol(target=="actions"?"copy":target))];
        if(choices.Count==0){wheel.IsVisible=false;SetHint(L10n.T("Nothing in this wheel fits these files."),center);return;}
        wheel.Items=choices;wheel.CenterTitle=L10n.T(title);wheel.IsVisible=true;
        Canvas.SetLeft(wheel,center.X-WheelSize/2);Canvas.SetTop(wheel,center.Y-WheelSize/2);
        SetHint(clickMode?L10n.T("Choose a slice."):L10n.T("Drop on a slice."),center);
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
