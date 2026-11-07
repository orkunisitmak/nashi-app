using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Nashi.Windows;
public sealed class PdfOrganizer:Window
{
    private sealed class Page(int number,byte[] image){public int Number{get;}=number;public byte[] Image{get;}=image;public int Rotation{get;set;}}
    // Each page picture is decoded once; moving, turning or copying pages only rebuilds the tiles.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[],ImageSource> decoded=new();
    private static readonly System.Windows.Media.Effects.DropShadowEffect sheetShadow=Shadow();
    private static System.Windows.Media.Effects.DropShadowEffect Shadow(){var shadow=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=12,ShadowDepth=3,Direction=270,Opacity=.18};shadow.Freeze();return shadow;}
    private readonly CheckBox uniformWidth=new(){Content="Make all pages the same width",Margin=new(8)};public bool UniformWidth=>uniformWidth.IsChecked==true;
    private readonly List<Page> pages=[];private readonly ListBox gallery=new(){SelectionMode=SelectionMode.Extended};
    public int[] Pages=>pages.Select(p=>p.Number).ToArray();public Dictionary<int,int> Rotations=>pages.GroupBy(p=>p.Number).ToDictionary(g=>g.Key,g=>g.First().Rotation);public int[] PageRotations=>pages.Select(p=>p.Rotation).ToArray();
    private PdfOrganizer(Window owner){Owner=owner;Theme.Attach(this);L10n.Bind(this,Ui.HeadingProperty,"Organize PDF");Title="Organize PDF · Nashi";Width=1000;Height=760;MinWidth=700;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;var root=new DockPanel{Margin=new(28,4,28,22)};Content=root;
        // A title and one line about it; the pages; the page tools in one quiet capsule, Cancel and Save on the right.
        var heading=L10n.Label(new TextBlock{Text="Organize PDF",FontSize=28,FontWeight=FontWeights.Bold});heading.SetResourceReference(TextBlock.FontFamilyProperty,"DisplayFont");
        var lead=L10n.Label(new TextBlock{Text="Select pages, then move, turn, copy or remove them. The original stays as it is.",FontSize=13,Margin=new(0,6,0,16),TextWrapping=TextWrapping.Wrap});lead.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var top=new StackPanel{Children={heading,lead}};DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);var footer=new DockPanel{Margin=new(0,16,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var save=MainWindow.Button("Save organized copy ↗",()=>{if(pages.Count==0){MessageBox.Show(this,L10n.Text("Keep at least one page."));return;}DialogResult=true;});save.Style=Ui.Style("Signature");save.Margin=new(0);DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);
        var cancel=MainWindow.Button("Cancel",()=>DialogResult=false);cancel.Style=Ui.Style("Plain");cancel.Margin=new(0,0,8,0);DockPanel.SetDock(cancel,Dock.Right);footer.Children.Add(cancel);
        Button Tool(string title,string icon,Action action,bool mirror=false)
        {
            var button=MainWindow.Button(title,action);button.Style=Ui.Style("Plain");button.MinHeight=34;button.Margin=new(0);Ui.SetIcon(button,icon,15);
            if(mirror&&Ui.GetIcon(button) is FrameworkElement glyph){glyph.RenderTransformOrigin=new(.5,.5);glyph.RenderTransform=new ScaleTransform(-1,1);}return button;
        }
        var tools=new StackPanel{Orientation=Orientation.Horizontal};tools.Children.Add(Tool("Move left","chevLeft",()=>Move(-1)));tools.Children.Add(Tool("Move right","chevRight",()=>Move(1)));tools.Children.Add(Tool("Rotate","rotL",Rotate,true));tools.Children.Add(Tool("Duplicate","copy",Duplicate));tools.Children.Add(Tool("Remove","trash",Remove));
        var capsule=new RoundBorder{Radius=99,Padding=new(4),BorderThickness=new(1),Child=tools,VerticalAlignment=VerticalAlignment.Center};capsule.SetResourceReference(Border.BackgroundProperty,"Well");capsule.SetResourceReference(Border.BorderBrushProperty,"Line");
        uniformWidth.Margin=new(18,6,16,6);uniformWidth.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(new WrapPanel{VerticalAlignment=VerticalAlignment.Center,Children={capsule,uniformWidth}});
        gallery.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));ScrollViewer.SetHorizontalScrollBarVisibility(gallery,ScrollBarVisibility.Disabled);root.Children.Add(gallery);PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){Close();e.Handled=true;}else if(e.Key==System.Windows.Input.Key.Delete){Remove();e.Handled=true;}};}
    public static async Task<PdfOrganizer> CreateAsync(Window owner,string file){var result=new PdfOrganizer(owner);var images=await NativePdf.RenderAsync(file,72);for(var i=0;i<images.Length;i++)result.pages.Add(new(i+1,images[i]));result.Reload();return result;}
    private void Reload(){var selected=gallery.SelectedItems.Cast<ListBoxItem>().Select(i=>(Page)i.Tag).ToArray();gallery.Items.Clear();foreach(var page in pages){var content=new StackPanel();var sheet=new Border{HorizontalAlignment=HorizontalAlignment.Center,BorderThickness=new(1),Background=Brushes.White,Effect=sheetShadow,Child=new Image{Source=decoded.GetValue(page.Image,ImageEngine.FromPng),Width=130,Height=170,Stretch=Stretch.Uniform,LayoutTransform=new RotateTransform(page.Rotation)}};sheet.SetResourceReference(Border.BorderBrushProperty,"Line");content.Children.Add(sheet);
            var number=L10n.Label(new TextBlock{Text="Page "+page.Number,TextAlignment=TextAlignment.Center,FontSize=12,Margin=new(0,10,0,0)});number.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(number);var item=new ListBoxItem{Content=content,Tag=page,Margin=new(6),Padding=new(14,14,14,10),IsSelected=selected.Contains(page)};gallery.Items.Add(item);}}
    private Page[] Selected()=>gallery.SelectedItems.Cast<ListBoxItem>().Select(i=>(Page)i.Tag).ToArray();
    private void Move(int delta){var selected=Selected();foreach(var item in delta<0?selected:selected.Reverse()){var index=pages.IndexOf(item);var next=index+delta;if(next<0||next>=pages.Count)continue;pages.RemoveAt(index);pages.Insert(next,item);}Reload();}
    private void Rotate(){foreach(var page in Selected())page.Rotation=(page.Rotation+90)%360;Reload();}
    private void Duplicate(){foreach(var page in Selected().Reverse()){var index=pages.IndexOf(page);pages.Insert(index+1,new(page.Number,page.Image){Rotation=page.Rotation});}Reload();}
    private void Remove(){if(pages.Count-Selected().Length<1)return;foreach(var page in Selected())pages.Remove(page);Reload();}
}
