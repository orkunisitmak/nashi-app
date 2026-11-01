using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
namespace Nashi.Linux;

/// Float: a small window that stays on top and holds files. Drop files on it to add them, drag
/// them out into any app, open or remove them, or collapse it to a title strip. Removing a file
/// from Float never touches the file itself.
internal sealed class FloatShelf:Window
{
    private readonly List<string> items=[];
    private readonly ListBox list=new(){SelectionMode=SelectionMode.Multiple,MinHeight=80};
    private readonly TextBlock caption=new(){FontWeight=FontWeight.SemiBold,VerticalAlignment=VerticalAlignment.Center};
    private readonly Control body;
    private Point? pressed;
    internal IReadOnlyList<string> Items=>items;
    internal ListBox List=>list;

    public FloatShelf(IEnumerable<string> files)
    {
        Title="Float";Width=300;Height=380;MinWidth=220;MinHeight=40;Topmost=true;ShowInTaskbar=true;CanResize=true;
        list.ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<string>((path,_)=>{var row=new TextBlock{Text=Path.GetFileName(path),TextTrimming=TextTrimming.CharacterEllipsis};ToolTip.SetTip(row,path);return row;});
        var open=new Button{Content="Open"};open.Click+=(_,_)=>{foreach(var file in Selected())Launch(file);};
        var remove=new Button{Content="Remove"};remove.Click+=(_,_)=>Remove();
        var collapse=new Button{Content="▁",Padding=new(8,2)};ToolTip.SetTip(collapse,"Collapse");collapse.Click+=(_,_)=>Toggle();
        var header=new DockPanel{Margin=new(10,8),Children={collapse,caption}};DockPanel.SetDock(collapse,Dock.Right);
        body=new DockPanel{Margin=new(10,0,10,10),Children={new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,Margin=new(0,8,0,0),Children={open,remove}},new TextBlock{Text="Drag files out to use them anywhere. Drop files here to add them.",FontSize=11,Opacity=.7,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,6)},list}};
        DockPanel.SetDock(((DockPanel)body).Children[0],Dock.Bottom);DockPanel.SetDock(((DockPanel)body).Children[1],Dock.Top);
        var root=new DockPanel{Children={header,body}};DockPanel.SetDock(header,Dock.Top);
        this[!BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Surface");Content=root;
        DragDrop.SetAllowDrop(this,true);
        AddHandler(DragDrop.DragOverEvent,(_,e)=>e.DragEffects=e.DataTransfer.Contains(DataFormat.File)&&!ReferenceEquals(e.Source,list)?DragDropEffects.Copy:DragDropEffects.None);
        AddHandler(DragDrop.DropEvent,(_,e)=>Add(e.DataTransfer.TryGetFiles()?.Select(f=>f.TryGetLocalPath()).OfType<string>()??[]));
        list.AddHandler(PointerPressedEvent,(_,e)=>{if(e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)pressed=e.GetPosition(list);},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        list.AddHandler(PointerMovedEvent,async(_,e)=>await DragOut(e),Avalonia.Interactivity.RoutingStrategies.Tunnel);
        list.AddHandler(PointerReleasedEvent,(_,_)=>pressed=null,Avalonia.Interactivity.RoutingStrategies.Tunnel);
        list.DoubleTapped+=(_,_)=>{foreach(var file in Selected())Launch(file);};
        KeyDown+=(_,e)=>{if(e.Key is Key.Delete or Key.Back){Remove();e.Handled=true;}else if(e.Key==Key.A&&e.KeyModifiers==KeyModifiers.Control){list.SelectAll();e.Handled=true;}};
        Add(files);
    }

    public void Add(IEnumerable<string> files)
    {
        foreach(var file in files.Select(Path.GetFullPath))if((File.Exists(file)||Directory.Exists(file))&&!items.Contains(file))items.Add(file);
        Refresh();
    }
    private string[] Selected()=>list.SelectedItems?.OfType<string>().ToArray() is {Length:>0} picked?picked:items.ToArray();
    public void Remove(){foreach(var file in list.SelectedItems?.OfType<string>().ToArray()??[])items.Remove(file);Refresh();}
    private void Refresh(){list.ItemsSource=items.ToArray();caption.Text=items.Count==1?"Float · 1 file":Localizer.Pick($"Float · {items.Count} files",$"Float · {items.Count} dosya");}
    private bool collapsed;private double expanded=380;
    public void Toggle(){collapsed=!collapsed;body.IsVisible=!collapsed;if(collapsed){expanded=Height;Height=48;}else Height=expanded;}

    /// The selected files (or the one under the pointer) leave as a file drag every desktop app accepts.
    private async Task DragOut(PointerEventArgs e)
    {
        if(pressed is not {} start||!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)return;
        var point=e.GetPosition(list);if(Math.Abs(point.X-start.X)+Math.Abs(point.Y-start.Y)<8)return;pressed=null;
        var transfer=new DataTransfer();
        foreach(var path in Selected())if((Directory.Exists(path)?await StorageProvider.TryGetFolderFromPathAsync(path) as IStorageItem:await StorageProvider.TryGetFileFromPathAsync(path)) is {} item)transfer.Add(DataTransferItem.CreateFile(item));
        Trace.Log($"float drag out: {transfer.Items.Count} items");
        if(transfer.Items.Count>0){var result=await DragDrop.DoDragDropAsync(e,transfer,DragDropEffects.Copy|DragDropEffects.Link);Trace.Log("float drag result: "+result);}
    }
    private static void Launch(string path)
    {
        try{var start=new System.Diagnostics.ProcessStartInfo("xdg-open"){UseShellExecute=false};start.ArgumentList.Add(path);System.Diagnostics.Process.Start(start)?.Dispose();}
        catch(Exception error)when(error is System.ComponentModel.Win32Exception or InvalidOperationException){}
    }
}
