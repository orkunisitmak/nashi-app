using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
namespace Nashi.Linux.UI;

/// Wayland never shows an app another app's drag. A global shortcut pressed mid-drag opens this
/// see-through layer on every screen; the drag that is already in progress then passes over it,
/// so Nashi finally sees the pointer and the files. Moving left, right, up or down picks a wheel
/// (Settings → Gestures), and dropping on a slice runs it. A direct shortcut opens one wheel at
/// once. Without a drag, the wheel works by clicking (used for copied files).
public sealed class GestureWindow:Window
{
    private const double Threshold=70,Radius=150,Item=76;
    private readonly MainWindow main;private readonly string? fixedMode;private readonly Action closeAll;private readonly LinuxSettings settings;
    private readonly Canvas canvas=new();private readonly Border hint;private readonly TextBlock hintText=new(){FontSize=15,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=520};
    private Point? origin;private string? mode;private string[]? files;private bool clickMode;
    private readonly List<(Button Button,ToolChoice Choice,Point Center)> items=[];private int hover=-1;
    private readonly DispatcherTimer idle=new(){Interval=TimeSpan.FromSeconds(7)};

    public GestureWindow(MainWindow main,Screen screen,string? fixedMode,Action closeAll,string[]? presetFiles=null)
    {
        this.main=main;this.fixedMode=fixedMode;this.closeAll=closeAll;settings=main.LinuxSettings;
        Title="Nashi gesture";WindowDecorations=WindowDecorations.None;Topmost=true;ShowInTaskbar=false;CanResize=false;
        TransparencyLevelHint=[WindowTransparencyLevel.Transparent];Background=Brushes.Transparent;
        WindowStartupLocation=WindowStartupLocation.Manual;Position=screen.Bounds.Position;Width=screen.Bounds.Width/screen.Scaling;Height=screen.Bounds.Height/screen.Scaling;
        // A faint veil: it shows the layer is listening, and a non-null background is needed for drops.
        canvas.Background=new SolidColorBrush(Color.FromArgb(48,17,26,20));Content=canvas;
        hint=new Border{Classes={"card","gesture-hint"},Padding=new(22,14),Child=hintText};canvas.Children.Add(hint);
        DragDrop.SetAllowDrop(canvas,true);
        canvas.AddHandler(DragDrop.DragEnterEvent,OnDrag);canvas.AddHandler(DragDrop.DragOverEvent,OnDrag);canvas.AddHandler(DragDrop.DropEvent,OnDrop);
        canvas.PointerPressed+=(_,e)=>{if(!clickMode||e.Source==canvas)closeAll();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape)closeAll();};
        idle.Tick+=(_,_)=>{idle.Stop();if(origin is null&&!clickMode)closeAll();};
        Opened+=(_,_)=>
        {
            Log($"layer opened on {screen.Bounds} mode={fixedMode??"gesture"}");
            if(presetFiles is {Length:>0}){files=presetFiles;origin=new(Width/2,Height/2);clickMode=true;ShowWheel(fixedMode??"tools");}
            else{SetHint(fixedMode is null?Directions():L10n.T("Keep dragging here, then drop on a slice."));idle.Start();}
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
        Canvas.SetTop(hint,Math.Clamp(near is null?at.Y-size.Height/2:at.Y+Radius+Item/2+18,12,Math.Max(12,Height-size.Height-12)));
    }
    /// NASHI_DEBUG_GESTURE=1 writes what the layer receives to ~/.local/share/Nashi/gesture.log.
    private static readonly bool Debug=Environment.GetEnvironmentVariable("NASHI_DEBUG_GESTURE")=="1";
    internal static void Log(string text){if(!Debug)return;try{Directory.CreateDirectory(AppPaths.Data);File.AppendAllText(Path.Combine(AppPaths.Data,"gesture.log"),$"{DateTime.Now:HH:mm:ss.fff} {text}\n");}catch(IOException){}}
    private string[]? Files(DragEventArgs e)=>e.DataTransfer.TryGetFiles()?.Select(f=>f.TryGetLocalPath()).OfType<string>().ToArray() is {Length:>0} paths?paths:null;

    private void OnDrag(object? sender,DragEventArgs e)
    {
        idle.Stop();var point=e.GetPosition(canvas);e.DragEffects=DragDropEffects.Copy;
        if(origin is null||mode is null)Log($"{e.RoutedEvent?.Name} at {point:F0} files={(files??Files(e))?.Length.ToString()??"not yet"} modifiers={e.KeyModifiers}");
        // Some toolkits only hand over the files on drop; the wheel then finishes after the drop.
        files??=Files(e);
        if(origin is null){origin=point;if(fixedMode is not null)ShowWheel(fixedMode);return;}
        if(mode is null&&fixedMode is null)
        {
            var delta=point-origin.Value;if(Math.Sqrt(delta.X*delta.X+delta.Y*delta.Y)<Threshold)return;
            var direction=Math.Abs(delta.X)>=Math.Abs(delta.Y)?(delta.X<0?"left":"right"):(delta.Y<0?"up":"down");
            var wheel=settings.Gestures[direction];
            if(wheel=="none"){SetHint(L10n.T("Drop anywhere to cancel."),origin);return;}
            ShowWheel(wheel);return;
        }
        Hover(point);
    }
    private void OnDrop(object? sender,DragEventArgs e)
    {
        files=Files(e)??files;var point=e.GetPosition(canvas);Log($"Drop at {point:F0} files={files?.Length.ToString()??"none"} mode={mode} hover={hover}");
        if(files is null){closeAll();return;}
        if(mode is null){closeAll();return;}
        // Without the files during the drag the slices were placeholders; now show the real ones to click.
        if(items.Count==0||items.All(i=>i.Choice.Id.StartsWith("placeholder:",StringComparison.Ordinal))){clickMode=true;ShowWheel(mode);return;}
        Hover(point);
        if(hover>=0)_=Choose(items[hover].Choice);else closeAll();
    }
    private void Hover(Point point)
    {
        var found=items.FindIndex(i=>(point-i.Center) is var d&&d.X*d.X+d.Y*d.Y<=(Item/2+6)*(Item/2+6));
        if(found==hover)return;hover=found;
        for(var n=0;n<items.Count;n++)items[n].Button.Classes.Set("selected",n==hover);
        if(hover>=0)SetHint(L10n.T(items[hover].Choice.Title),origin);
    }
    private void ShowWheel(string wheel)
    {
        mode=wheel;foreach(var item in items)canvas.Children.Remove(item.Button);items.Clear();hover=-1;
        var center=origin??new Point(Width/2,Height/2);
        // Keep the whole ring on screen even when the drag entered near an edge.
        var margin=Radius+Item/2+8;center=new(Math.Clamp(center.X,margin,Math.Max(margin,Width-margin)),Math.Clamp(center.Y,margin,Math.Max(margin,Height-margin)));origin=center;
        var choices=files is null?[new ToolChoice("placeholder:"+wheel,wheel switch{"convert"=>"Convert","actions"=>"File actions",_=>"Tools"},Ui.Symbol(wheel=="actions"?"copy":wheel))]:main.ChoicesFor(files,wheel);
        if(choices.Count==0){SetHint(L10n.T("Nothing in this wheel fits these files."),center);return;}
        var disc=canvas.Children.OfType<Border>().FirstOrDefault(b=>b.Classes.Contains("gesture-disc"));
        if(disc is null){disc=new Border{Classes={"card","gesture-disc"},Width=2*Radius+Item+24,Height=2*Radius+Item+24,CornerRadius=new(Radius+Item/2+12),Padding=new(0)};canvas.Children.Insert(0,disc);}
        Canvas.SetLeft(disc,center.X-disc.Width/2);Canvas.SetTop(disc,center.Y-disc.Height/2);
        var size=choices.Count>12?Item*.8:Item;
        for(var n=0;n<choices.Count;n++)
        {
            var choice=choices[n];var angle=-Math.PI/2+2*Math.PI*n/choices.Count;var at=new Point(center.X+Radius*Math.Cos(angle),center.Y+Radius*Math.Sin(angle));
            var content=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,Children={new TextBlock{Text=choice.Symbol,FontSize=size*.3,HorizontalAlignment=HorizontalAlignment.Center},new TextBlock{Text=L10n.T(choice.Title),FontSize=10,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=size-8,HorizontalAlignment=HorizontalAlignment.Center}}};
            var button=new Button{Width=size,Height=size,Content=content,Classes={"wheel"}};ToolTip.SetTip(button,L10n.T(choice.Title));
            button.Click+=async(_,_)=>{if(clickMode)await Choose(choice);};
            DragDrop.SetAllowDrop(button,true);
            Canvas.SetLeft(button,at.X-size/2);Canvas.SetTop(button,at.Y-size/2);canvas.Children.Add(button);items.Add((button,choice,at));
        }
        SetHint(clickMode?L10n.T("Choose a slice."):L10n.T("Drop on a slice."),center);
    }
    private async Task Choose(ToolChoice choice)
    {
        if(files is null){closeAll();return;}
        // Tools offers Convert as one slice; it opens the format wheel in place.
        if(choice.Id=="convert"){clickMode=true;ShowWheel("convert");return;}
        var chosen=files;closeAll();await main.RunGesture(chosen,choice);
    }
}
