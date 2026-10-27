using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nashi.Windows;
namespace Nashi.Linux;

public sealed record PdfOrganization(int[] Pages,int[] Rotations,bool UniformWidth);
internal sealed class PdfOrganizer:Window
{
    private sealed class Page(int number,byte[] image){public int Number{get;}=number;public byte[] Image{get;}=image;public int Rotation{get;set;}}
    private readonly List<Page> pages=[];
    private readonly ListBox gallery=new(){SelectionMode=SelectionMode.Multiple,ItemsPanel=new FuncTemplate<Panel?>(()=>new WrapPanel())};
    private readonly CheckBox uniform=new(){Content="Make all pages the same width"};
    public PdfOrganizer(byte[][] images)
    {
        Title="Organize PDF · Nashi";Width=1000;Height=760;MinWidth=700;MinHeight=500;Background=NashiTheme.Paper;Foreground=NashiTheme.Ink;
        for(var i=0;i<images.Length;i++)pages.Add(new(i+1,images[i]));
        var root=new DockPanel{Margin=new(26)};Content=root;var heading=NashiTheme.Text("Organize PDF",28);heading.Margin=new(0,0,0,18);DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var controls=new WrapPanel{Margin=new(0,16,0,0)};DockPanel.SetDock(controls,Dock.Bottom);root.Children.Add(controls);
        controls.Children.Add(NashiTheme.Button("Move left",()=>Move(-1)));controls.Children.Add(NashiTheme.Button("Move right",()=>Move(1)));controls.Children.Add(NashiTheme.Button("Rotate",()=>{foreach(var page in Selected())page.Rotation=(page.Rotation+90)%360;Reload();}));
        controls.Children.Add(NashiTheme.Button("Duplicate",()=>{foreach(var page in Selected().Reverse())pages.Insert(pages.IndexOf(page)+1,new(page.Number,page.Image){Rotation=page.Rotation});Reload();}));
        controls.Children.Add(NashiTheme.Button("Remove",Remove));controls.Children.Add(uniform);controls.Children.Add(NashiTheme.Button("Save organized copy",()=>{if(pages.Count>0)Close(new PdfOrganization(pages.Select(p=>p.Number).ToArray(),pages.Select(p=>p.Rotation).ToArray(),uniform.IsChecked==true));},true));
        controls.Children.Add(NashiTheme.Button("Cancel",()=>Close((PdfOrganization?)null)));foreach(var control in controls.Children)control.Margin=new(3);root.Children.Add(gallery);Reload();
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close((PdfOrganization?)null);if(e.Key==Key.Delete)Remove();};
    }
    private Page[] Selected()=>gallery.SelectedItems!.Cast<ListBoxItem>().Select(i=>(Page)i.Tag!).ToArray();
    private void Reload()
    {
        var selected=Selected();gallery.Items.Clear();foreach(var page in pages)
        {
            using var stream=new MemoryStream(page.Image);var content=new StackPanel{Spacing=8};content.Children.Add(new Image{Source=new Bitmap(stream),Width=130,Height=170,Stretch=Stretch.Uniform,RenderTransformOrigin=new(.5,.5,RelativeUnit.Relative),RenderTransform=new RotateTransform(page.Rotation)});content.Children.Add(NashiTheme.Text("Page "+page.Number,13));
            gallery.Items.Add(new ListBoxItem{Content=content,Tag=page,Margin=new(6),Padding=new(10),IsSelected=selected.Contains(page)});
        }
    }
    private void Move(int delta)
    {var selected=Selected();foreach(var page in delta<0?selected:selected.Reverse()){var i=pages.IndexOf(page);if(i+delta<0||i+delta>=pages.Count)continue;pages.RemoveAt(i);pages.Insert(i+delta,page);}Reload();}
    private void Remove(){var selected=Selected();if(pages.Count-selected.Length<1)return;foreach(var page in selected)pages.Remove(page);Reload();}
}
