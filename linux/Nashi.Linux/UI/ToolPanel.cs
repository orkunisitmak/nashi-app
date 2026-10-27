using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Nashi.Core;
using Nashi.Linux.Engine;
namespace Nashi.Linux.UI;

/// Options for one tool, the Linux counterpart of the Windows ToolEditor. It
/// only collects values; Operations validates and runs them like the CLI does.
internal sealed class ToolPanel:StackPanel
{
    private readonly List<Func<JobSettings,JobSettings>> readers=[];
    private readonly WrapPanel row=new(){Orientation=Orientation.Horizontal};
    public MediaInfo? Info{get;private set;}
    private ToolPanel(){Spacing=8;Children.Add(row);}

    public static async Task<ToolPanel> CreateAsync(string id,string? format,IReadOnlyList<string> inputs,PreferencesStore preferences,Operations operations,CancellationToken token)
    {
        var panel=new ToolPanel();var input=inputs[0];var profile=format is null?new OutputProfile():preferences.Profile(format);
        var images=Operations.Images(inputs);var media=Operations.Media(inputs);
        if(media&&operations.Tools.MediaReady&&id!="convert")panel.Info=await new MediaEngine(operations.Tools.Executable("ffmpeg"),operations.Tools.Executable("ffprobe")).InspectAsync(input,token);
        switch(id)
        {
            case "convert":
                if(images&&format is "jpg" or "webp" or "heic" or "avif"||media&&format is not ("wav" or "flac" or "aiff"))panel.Quality((int)Math.Round(profile.Quality*100));
                if(images&&format is not ("pdf" or "bmp" or "gif"))panel.Check("Keep metadata",profile.PreserveMetadata,(s,v)=>s with{PreserveImageMetadata=v});
                if(media&&FeatureCatalog.IsVideo(input)&&format is not ("mp3" or "gif"))panel.Number("Height",profile.MaxVideoHeight>0?profile.MaxVideoHeight.ToString(CultureInfo.InvariantCulture):"",(s,v)=>s with{Media=s.Media with{Height=v is null?null:(int)v}});
                if(!images&&!media)
                {
                    if(format is "png" or "jpg"||format=="epub")panel.Number("DPI",profile.Dpi.ToString(CultureInfo.InvariantCulture),(s,v)=>s with{Document=s.Document with{Dpi=(int)(v??144)}});
                    if(FeatureCatalog.Extension(input)=="pdf"&&format is "docx" or "pages")panel.Choice("Layout",[("editable","Editable"),("preserve","Preserve layout")],profile.Layout,(s,v)=>s with{Document=s.Document with{Layout=v}});
                    if(format=="epub")panel.Choice("Layout",[("reflowable","Reflowable"),("fixed","Fixed layout")],profile.EpubLayout,(s,v)=>s with{Document=s.Document with{EpubLayout=v}});
                    if(FeatureCatalog.Extension(input) is "pdf" or "zip" or "rar")panel.Password();
                }
                break;
            case "resize":
                panel.Number("Width","",(s,v)=>s with{Image=s.Image with{Width=v is null?null:(int)v}});panel.Number("Height","",(s,v)=>s with{Image=s.Image with{Height=v is null?null:(int)v}});break;
            case "rotate":panel.Choice("Angle",[("90","90°"),("180","180°"),("270","270°")],"90",(s,v)=>s with{Image=s.Image with{Rotation=int.Parse(v,CultureInfo.InvariantCulture)}});break;
            case "flip":panel.Choice("Flip",[("h","Horizontal"),("v","Vertical")],"h",(s,v)=>s with{Image=s.Image with{Horizontal=v=="h"}});break;
            case "cropImage":case "redactImage":case "annotateImage":
            {
                var picker=new RegionPicker(new Bitmap(new MemoryStream(PhotoCodec.Png(input))),id!="cropImage");panel.Hint(id=="cropImage"?"Drag on the preview to select an area.":"Drag on the preview to add areas. Right-click removes the last one.");panel.Children.Add(picker);
                panel.readers.Add(s=>s with{Image=s.Image with{Crop=picker.Regions.Count>0?picker.Regions[0]:null,Regions=picker.Regions.ToArray()}});
                if(id=="redactImage")panel.Choice("Style",[("solid","Solid"),("pixelate","Pixelate"),("blur","Blur")],"solid",(s,v)=>s with{Image=s.Image with{RedactionStyle=v}});
                if(id=="annotateImage")panel.Text("Caption","",(s,v)=>s with{Image=s.Image with{AnnotationText=v}},260);
                break;
            }
            case "editImage":
                panel.Slider("Exposure",-2,2,0,(s,v)=>s with{Image=s.Image with{Adjustments=s.Image.Adjustments with{Exposure=v}}});
                panel.Slider("Brightness",-.5,.5,0,(s,v)=>s with{Image=s.Image with{Adjustments=s.Image.Adjustments with{Brightness=v}}});
                panel.Slider("Contrast",0,2,1,(s,v)=>s with{Image=s.Image with{Adjustments=s.Image.Adjustments with{Contrast=v}}});
                panel.Slider("Saturation",0,2,1,(s,v)=>s with{Image=s.Image with{Adjustments=s.Image.Adjustments with{Saturation=v}}});
                panel.Slider("Warmth",-1,1,0,(s,v)=>s with{Image=s.Image with{Adjustments=s.Image.Adjustments with{Temperature=v}}});
                panel.Slider("Sharpen",0,2,0,(s,v)=>s with{Image=s.Image with{Adjustments=s.Image.Adjustments with{Sharpen=v}}});
                panel.Slider("Vignette",0,1,0,(s,v)=>s with{Image=s.Image with{Adjustments=s.Image.Adjustments with{Vignette=v}}});
                break;
            case "frameImage":
                panel.Number("Padding","80",(s,v)=>s with{Image=s.Image with{Frame=s.Image.Frame with{Padding=(int)(v??80)}}});
                panel.Number("Corner radius","24",(s,v)=>s with{Image=s.Image with{Frame=s.Image.Frame with{CornerRadius=v??24}}});
                panel.Text("Background color","#FFFFFF",(s,v)=>s with{Image=s.Image with{Frame=s.Image.Frame with{Color=v.Trim()}}});
                panel.Choice("Style",[("solid","Solid"),("gradient","Gradient")],"solid",(s,v)=>s with{Image=s.Image with{Frame=s.Image.Frame with{Backdrop=v}}});
                break;
            case "createCollage":
                panel.Number("Columns","2",(s,v)=>s with{Image=s.Image with{Collage=s.Image.Collage with{Columns=(int)(v??2)}}});
                panel.Number("Width","2048",(s,v)=>s with{Image=s.Image with{Collage=s.Image.Collage with{Width=(int)(v??2048)}}});
                panel.Number("Height","2048",(s,v)=>s with{Image=s.Image with{Collage=s.Image.Collage with{Height=(int)(v??2048)}}});
                panel.Text("Background color","#FFFFFF",(s,v)=>s with{Image=s.Image with{Collage=s.Image.Collage with{Color=v.Trim()}}});
                break;
            case "compress":
                panel.Number("Reduce by %","",(s,v)=>s with{Image=s.Image with{ReductionPercent=v},Media=s.Media with{ReductionPercent=v}});
                if(!media)panel.Quality(85);
                if(FeatureCatalog.Extension(input)=="pdf")panel.Password();
                break;
            case "trimVideo":case "trimAudio":
                panel.Number("Start (s)","0",(s,v)=>s with{Media=s.Media with{Start=v??0}});
                panel.Number("End (s)",panel.Info is {Duration:>0} d?d.Duration.ToString("0.###",CultureInfo.InvariantCulture):"",(s,v)=>s with{Media=s.Media with{End=v}});break;
            case "changeVideoSpeed":panel.Choice("Speed",[("0.25","0.25×"),("0.5","0.5×"),("1.5","1.5×"),("2","2×"),("4","4×")],"2",(s,v)=>s with{Media=s.Media with{Speed=double.Parse(v,CultureInfo.InvariantCulture)}});break;
            case "audioChannels":panel.Choice("Channels",[("1","Mono"),("2","Stereo")],"1",(s,v)=>s with{Media=s.Media with{Channels=int.Parse(v,CultureInfo.InvariantCulture)}});break;
            case "videoSnapshots":panel.Text("Snapshot times (s)","0",(s,v)=>s with{Media=s.Media with{SnapshotTimes=Numbers(v)}},200);break;
            case "splitVideo":panel.Number("Clip length (s)","30",(s,v)=>s with{Media=s.Media with{SplitInterval=v??30}});break;
            case "audioToVideo":panel.Choice("Visualization",[("waveform","Waveform"),("spectrum","Spectrum"),("bars","Bars")],"waveform",(s,v)=>s with{Media=s.Media with{Visualization=v}});break;
            case "redactAudio":
                panel.Text("Start–end (s)","0-1",(s,v)=>s with{Media=s.Media with{AudioRedactions=v.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(r=>{var n=Numbers(r.Replace('–','-').Replace('-',' '));if(n.Length!=2)throw new ArgumentException("Use ranges such as 10-12, 30-31.");return new MediaRange(n[0],n[1]);}).ToArray()}},220);
                panel.Choice("Style",[("bleep","Bleep"),("mute","Mute")],"bleep",(s,v)=>s with{Media=s.Media with{RedactionMode=v}});break;
            case "cropVideo":case "redactVideo":
            {
                if(!operations.Tools.MediaReady)break;
                var frame=Path.Combine(Path.GetTempPath(),"Nashi-preview-"+Guid.NewGuid().ToString("N")+".png");
                try{await ProcessRunner.RunAsync(operations.Tools.Executable("ffmpeg"),["-v","error","-i",input,"-frames:v","1",frame],token);var picker=new RegionPicker(new Bitmap(frame),id=="redactVideo");panel.Hint(id=="cropVideo"?"Drag on the preview to select an area.":"Drag on the preview to add areas. Right-click removes the last one.");panel.Children.Add(picker);
                    panel.readers.Add(s=>s with{Media=s.Media with{Regions=picker.Regions.Select(r=>new MediaRegion(r.X,r.Y,r.Width,r.Height)).ToArray()}});}
                finally{if(File.Exists(frame))File.Delete(frame);}
                if(id=="redactVideo")panel.Choice("Style",[("solid","Solid"),("pixelate","Pixelate"),("blur","Blur")],"solid",(s,v)=>s with{Media=s.Media with{RedactionMode=v}});
                break;
            }
            case "splitPDF":panel.Number("Pages per file","1",(s,v)=>s with{PagesPerFile=(int)(v??1)});panel.Password();break;
            case "organizePDF":panel.Text("Pages (e.g. 1,3,5)","",(s,v)=>s with{Pages=string.IsNullOrWhiteSpace(v)?null:Numbers(v).Select(n=>(int)n).ToArray()},200);panel.Password();break;
            case "mergePDF":case "extractArchive":panel.Password();break;
        }
        return panel;
    }
    public JobSettings Settings(JobSettings initial)=>readers.Aggregate(initial,(s,read)=>read(s));
    private static double[] Numbers(string text)=>text.Split([',',' ',';'],StringSplitOptions.RemoveEmptyEntries).Select(n=>double.Parse(n,NumberStyles.Float,CultureInfo.InvariantCulture)).ToArray();
    private static StackPanel Labeled(string label,Control control)
    {
        var stack=new StackPanel{Margin=new(0,0,14,8),Spacing=4};stack.Children.Add(Brand.Text(label,11,"Muted"));stack.Children.Add(control);return stack;
    }
    private void Hint(string text)=>Children.Add(Brand.Text(text,12,"Muted"));
    private void Text(string label,string value,Func<JobSettings,string,JobSettings> apply,double width=110)
    {
        var box=new TextBox{Text=value,Width=width};row.Children.Add(Labeled(label,box));readers.Add(s=>apply(s,box.Text??""));
    }
    private void Number(string label,string value,Func<JobSettings,double?,JobSettings> apply)
    {
        var box=new TextBox{Text=value,Width=110};row.Children.Add(Labeled(label,box));
        readers.Add(s=>{var text=(box.Text??"").Trim().Replace(',','.');if(text.Length==0)return apply(s,null);
            if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n))throw new ArgumentException(L10n.T(label)+": "+L10n.T("Enter a number."));return apply(s,n);});
    }
    private void Choice(string label,(string Value,string Title)[] items,string selected,Func<JobSettings,string,JobSettings> apply)
    {
        var box=new ComboBox{ItemsSource=items.Select(i=>L10n.T(i.Title)).ToArray(),SelectedIndex=Math.Max(0,Array.FindIndex(items,i=>i.Value==selected)),MinWidth=130};
        row.Children.Add(Labeled(label,box));readers.Add(s=>apply(s,items[Math.Max(0,box.SelectedIndex)].Value));
    }
    private void Check(string label,bool value,Func<JobSettings,bool,JobSettings> apply)
    {
        var box=new CheckBox{Content=L10n.T(label),IsChecked=value,Margin=new(0,18,14,8)};row.Children.Add(box);readers.Add(s=>apply(s,box.IsChecked==true));
    }
    private void Slider(string label,double minimum,double maximum,double value,Func<JobSettings,double,JobSettings> apply)
    {
        var slider=new Slider{Minimum=minimum,Maximum=maximum,Value=value,Width=170};row.Children.Add(Labeled(label,slider));readers.Add(s=>apply(s,Math.Round(slider.Value,3)));
    }
    private void Quality(int value)
    {
        var slider=new Slider{Minimum=10,Maximum=100,Value=value,Width=170,TickFrequency=5,IsSnapToTickEnabled=true};row.Children.Add(Labeled("Quality",slider));
        readers.Add(s=>s with{Image=s.Image with{Quality=(int)slider.Value},Media=s.Media with{Quality=slider.Value/100}});
    }
    private void Password()
    {
        var box=new TextBox{PasswordChar='•',Width=160};row.Children.Add(Labeled("Password (optional)",box));readers.Add(s=>s with{Document=s.Document with{Password=box.Text??""}});
    }
}
