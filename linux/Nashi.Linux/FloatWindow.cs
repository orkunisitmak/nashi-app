using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
namespace Nashi.Linux;
public sealed class FloatWindow:Window
{
    public FloatWindow(string[] files,Action<IEnumerable<string>> reuse)
    {
        Title="Nashi · Float";Width=380;Height=360;Topmost=true;FontFamily=new Avalonia.Media.FontFamily("DejaVu Sans");Background=NashiTheme.Paper;Foreground=NashiTheme.Ink;
        var panel=new StackPanel{Margin=new(18),Spacing=10};Content=new ScrollViewer{Content=panel};panel.Children.Add(NashiTheme.Text("Float",22));
        foreach(var path in files)
        {
            var row=new StackPanel{Spacing=6};row.Children.Add(NashiTheme.Text(System.IO.Path.GetFileName(path)));var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
            actions.Children.Add(NashiTheme.Button("Use",()=>reuse([path])));actions.Children.Add(NashiTheme.Button("Preview",async()=>{try{await MainWindow.OpenPath(path);}catch(Exception error){Title=error.Message;}}));actions.Children.Add(NashiTheme.Button("Remove",()=>panel.Children.Remove(row)));row.Children.Add(actions);panel.Children.Add(row);
        }
    }
}
