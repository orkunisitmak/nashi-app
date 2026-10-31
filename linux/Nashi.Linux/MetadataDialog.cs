using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nashi.Core;
namespace Nashi.Linux;

/// What the metadata editor asks for: remove everything, or change only the listed fields.
public sealed record MetadataEdit(bool RemoveAll,IReadOnlyDictionary<string,string> Updates);

/// The Windows metadata editor: editable fields first, every other tag under "All metadata".
/// Only fields whose text changed are written; the source file is never modified.
internal sealed class MetadataDialog:Window
{
    private readonly CheckBox removeAll=new(){Content="Remove all metadata",Margin=new(0,10,0,6)};
    private readonly Dictionary<string,TextBox> fields=[];private readonly Dictionary<string,string> original=[];
    public MetadataEdit? Result{get;private set;}
    internal IReadOnlyDictionary<string,TextBox> Fields=>fields;
    internal CheckBox RemoveAllBox=>removeAll;

    public MetadataDialog(IReadOnlyDictionary<string,string> metadata,bool media)
    {
        Title="Metadata";Width=760;Height=680;MinWidth=560;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var source=metadata.GetValueOrDefault("SourceFile","")??"";var ext=FeatureCatalog.Extension(source);
        IEnumerable<string> editable;
        if(media)editable=MediaMetadata.Editable.Concat(metadata.Keys.Where(k=>k.StartsWith("stream:",StringComparison.Ordinal)||k.StartsWith("chapter:",StringComparison.Ordinal)).Where(k=>k.EndsWith(":title",StringComparison.Ordinal)||k.EndsWith(":language",StringComparison.Ordinal)||k.EndsWith(":handler_name",StringComparison.Ordinal)||k.EndsWith(":comment",StringComparison.Ordinal))).Distinct();
        else{var prefix=ext=="pdf"?"PDF:":"";editable=MetadataService.EditableTags.Where(k=>prefix.Length>0?k.StartsWith(prefix,StringComparison.Ordinal):k.StartsWith("EXIF:",StringComparison.Ordinal)||k.StartsWith("XMP:",StringComparison.Ordinal));}
        var list=new StackPanel{Spacing=4};
        foreach(var key in editable)
        {
            var value=metadata.GetValueOrDefault(key,"")??"";original[key]=value;
            var field=new TextBox{Text=value};fields[key]=field;
            list.Children.Add(new TextBlock{Text=media?MediaMetadata.Label(key):key,Margin=new(0,8,0,0),FontSize=12,Opacity=.8});list.Children.Add(field);
        }
        var details=new StackPanel{Spacing=4};
        foreach(var pair in metadata.Where(p=>!fields.ContainsKey(p.Key)).OrderBy(p=>p.Key,StringComparer.Ordinal))
            details.Children.Add(new SelectableTextBlock{Text=(media?MediaMetadata.Label(pair.Key):pair.Key)+"  ·  "+pair.Value,TextWrapping=TextWrapping.Wrap,FontSize=12});
        list.Children.Add(new Expander{Header=Localizer.Pick("All metadata","Tüm metadata")+$" ({details.Children.Count})",Content=details,Margin=new(0,16,0,8),HorizontalAlignment=HorizontalAlignment.Stretch});
        removeAll.IsCheckedChanged+=(_,_)=>{foreach(var field in fields.Values)field.IsEnabled=removeAll.IsChecked!=true;};
        var save=new Button{Content="Save new copy",IsDefault=true,FontWeight=FontWeight.SemiBold,Background=NashiTheme.Primary,Foreground=new SolidColorBrush(Color.Parse("#243B2B"))};save.Click+=(_,_)=>Accept();
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();
        var header=new StackPanel{Margin=new(20,16,20,0),Children=
        {
            new TextBlock{Text=Path.GetFileName(source),FontSize=20,FontWeight=FontWeight.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis},
            new TextBlock{Text=media?"Edit fields or remove metadata. Encoded audio and video stay untouched.":"Edit fields or remove metadata. Encoded image pixels stay untouched.",TextWrapping=TextWrapping.Wrap,Opacity=.8,Margin=new(0,6,0,0)},
            removeAll
        }};
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(20,10),Children={cancel,save}};
        var root=new DockPanel{Children={header,bar,new ScrollViewer{Content=list,Margin=new(20,8,20,0)}}};DockPanel.SetDock(header,Dock.Top);DockPanel.SetDock(bar,Dock.Bottom);
        this[!BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Paper");Content=root;
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}};
    }
    public MetadataEdit Current=>new(removeAll.IsChecked==true,removeAll.IsChecked==true?new Dictionary<string,string>():fields.Where(p=>(p.Value.Text??"")!=original.GetValueOrDefault(p.Key,"")).ToDictionary(p=>p.Key,p=>p.Value.Text??""));
    private void Accept(){Result=Current;Close();}
}
