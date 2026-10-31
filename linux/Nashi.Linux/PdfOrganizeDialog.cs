using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
namespace Nashi.Linux;

/// The organizer's result: 1-based source pages in the new order (repeats allowed) and each entry's extra rotation.
public sealed record PdfOrganization(int[] Pages,int[] Rotations,bool UniformWidth);

/// Page thumbnails in reading order. Select with click, Ctrl and Shift; drag a page to move it;
/// Ctrl+←/→ moves, R rotates, Ctrl+D duplicates, Delete removes. At least one page stays.
internal sealed class PdfOrganizeDialog:Window
{
    private sealed class Entry(int number,Bitmap image){public int Number{get;}=number;public Bitmap Image{get;}=image;public int Rotation{get;set;}}
    private readonly List<Entry> entries=[];private readonly HashSet<Entry> selected=[];
    private readonly WrapPanel gallery=new(){Margin=new(4)};
    internal WrapPanel Gallery=>gallery;
    private readonly CheckBox uniform=new(){Content="Make all pages the same width"};
    private readonly TextBlock state=new(){FontSize=12,Opacity=.75,VerticalAlignment=VerticalAlignment.Center};
    private Entry? anchor,dragging;private Point dragStart;
    public PdfOrganization? Result{get;private set;}

    private PdfOrganizeDialog(string title,string file)
    {
        Title=title;Width=1000;Height=720;MinWidth=640;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var save=new Button{Content="Save organized copy",IsDefault=true,FontWeight=FontWeight.SemiBold,Background=NashiTheme.Primary,Foreground=new SolidColorBrush(Color.Parse("#243B2B"))};
        save.Click+=(_,_)=>{Result=new(entries.Select(e=>e.Number).ToArray(),entries.Select(e=>e.Rotation).ToArray(),uniform.IsChecked==true);Close();};
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();
        Button Tool(string text,string tip,Action action){var b=new Button{Content=text};ToolTip.SetTip(b,tip);b.Click+=(_,_)=>{action();Focus();};return b;}
        var tools=new WrapPanel{Children={Tool("◀ Move","Move left (Ctrl+←)",()=>Move(-1)),Tool("Move ▶","Move right (Ctrl+→)",()=>Move(1)),Tool("⟳ Rotate","Rotate 90° (R)",Rotate),Tool("Duplicate","Duplicate (Ctrl+D)",Duplicate),Tool("Remove","Remove (Delete)",Remove),Tool("Select all","Ctrl+A",()=>{selected.UnionWith(entries);Reload();})}};
        foreach(var child in tools.Children.OfType<Control>())child.Margin=new(0,0,8,8);
        var bar=new DockPanel{Margin=new(16,10),Children={new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={cancel,save}},new StackPanel{Orientation=Orientation.Horizontal,Spacing=16,Children={uniform,state}}}};DockPanel.SetDock(bar.Children[0],Dock.Right);
        var header=new StackPanel{Margin=new(16,12,16,4),Spacing=8,Children={new TextBlock{Text=Path.GetFileName(file),FontSize=12,Opacity=.75,TextTrimming=TextTrimming.CharacterEllipsis},tools}};
        gallery.PointerMoved+=(_,e)=>Drag(e);gallery.PointerReleased+=(_,e)=>Drop(e);gallery.Background=Brushes.Transparent;
        var root=new DockPanel{Children={bar,header,new ScrollViewer{Content=gallery,Margin=new(12,0)}}};DockPanel.SetDock(bar,Dock.Bottom);DockPanel.SetDock(header,Dock.Top);
        this[!BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Paper");Content=root;
        // Tunnel, so the gallery's scroll viewer does not take the arrow keys first.
        AddHandler(KeyDownEvent,OnKey,Avalonia.Interactivity.RoutingStrategies.Tunnel);Closed+=(_,_)=>{foreach(var image in entries.Select(e=>e.Image).Distinct())image.Dispose();};
    }

    public static async Task<PdfOrganizeDialog> CreateAsync(string title,string file)
    {
        // Thumbnails at most 260 px on the long side keep a 300-page PDF light.
        var images=await Task.Run(()=>{using var pdf=PdfRaster.Open(file);return Enumerable.Range(0,pdf.PageCount).Select(i=>pdf.Thumbnail(i,260)).ToArray();});
        var dialog=new PdfOrganizeDialog(title,file);
        for(var i=0;i<images.Length;i++){using var stream=new MemoryStream(images[i]);dialog.entries.Add(new(i+1,new Bitmap(stream)));}
        dialog.Reload();return dialog;
    }

    private void Reload()
    {
        selected.IntersectWith(entries);gallery.Children.Clear();
        foreach(var entry in entries)
        {
            // Quarter turns swap the box so the turned page still fits the card.
            var turned=entry.Rotation is 90 or 270;
            var picture=new LayoutTransformControl{LayoutTransform=new RotateTransform(entry.Rotation),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Child=new Image{Source=entry.Image,MaxWidth=turned?170:130,MaxHeight=turned?130:170,Stretch=Stretch.Uniform}};
            var on=selected.Contains(entry);
            var card=new Border{Width=158,Margin=new(6),Padding=new(8),CornerRadius=new(12),BorderThickness=new(on?3:1),Cursor=new Cursor(StandardCursorType.Hand),
                Child=new StackPanel{Spacing=6,Children={new Border{Child=picture,Height=170},new TextBlock{Text=Localizer.Pick($"Page {entry.Number}",$"Sayfa {entry.Number}")+(entry.Rotation>0?$" · {entry.Rotation}°":""),HorizontalAlignment=HorizontalAlignment.Center}}}};
            card[!Border.BackgroundProperty]=Resource(on?"Sage":"Surface");card.BorderBrush=on?new SolidColorBrush(Color.FromRgb(227,184,89)):null;if(!on)card[!Border.BorderBrushProperty]=Resource("Line");
            card.Tag=entry;card.PointerPressed+=(_,e)=>Press(entry,e);
            gallery.Children.Add(card);
        }
        state.Text=Localizer.Pick($"{entries.Count} page{(entries.Count==1?"":"s")}"+(selected.Count>0?$" · {selected.Count} selected":""),$"{entries.Count} sayfa"+(selected.Count>0?$" · {selected.Count} seçili":""));
    }

    private void Press(Entry entry,PointerPressedEventArgs e)
    {
        var modifiers=e.KeyModifiers;
        if(modifiers.HasFlag(KeyModifiers.Shift)&&anchor is not null&&entries.Contains(anchor))
        {var a=entries.IndexOf(anchor);var b=entries.IndexOf(entry);selected.Clear();selected.UnionWith(entries.Skip(Math.Min(a,b)).Take(Math.Abs(a-b)+1));}
        else if(modifiers.HasFlag(KeyModifiers.Control)){if(!selected.Remove(entry))selected.Add(entry);anchor=entry;}
        else{if(!selected.Contains(entry)){selected.Clear();selected.Add(entry);}anchor=entry;}
        // The gallery keeps the pointer for the drag; the cards are only restyled, not rebuilt.
        dragging=entry;dragStart=e.GetPosition(gallery);e.Pointer.Capture(gallery);e.Handled=true;Restyle();Focus();
    }
    private void Restyle()
    {
        foreach(var card in gallery.Children.OfType<Border>())
        {
            var on=card.Tag is Entry entry&&selected.Contains(entry);card.BorderThickness=new(on?3:1);
            card[!Border.BackgroundProperty]=Resource(on?"Sage":"Surface");if(on)card.BorderBrush=new SolidColorBrush(Color.FromRgb(227,184,89));else card[!Border.BorderBrushProperty]=Resource("Line");
        }
        state.Text=Localizer.Pick($"{entries.Count} page{(entries.Count==1?"":"s")}"+(selected.Count>0?$" · {selected.Count} selected":""),$"{entries.Count} sayfa"+(selected.Count>0?$" · {selected.Count} seçili":""));
    }
    private void Drag(PointerEventArgs e)
    {
        if(dragging is null||!e.GetCurrentPoint(gallery).Properties.IsLeftButtonPressed)return;
        var point=e.GetPosition(gallery);if(Math.Abs(point.X-dragStart.X)+Math.Abs(point.Y-dragStart.Y)<12)return;
        Cursor=new Cursor(StandardCursorType.DragMove);
    }
    /// Drops the selection before the card under the pointer (after it when released on its right half).
    private void Drop(PointerReleasedEventArgs e)
    {
        var source=dragging;dragging=null;Cursor=Cursor.Default;e.Pointer.Capture(null);if(source is null)return;
        var point=e.GetPosition(gallery);if(Math.Abs(point.X-dragStart.X)+Math.Abs(point.Y-dragStart.Y)<12)return;
        var target=gallery.Children.Select((card,index)=>(card,index)).FirstOrDefault(c=>c.card.Bounds.Contains(point));
        if(target.card is null)return;
        var moving=entries.Where(selected.Contains).ToList();var at=target.index+(point.X>target.card.Bounds.Center.X?1:0);
        at-=entries.Take(at).Count(selected.Contains);foreach(var item in moving)entries.Remove(item);entries.InsertRange(Math.Clamp(at,0,entries.Count),moving);Reload();
    }

    private void OnKey(object? sender,KeyEventArgs e)
    {
        var ctrl=e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch(e.Key)
        {
            case Key.Left when ctrl:Move(-1);break;case Key.Right when ctrl:Move(1);break;
            case Key.Left or Key.Right:Step(e.Key==Key.Left?-1:1,e.KeyModifiers.HasFlag(KeyModifiers.Shift));break;
            case Key.R when !ctrl:Rotate();break;case Key.D when ctrl:Duplicate();break;case Key.A when ctrl:selected.UnionWith(entries);Reload();break;
            case Key.Delete or Key.Back:Remove();break;case Key.Escape:Close();break;
            default:return;
        }
        e.Handled=true;
    }
    private void Step(int delta,bool extend)
    {
        if(entries.Count==0)return;var from=anchor is not null&&entries.Contains(anchor)?entries.IndexOf(anchor):-1;var next=entries[Math.Clamp(from+delta,0,entries.Count-1)];
        if(!extend)selected.Clear();selected.Add(next);anchor=next;Reload();
    }
    // Public for the render check, which drives the dialog without a pointer.
    public void Select(params int[] indexes){selected.Clear();foreach(var i in indexes)selected.Add(entries[i]);anchor=entries[indexes[^1]];Reload();}
    public void Move(int delta)
    {
        var moving=entries.Where(selected.Contains).ToList();if(moving.Count==0)return;
        foreach(var item in delta<0?moving:Enumerable.Reverse(moving)){var index=entries.IndexOf(item);var next=index+delta;if(next<0||next>=entries.Count||selected.Contains(entries[next]))continue;entries.RemoveAt(index);entries.Insert(next,item);}
        Reload();
    }
    public void Rotate(){foreach(var entry in selected)entry.Rotation=(entry.Rotation+90)%360;Reload();}
    public void Duplicate()
    {
        var copies=new List<Entry>();
        foreach(var entry in entries.Where(selected.Contains).Reverse().ToArray()){var copy=new Entry(entry.Number,entry.Image){Rotation=entry.Rotation};entries.Insert(entries.IndexOf(entry)+1,copy);copies.Add(copy);}
        selected.Clear();selected.UnionWith(copies);Reload();
    }
    public void Remove()
    {
        if(selected.Count==0)return;if(entries.Count-selected.Count<1){state.Text="Keep at least one page.";return;}
        var first=entries.FindIndex(selected.Contains);entries.RemoveAll(selected.Contains);selected.Clear();
        if(entries.Count>0){anchor=entries[Math.Min(first,entries.Count-1)];selected.Add(anchor);}Reload();
    }
    public PdfOrganization Current=>new(entries.Select(e=>e.Number).ToArray(),entries.Select(e=>e.Rotation).ToArray(),uniform.IsChecked==true);
    private static Avalonia.Data.IBinding Resource(string key)=>new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key);
}
