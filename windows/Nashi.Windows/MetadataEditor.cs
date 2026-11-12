using System.Windows;
using System.Windows.Controls;
using Nashi.Core;
namespace Nashi.Windows;
public sealed class MetadataEditor:Window
{
    private readonly CheckBox removeAll=new(){Content="Remove all metadata",Margin=new(4,14,4,14)};
    private readonly Dictionary<string,TextBox> fields=[];private readonly Dictionary<string,string> original=[];
    public bool RemoveAll=>removeAll.IsChecked==true;public Dictionary<string,string> Updates=>fields.Where(p=>p.Value.Text!=original.GetValueOrDefault(p.Key,"")).ToDictionary(p=>p.Key,p=>p.Value.Text);
    private static Border Group(UIElement child){var card=new Border{CornerRadius=new(16),Padding=new(16,2,16,2),BorderThickness=new(1),Child=child};card.SetResourceReference(Border.BackgroundProperty,"Well");card.SetResourceReference(Border.BorderBrushProperty,"Line");return card;}
    private static Border Hairline(){var line=new Border{Height=1};line.SetResourceReference(Border.BackgroundProperty,"Line");return line;}
    public MetadataEditor(Window owner,IReadOnlyDictionary<string,string> metadata,bool media=false)
    {
        Owner=owner;Theme.Attach(this);L10n.Bind(this,Ui.HeadingProperty,"Metadata");Title="Metadata · Nashi";Width=820;Height=700;MinWidth=640;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        // Laid out like Settings: a title and a line about it, then cards of rows — the name on the left, its value on the right.
        var root=new DockPanel{Margin=new(28,4,28,22)};Content=root;var header=new StackPanel();var title=L10n.Label(new TextBlock{Text="Metadata",FontSize=28,FontWeight=FontWeights.Bold});title.SetResourceReference(TextBlock.FontFamilyProperty,"DisplayFont");header.Children.Add(title);
        var lead=new TextBlock{Text=L10n.Text(media?"Edit fields or remove metadata. Encoded audio and video stay untouched.":"Edit fields or remove metadata. Encoded image pixels stay untouched."),Margin=new(0,6,0,16),TextWrapping=TextWrapping.Wrap,FontSize=13};lead.SetResourceReference(TextBlock.ForegroundProperty,"Muted");header.Children.Add(lead);
        removeAll.Margin=new(0);removeAll.MinHeight=46;header.Children.Add(Group(removeAll));DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var footer=new DockPanel{LastChildFill=false,Margin=new(0,16,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);var save=MainWindow.Button("Save new copy ↗",()=>DialogResult=true);save.Style=Ui.Style("Signature");DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);var cancel=MainWindow.Button("Cancel",()=>DialogResult=false);DockPanel.SetDock(cancel,Dock.Left);footer.Children.Add(cancel);var stack=new StackPanel();root.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var source=metadata.GetValueOrDefault("SourceFile","")??"";var prefix=FeatureCatalog.Extension(source)=="pdf"?"PDF:":FeatureCatalog.Extension(source)=="mp3"?"ID3:":FeatureCatalog.IsVideo(source)||FeatureCatalog.IsAudio(source)?"QuickTime:":"";
        IEnumerable<string> editable=MetadataService.EditableTags.Where(k=>prefix.Length>0?k.StartsWith(prefix,StringComparison.Ordinal):k.StartsWith("EXIF:",StringComparison.Ordinal)||k.StartsWith("XMP:",StringComparison.Ordinal));
        if(media)editable=MediaMetadata.Editable.Concat(metadata.Keys.Where(k=>k.StartsWith("stream:",StringComparison.Ordinal)||k.StartsWith("chapter:",StringComparison.Ordinal)).Where(k=>k.EndsWith(":title",StringComparison.Ordinal)||k.EndsWith(":language",StringComparison.Ordinal)||k.EndsWith(":handler_name",StringComparison.Ordinal)||k.EndsWith(":comment",StringComparison.Ordinal))).Distinct();
        var rows=new StackPanel();
        foreach(var key in editable)
        {
            var value=metadata.GetValueOrDefault(key,"");original[key]=value;if(rows.Children.Count>0)rows.Children.Add(Hairline());
            var row=new Grid{MinHeight=52};row.ColumnDefinitions.Add(new(){Width=new(200)});row.ColumnDefinitions.Add(new());var names=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,8,14,8)};
            var (name,family)=media?(MediaMetadata.Label(key),""):key.IndexOf(':') is var colon&&colon>0?(key[(colon+1)..],key[..colon]):(key,"");
            names.Children.Add(new TextBlock{Text=name,FontSize=13,TextTrimming=TextTrimming.CharacterEllipsis});if(family.Length>0){var group=new TextBlock{Text=family,FontSize=11,Margin=new(0,2,0,0)};group.SetResourceReference(TextBlock.ForegroundProperty,"Muted");names.Children.Add(group);}
            var field=new TextBox{Text=value,Margin=new(0,8,0,8),VerticalAlignment=VerticalAlignment.Center};fields[key]=field;Grid.SetColumn(field,1);row.Children.Add(names);row.Children.Add(field);rows.Children.Add(row);
        }
        if(rows.Children.Count>0)stack.Children.Add(Group(rows));
        // Everything else, read only: names and values in two columns.
        var details=new StackPanel{Margin=new(0,6,0,6)};
        foreach(var pair in metadata.Where(p=>!fields.ContainsKey(p.Key)))
        {
            var line=new Grid{Margin=new(0,5,0,5)};line.ColumnDefinitions.Add(new(){Width=new(200)});line.ColumnDefinitions.Add(new());
            var key=new TextBlock{Text=media?MediaMetadata.Label(pair.Key):pair.Key,FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,0,14,0),ToolTip=pair.Key};key.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
            var text=new TextBlock{Text=pair.Value,FontSize=12.5,TextWrapping=TextWrapping.Wrap};Grid.SetColumn(text,1);line.Children.Add(key);line.Children.Add(text);details.Children.Add(line);
        }
        stack.Children.Add(new Expander{Header=L10n.Text("All metadata"),Content=Group(details),Margin=new(0,16,0,12)});removeAll.Checked+=(_,_)=>{foreach(var field in fields.Values)field.IsEnabled=false;};removeAll.Unchecked+=(_,_)=>{foreach(var field in fields.Values)field.IsEnabled=true;};PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){Close();e.Handled=true;}};
    }
}
