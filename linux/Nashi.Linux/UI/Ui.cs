using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nashi.Core;
namespace Nashi.Linux.UI;

/// The app language. Text is translated when a view is built; changing the language
/// rebuilds the open views, as the catalog in Nashi.Core is the single source of strings.
public static class L10n
{
    public static string Language{get;set;}="en";
    /// Shared catalog first (identical on macOS and Windows), then the few Linux-only strings.
    public static string T(string source)
    {
        var text=AppLocalization.Text(source,Language);
        return Language=="tr"&&ReferenceEquals(text,source)&&Linux.TryGetValue(source,out var linux)?linux:text;
    }
    private static readonly Dictionary<string,string> Linux=Load();
    private static Dictionary<string,string> Load()
    {
        using var stream=typeof(L10n).Assembly.GetManifestResourceStream("Nashi.Turkish.Linux.json");
        return stream is null?[]:System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??[];
    }
    public static string Upper(string source)=>AppLocalization.Uppercase(T(source),Language);
}

/// Small factory helpers so views stay readable in code (no XAML per view).
public static class Ui
{
    public static TextBlock Text(string text,double size=14,string? style=null,bool translate=true)
    {
        var block=new TextBlock{Text=translate?L10n.T(text):text,FontSize=size,TextWrapping=TextWrapping.Wrap};
        if(style is not null)foreach(var name in style.Split(' '))block.Classes.Add(name);return block;
    }
    public static TextBlock Title(string text,double size=24)=>Text(text,size,"title");
    public static Button Button(string text,Action click,string? style=null,string? tip=null)
    {
        var button=new Button{Content=L10n.T(text)};if(style is not null)foreach(var name in style.Split(' '))button.Classes.Add(name);
        if(tip is not null)ToolTip.SetTip(button,L10n.T(tip));button.Click+=(_,_)=>click();return button;
    }
    public static Button Button(string text,Func<Task> click,string? style=null,string? tip=null)=>Button(text,()=>{_=Guard(click);},style,tip);
    public static Button Primary(string text,Action click)=>Button(text,click,"primary");
    public static Button Primary(string text,Func<Task> click)=>Button(text,click,"primary");
    public static Border Card(Control child,string style="card")=>new(){Child=child,Classes={style}};
    public static StackPanel Stack(double spacing=0,params Control[] children){var stack=new StackPanel{Spacing=spacing};stack.Children.AddRange(children);return stack;}
    public static StackPanel Row(double spacing=0,params Control[] children){var stack=new StackPanel{Orientation=Orientation.Horizontal,Spacing=spacing};stack.Children.AddRange(children);return stack;}
    public static WrapPanel Wrap(params Control[] children){var wrap=new WrapPanel();wrap.Children.AddRange(children);return wrap;}
    /// A label above a control, as in the Windows editors.
    public static StackPanel Field(string label,Control control,string? hint=null)
    {
        var stack=new StackPanel{Spacing=4,Margin=new(0,0,0,12)};stack.Children.Add(Text(label,12,"muted"));stack.Children.Add(control);
        if(hint is not null)stack.Children.Add(Text(hint,11,"muted"));return stack;
    }
    public static ComboBox Choice(IEnumerable<(string Value,string Label)> items,string selected)
    {
        var list=items.ToList();var combo=new ComboBox{ItemsSource=list.Select(i=>L10n.T(i.Label)).ToList(),MinWidth=180,Tag=list.Select(i=>i.Value).ToArray()};
        combo.SelectedIndex=Math.Max(0,list.FindIndex(i=>i.Value==selected));return combo;
    }
    public static string Value(ComboBox combo)=>((string[])combo.Tag!)[Math.Max(0,combo.SelectedIndex)];
    public static Slider Slider(double minimum,double maximum,double value,double step=1)=>new(){Minimum=minimum,Maximum=maximum,Value=value,TickFrequency=step,IsSnapToTickEnabled=step>0,MinWidth=220};
    /// A slider with its live value beside it.
    public static Control Labeled(Slider slider,Func<double,string> format)
    {
        var value=new TextBlock{Text=format(slider.Value),MinWidth=56,VerticalAlignment=VerticalAlignment.Center,Margin=new(10,0,0,0)};
        slider.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty)value.Text=format(slider.Value);};
        var grid=new Grid{ColumnDefinitions=new("*,Auto")};grid.Children.Add(slider);Grid.SetColumn(value,1);grid.Children.Add(value);return grid;
    }
    public static NumericUpDown Number(double value,double minimum,double maximum,double increment=1,string format="0")=>new(){Value=(decimal)value,Minimum=(decimal)minimum,Maximum=(decimal)maximum,Increment=(decimal)increment,FormatString=format,MinWidth=140};
    public static double Read(NumericUpDown number)=>(double)(number.Value??0);
    public static Bitmap Asset(string name)
    {
        using var stream=typeof(Ui).Assembly.GetManifestResourceStream("Nashi."+name+".png")??throw new FileNotFoundException(name);return new Bitmap(stream);
    }
    public static Bitmap FromPng(byte[] png){using var stream=new MemoryStream(png);return new Bitmap(stream);}
    public static string Size(long bytes)=>bytes switch{<1024=>$"{bytes} B",<1024*1024=>$"{bytes/1024d:0.#} KB",<1024L*1024*1024=>$"{bytes/1024d/1024:0.#} MB",_=>$"{bytes/1024d/1024/1024:0.##} GB"};
    /// Runs a UI task and shows its error instead of letting it escape the event loop.
    public static async Task Guard(Func<Task> work)
    {
        try{await work();}
        catch(OperationCanceledException){}
        catch(Exception error){await Dialogs.Message(null,"Couldn’t finish",error.Message);}
    }
    public static string Symbol(string id)=>id switch{"convert"=>"⇄","compress"=>"↘","removeMetadata"=>"◇","editImage"=>"☷","annotateImage"=>"✎","frameImage"=>"▣","cropImage" or "cropVideo"=>"⌗","trimAudio" or "trimVideo"=>"✂","muteVideo"=>"♫","normalizeAudio"=>"≋","audioChannels"=>"⇆","audioToVideo"=>"▷","createCollage"=>"▦","redactImage" or "redactVideo" or "redactAudio"=>"▨","readQRCode"=>"▧","mergePDF"=>"▣","splitPDF" or "splitVideo"=>"◫","organizePDF"=>"▤","extractArchive"=>"↗","createPDF"=>"▱","joinVideos"=>"⇥","videoSnapshots"=>"▣","changeVideoSpeed"=>"◴","share"=>"◉","float"=>"▣","copy"=>"▱","move"=>"↗","copyPath"=>"↪","terminal"=>">_","open"=>"↗","reveal"=>"⌂",_=>"↗"};
}
