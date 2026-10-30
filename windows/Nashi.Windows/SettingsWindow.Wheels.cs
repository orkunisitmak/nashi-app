using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nashi.Core;
namespace Nashi.Windows;
/// Settings › Wheels: hide the convert targets, tools and file actions someone never uses. Mirrors the Mac WheelSettingsView.
public sealed partial class SettingsWindow
{
    private string wheelMode="convert";private WheelSample convertSample=WheelCustomization.ConvertSamples[0],toolSample=WheelCustomization.ToolSamples[0];
    private string WheelModifier(string mode)=>mode=="convert"?store.Value.ConvertModifier:mode=="tools"?store.Value.ToolsModifier:store.Value.ActionsModifier;
    private string[] HiddenWheel(string mode)=>mode=="convert"?store.Value.HiddenWheelFormats:mode=="tools"?store.Value.HiddenWheelTools:store.Value.HiddenWheelActions;
    private void SaveHiddenWheel(string mode,IEnumerable<string> ids){var list=ids.Distinct().Order().ToArray();Save(mode=="convert"?store.Value with{HiddenWheelFormats=list}:mode=="tools"?store.Value with{HiddenWheelTools=list}:store.Value with{HiddenWheelActions=list});}
    private (string Group,(string Id,string Title)[] Items)[] WheelGroups()=>wheelMode switch
    {
        "convert"=>WheelCustomization.FormatGroups.Select(g=>(g.Group,g.Formats.Select(f=>(f,f=="gz"?"GZIP":f.ToUpperInvariant())).ToArray())).ToArray(),
        "tools"=>WheelCustomization.ToolGroups.Select(g=>(g.Group,g.Tools.Select(t=>(t,FeatureCatalog.ToolNames[t])).ToArray())).ToArray(),
        _=>[("",WheelPresentation.ActionChoices.Select(c=>(c.Id,c.Title)).ToArray())]
    };
    private IReadOnlyList<ToolChoice> WheelPreviewChoices()=>WheelPresentation.Visible(wheelMode,wheelMode switch
    {
        "convert"=>FeatureCatalog.Formats(convertSample.Inputs).Select(f=>new ToolChoice("convert:"+f,f.ToUpperInvariant(),"↗")).ToArray(),
        "tools"=>FeatureCatalog.Tools(toolSample.Inputs).Select(t=>new ToolChoice(t,FeatureCatalog.ToolNames[t],MainWindow.ToolSymbol(t))).ToArray(),
        _=>WheelPresentation.ActionChoices
    },store.Value);
    private void WheelSettings()
    {
        page.Children.Clear();Heading("Only what you need.","Hide what you never use. The wheels share the space among the rest.");
        var tabs=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,0,16)};
        foreach(var (mode,title) in new[]{("convert","Convert"),("tools","Tools"),("actions","File actions")}){var tab=new Button{Content=L10n.Text(title)+"  ·  "+WheelModifier(mode),Padding=new(14,6,14,6),Margin=new(0,0,6,0),Background=Theme.Brush(mode==wheelMode?"Sage":"Inset"),FontWeight=mode==wheelMode?FontWeights.SemiBold:FontWeights.Normal};tab.Click+=(_,_)=>{wheelMode=mode;WheelSettings();};tabs.Children.Add(tab);}
        page.Children.Add(tabs);page.Children.Add(new TextBlock{Text=L10n.Text($"Opens with {WheelModifier(wheelMode)} while dragging files."),FontSize=12,Foreground=Theme.Brush("Muted"),Margin=new(2,-8,0,14)});
        var hidden=HiddenWheel(wheelMode).ToHashSet();var groups=WheelGroups();var all=groups.SelectMany(g=>g.Items).ToArray();var visible=all.Count(item=>!hidden.Contains(item.Id));var lastAction=wheelMode=="actions"&&visible==1;
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new(280)});
        var chips=new StackPanel{Margin=new(0,0,20,0)};
        foreach(var (group,items) in groups)
        {
            if(group.Length>0)chips.Children.Add(L10n.Label(new TextBlock{Text=group,FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=Theme.Brush("Muted"),Margin=new(2,10,0,4)}));
            var wrap=new WrapPanel();
            foreach(var (id,title) in items)
            {
                var shown=!hidden.Contains(id);var text=new TextBlock{Text=L10n.Text(title),TextDecorations=shown?null:TextDecorations.Strikethrough};
                var chip=new Button{Content=text,IsEnabled=!(shown&&lastAction),Padding=new(10,4,10,4),Margin=new(0,0,6,6),Background=Theme.Brush(shown?"Sage":"Inset"),Foreground=shown?Theme.Brush("Ink"):Theme.Brush("Muted")};
                System.Windows.Automation.AutomationProperties.SetName(chip,L10n.Text(title));
                chip.Click+=(_,_)=>ToggleWheelItem(id,hidden);wrap.Children.Add(chip);
            }
            chips.Children.Add(wrap);
        }
        chips.Children.Add(L10n.Label(new TextBlock{Text="Click an item to hide or show it.",FontSize=12,Foreground=Theme.Brush("Muted"),Margin=new(2,10,0,0)}));
        if(lastAction)chips.Children.Add(L10n.Label(new TextBlock{Text="At least one action stays visible.",FontSize=12,Foreground=Theme.Brush("Gold"),Margin=new(2,6,0,0)}));
        grid.Children.Add(chips);
        var side=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};Grid.SetColumn(side,1);
        if(wheelMode!="actions")
        {
            var samples=wheelMode=="convert"?WheelCustomization.ConvertSamples:WheelCustomization.ToolSamples;
            var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,0,8)};row.Children.Add(L10n.Label(new TextBlock{Text="Preview with",VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,8,0)}));
            // String items, like every other picker here: the app-wide ComboBox template only renders text.
            var picker=new ComboBox{ItemsSource=samples.Select(s=>s.Title).ToArray(),SelectedIndex=Array.IndexOf(samples,wheelMode=="convert"?convertSample:toolSample),MinWidth=110};
            picker.SelectionChanged+=(_,_)=>{if(picker.SelectedIndex<0)return;if(wheelMode=="convert")convertSample=samples[picker.SelectedIndex];else toolSample=samples[picker.SelectedIndex];WheelSettings();};
            row.Children.Add(picker);side.Children.Add(row);
        }
        var choices=WheelPreviewChoices();var canvas=new Canvas{Width=540,Height=540};
        WheelPresentation.Draw(canvas,choices,choices.Count==0?"Keep dragging\nDrop a file or link":wheelMode=="convert"?"Convert":wheelMode=="actions"?"File actions":"Tools",true);
        var preview=new Viewbox{Width=260,Height=260,Child=canvas,IsHitTestVisible=false};System.Windows.Automation.AutomationProperties.SetAutomationId(preview,"WheelSettingsPreview");side.Children.Add(preview);
        side.Children.Add(new TextBlock{Text=L10n.Text($"{visible} of {all.Length} visible"),HorizontalAlignment=HorizontalAlignment.Center,Foreground=Theme.Brush("Muted"),Margin=new(0,8,0,8)});
        var reset=MainWindow.Button("Show all",()=>{SaveHiddenWheel(wheelMode,[]);WheelSettings();});reset.IsEnabled=hidden.Count>0;reset.HorizontalAlignment=HorizontalAlignment.Center;side.Children.Add(reset);
        grid.Children.Add(side);page.Children.Add(grid);
    }
    private void ToggleWheelItem(string id,HashSet<string> hidden)
    {
        if(!hidden.Remove(id))hidden.Add(id);SaveHiddenWheel(wheelMode,hidden);
        // Show the change: switch the preview to a sample whose wheel contains this item.
        if(wheelMode=="convert"&&!FeatureCatalog.Formats(convertSample.Inputs).Contains(id)&&WheelCustomization.FirstConvertSample(id) is {} format)convertSample=format;
        if(wheelMode=="tools"&&!FeatureCatalog.Tools(toolSample.Inputs).Contains(id)&&WheelCustomization.FirstToolSample(id) is {} tool)toolSample=tool;
        WheelSettings();
    }
}
