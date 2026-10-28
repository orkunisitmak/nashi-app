using System.Windows;
using System.Windows.Controls;
using Nashi.Core;
namespace Nashi.Windows;
public sealed class MetadataEditor:Window
{
    private readonly CheckBox removeAll=new(){Content="Remove all metadata",Margin=new(4,14,4,14)};
    private readonly Dictionary<string,TextBox> fields=[];private readonly Dictionary<string,string> original=[];
    public bool RemoveAll=>removeAll.IsChecked==true;public Dictionary<string,string> Updates=>fields.Where(p=>p.Value.Text!=original.GetValueOrDefault(p.Key,"")).ToDictionary(p=>p.Key,p=>p.Value.Text);
    public MetadataEditor(Window owner,IReadOnlyDictionary<string,string> metadata,bool media=false)
    {
        Owner=owner;Theme.Attach(this);Title="Metadata · Nashi";Width=820;Height=700;MinWidth=640;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new(26)};Content=root;var header=new StackPanel();header.Children.Add(L10n.Label(new TextBlock{Text="Metadata",FontSize=28,FontWeight=FontWeights.Bold}));header.Children.Add(new TextBlock{Text=L10n.Text(media?"Edit fields or remove metadata. Encoded audio and video stay untouched.":"Edit fields or remove metadata. Encoded image pixels stay untouched."),Margin=new(0,8,0,0),TextWrapping=TextWrapping.Wrap});header.Children.Add(removeAll);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var footer=new DockPanel{LastChildFill=false,Margin=new(0,16,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);var save=MainWindow.Button("Save new copy ↗",()=>DialogResult=true);save.Background=Theme.Primary;save.Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(43,54,28));DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);var cancel=MainWindow.Button("Cancel",()=>DialogResult=false);DockPanel.SetDock(cancel,Dock.Left);footer.Children.Add(cancel);var stack=new StackPanel();root.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var source=metadata.GetValueOrDefault("SourceFile","")??"";var prefix=FeatureCatalog.Extension(source)=="pdf"?"PDF:":FeatureCatalog.Extension(source)=="mp3"?"ID3:":FeatureCatalog.IsVideo(source)||FeatureCatalog.IsAudio(source)?"QuickTime:":"";
        IEnumerable<string> editable=MetadataService.EditableTags.Where(k=>prefix.Length>0?k.StartsWith(prefix,StringComparison.Ordinal):k.StartsWith("EXIF:",StringComparison.Ordinal)||k.StartsWith("XMP:",StringComparison.Ordinal));
        if(media)editable=MediaMetadata.Editable.Concat(metadata.Keys.Where(k=>k.StartsWith("stream:",StringComparison.Ordinal)||k.StartsWith("chapter:",StringComparison.Ordinal)).Where(k=>k.EndsWith(":title",StringComparison.Ordinal)||k.EndsWith(":language",StringComparison.Ordinal)||k.EndsWith(":handler_name",StringComparison.Ordinal)||k.EndsWith(":comment",StringComparison.Ordinal))).Distinct();
        foreach(var key in editable){var value=metadata.GetValueOrDefault(key,"");original[key]=value;stack.Children.Add(new TextBlock{Text=media?MediaMetadata.Label(key):key,Margin=new(4,12,4,0)});var field=new TextBox{Text=value};fields[key]=field;stack.Children.Add(field);}
        var details=new StackPanel();foreach(var pair in metadata.Where(p=>!fields.ContainsKey(p.Key)))details.Children.Add(new TextBlock{Text=(media?MediaMetadata.Label(pair.Key):pair.Key)+"  ·  "+pair.Value,TextWrapping=TextWrapping.Wrap,Margin=new(4,5,4,5)});stack.Children.Add(new Expander{Header="All metadata",Content=details,Margin=new(4,18,4,12)});removeAll.Checked+=(_,_)=>{foreach(var field in fields.Values)field.IsEnabled=false;};removeAll.Unchecked+=(_,_)=>{foreach(var field in fields.Values)field.IsEnabled=true;};PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){Close();e.Handled=true;}};
    }
}
