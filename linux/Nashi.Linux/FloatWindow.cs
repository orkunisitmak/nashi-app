using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
namespace Nashi.Linux;
public sealed class FloatWindow:Window
{
    private readonly List<string> files=[];
    private readonly StackPanel rows=new(){Spacing=10};
    private readonly TextBlock count=NashiTheme.Text("",13,true);
    private readonly ScrollViewer list;
    private bool collapsed;
    private readonly Action<IEnumerable<string>> reuse;
    internal IReadOnlyList<string> Files=>files;
    public FloatWindow(string[] paths,Action<IEnumerable<string>> reuse)
    {
        this.reuse=reuse;
        Title="Nashi · Float";Width=410;Height=390;MinWidth=350;Topmost=true;FontFamily=new Avalonia.Media.FontFamily("DejaVu Sans");Background=NashiTheme.Paper;Foreground=NashiTheme.Ink;
        var panel=new DockPanel{Margin=new(18)};Content=panel;
        var header=new StackPanel{Spacing=8,Margin=new(0,0,0,12)};DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);
        header.Children.Add(NashiTheme.Text("Float",22));header.Children.Add(count);
        var actions=new WrapPanel{Orientation=Orientation.Horizontal};
        actions.Children.Add(NashiTheme.Button("Add files",async()=>{var chosen=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Add to Float",AllowMultiple=true});AddFiles(chosen.Select(p=>p.TryGetLocalPath()).OfType<string>());}));
        actions.Children.Add(NashiTheme.Button("Use all",()=>reuse(files.ToArray())));
        var dragAll=NashiTheme.Button("Drag all",()=>{});dragAll.PointerPressed+=async(_,e)=>await DragFiles(e,files.ToArray());actions.Children.Add(dragAll);
        actions.Children.Add(NashiTheme.Button("Collapse",ToggleCollapsed));header.Children.Add(actions);
        list=new ScrollViewer{Content=rows};panel.Children.Add(list);
        DragDrop.SetAllowDrop(this,true);
        AddHandler(DragDrop.DragOverEvent,(_,e)=>{e.DragEffects=e.DataTransfer.TryGetFiles() is null?DragDropEffects.None:DragDropEffects.Copy;e.Handled=true;});
        AddHandler(DragDrop.DropEvent,(_,e)=>{var items=e.DataTransfer.TryGetFiles();if(items is not null)AddFiles(items.Select(p=>p.TryGetLocalPath()).OfType<string>());e.DragEffects=DragDropEffects.Copy;e.Handled=true;});
        AddFiles(paths);
    }
    internal void AddFiles(IEnumerable<string> paths)
    {
        foreach(var path in paths){if(!Path.IsPathFullyQualified(path))continue;var full=Path.GetFullPath(path);if((File.Exists(full)||Directory.Exists(full))&&!files.Contains(full,StringComparer.Ordinal))files.Add(full);}
        Refresh();
    }
    internal void ToggleCollapsed(){collapsed=!collapsed;list.IsVisible=!collapsed;Height=collapsed?170:390;}
    private void Refresh()
    {
        count.Text=$"{files.Count} file{(files.Count==1?"":"s")} · Drop files here to add";rows.Children.Clear();
        foreach(var path in files){
            var row=new StackPanel{Spacing=6};var label=NashiTheme.Text(Path.GetFileName(path));ToolTip.SetTip(label,path);row.Children.Add(label);
            var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
            var drag=NashiTheme.Button("Drag",()=>{});drag.PointerPressed+=async(_,e)=>await DragFiles(e,[path]);actions.Children.Add(drag);
            actions.Children.Add(NashiTheme.Button("Use",()=>reuse([path])));actions.Children.Add(NashiTheme.Button("Preview",async()=>{try{await MainWindow.OpenPath(path);}catch(Exception error){count.Text=error.Message;}}));actions.Children.Add(NashiTheme.Button("Remove",()=>{files.Remove(path);Refresh();}));row.Children.Add(actions);rows.Children.Add(row);
        }
    }
    private async Task DragFiles(PointerPressedEventArgs e,string[] paths)
    {
        if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        try{
            using var data=new DataTransfer();var added=0;
            foreach(var path in paths){IStorageItem? item=Directory.Exists(path)?await StorageProvider.TryGetFolderFromPathAsync(new Uri(path)):await StorageProvider.TryGetFileFromPathAsync(new Uri(path));if(item is not null){data.Add(DataTransferItem.CreateFile(item));added++;}}
            if(added>0)await DragDrop.DoDragDropAsync(e,data,DragDropEffects.Copy);
        }catch(Exception error){count.Text="Could not drag files: "+error.Message;}
    }
}
