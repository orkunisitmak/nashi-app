using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class ToolEditor
{
    // The preset that last filled the size fields; editing the fields by hand drops it.
    private (double? Scale,int? LongestEdge,string Width,string Height)? compressionPreset;
    private (double? Scale,int? LongestEdge)? CompressionPreset()=>compressionPreset is {} p&&fields["Width"].Text==p.Width&&fields["Height"].Text==p.Height?(p.Scale,p.LongestEdge):null;
    private void BuildCompressionOptions()
    {
        if(tool!="compress")return;
        if(bitmap is null)
        {
            var preset=new ComboBox{ItemsSource=new[]{"Balanced","Strong"},SelectedIndex=0};options.Children.Add(L10n.Label(new TextBlock{Text="Compression preset",Margin=new(4,12,4,0)}));options.Children.Add(preset);preset.SelectionChanged+=(_,_)=>fields["Quality (%)"].Text=preset.SelectedIndex==0?"80":"55";
            resetCompression=()=>preset.SelectedIndex=0;
        }
        else
        {
            // Pictures: what the file weighs now and what these settings will make of it, then the format and the strength.
            var quality=fields["Quality (%)"];var at=options.Children.IndexOf(quality);if(at>0)options.Children.RemoveAt(at-1);options.Children.Remove(quality);
            var card=new Border{CornerRadius=new(14),Padding=new(14,12,14,12),BorderThickness=new(1),Margin=new(0,2,0,2),Child=new StackPanel{Children={estimateText,estimateNote}}};card.SetResourceReference(Border.BackgroundProperty,"Well");card.SetResourceReference(Border.BorderBrushProperty,"Line");
            estimateText.FontSize=15;estimateText.FontWeight=FontWeights.SemiBold;estimateText.Margin=new(0);estimateText.SetResourceReference(TextBlock.ForegroundProperty,"Ink");estimateNote.Margin=new(0,3,0,0);estimateNote.FontSize=11.5;
            options.Children.Insert(options.Children.IndexOf(panelTitle)+1,card);
            var own=FeatureCatalog.Extension(input);var kinds=new[]{own,"webp","jpg"}.Where(k=>k is "jpg" or "png" or "webp" or "heic" or "avif").Distinct().ToArray();
            format.ItemsSource=kinds;format.SelectedIndex=0;format.Style=Ui.Style("SegmentedCombo");format.HorizontalAlignment=HorizontalAlignment.Left;format.DropDownOpened+=(_,_)=>format.IsDropDownOpen=false;
            options.Children.Add(L10n.Label(new TextBlock{Text="Format"}));options.Children.Add(format);
            var strength=new ComboBox{ItemsSource=new[]{"gentle","balanced","strong"},SelectedIndex=1,Style=Ui.Style("SegmentedCombo"),HorizontalAlignment=HorizontalAlignment.Left};strength.DropDownOpened+=(_,_)=>strength.IsDropDownOpen=false;
            strength.SelectionChanged+=(_,_)=>quality.Text=strength.SelectedIndex switch{0=>"90",2=>"60",_=>"80"};options.Children.Add(L10n.Label(new TextBlock{Text="Strength"}));options.Children.Add(strength);
            resetCompression=()=>{strength.SelectedIndex=1;format.SelectedIndex=0;};format.SelectionChanged+=(_,_)=>QueueEstimate();
        }
        Field("Target size (KB; 0 = auto)",0);Field("Reduction (%; 0 = auto)",0);
        if(bitmap is not null)
        {
            Field("Width",bitmap.PixelWidth);Field("Height",bitmap.PixelHeight);var size=new ComboBox{ItemsSource=new[]{"Original dimensions","75%","50%","25%","2560 px longest edge","1920 px longest edge","1280 px longest edge","Custom dimensions"},SelectedIndex=0};options.Children.Add(L10n.Label(new TextBlock{Text="Image size",Margin=new(4,12,4,0)}));options.Children.Add(size);size.SelectionChanged+=(_,_)=>{compressionPreset=null;if(size.SelectedIndex==7)return;var factor=size.SelectedIndex switch{1=>.75,2=>.5,3=>.25,4=>Math.Min(1,2560d/Math.Max(bitmap.PixelWidth,bitmap.PixelHeight)),5=>Math.Min(1,1920d/Math.Max(bitmap.PixelWidth,bitmap.PixelHeight)),6=>Math.Min(1,1280d/Math.Max(bitmap.PixelWidth,bitmap.PixelHeight)),_=>1};fields["Width"].Text=Number(Math.Max(1,Math.Round(bitmap.PixelWidth*factor)));fields["Height"].Text=Number(Math.Max(1,Math.Round(bitmap.PixelHeight*factor)));
                if(size.SelectedIndex is >=1 and <=6)compressionPreset=(size.SelectedIndex<=3?factor:null,size.SelectedIndex>=4?new[]{2560,1920,1280}[size.SelectedIndex-4]:null,fields["Width"].Text,fields["Height"].Text);};
            resetCompression+=()=>size.SelectedIndex=0;
            // Fewer knobs: exact sides only for Custom dimensions; the reduction share stays out of sight (the target size covers it).
            UIElement[] Pair(string name){var box=fields[name];var at=options.Children.IndexOf(box);return at>0?[options.Children[at-1],box]:[box];}
            var custom=Pair("Width").Concat(Pair("Height")).ToArray();void ShowCustom(){foreach(var item in custom)item.Visibility=size.SelectedIndex==7?Visibility.Visible:Visibility.Collapsed;}size.SelectionChanged+=(_,_)=>ShowCustom();ShowCustom();
            foreach(var item in Pair("Reduction (%; 0 = auto)"))item.Visibility=Visibility.Collapsed;
            estimate.Tick+=async(_,_)=>{estimate.Stop();await EstimateAsync();};foreach(var box in fields.Values)box.TextChanged+=(_,_)=>QueueEstimate();Loaded+=(_,_)=>QueueEstimate();Closed+=(_,_)=>estimate.Stop();
        }
    }
    private readonly TextBlock estimateText=new(),estimateNote=new();private readonly System.Windows.Threading.DispatcherTimer estimate=new(){Interval=TimeSpan.FromMilliseconds(300)};private int estimateVersion;
    private void QueueEstimate(){if(tool!="compress"||bitmap is null)return;estimate.Stop();estimate.Start();if(estimateText.Text.Length==0&&File.Exists(input))estimateText.Text=Weight(new FileInfo(input).Length);L10n.SetText(estimateNote,"Calculating…");}
    private static string Weight(double bytes)=>bytes>=1024*1024?(bytes/1024/1024).ToString("0.0",CultureInfo.CurrentCulture)+" MB":Math.Max(1,Math.Round(bytes/1024)).ToString("0",CultureInfo.CurrentCulture)+" KB";
    // Encodes the picture once in the background with these settings to show what the copy will weigh.
    private async Task EstimateAsync()
    {
        if(bitmap is null||!File.Exists(input))return;var version=++estimateVersion;var original=new FileInfo(input).Length;var kind=format.SelectedItem as string??"jpg";
        try
        {
            if(kind is "heic" or "avif"){estimateText.Text=Weight(original);L10n.SetText(estimateNote,"Nashi keeps the original if a copy would not be smaller.");return;}
            var width=Math.Clamp((int)Value("Width",bitmap.PixelWidth),1,30000);var height=Math.Clamp((int)Value("Height",bitmap.PixelHeight),1,30000);var quality=(int)Math.Clamp(Value("Quality (%)",80),1,100);var target=Value("Target size (KB; 0 = auto)")*1024;
            var share=Math.Min(1,Math.Sqrt(2_000_000d/((double)width*height)));var sw=Math.Max(1,(int)Math.Round(width*share));var sh=Math.Max(1,(int)Math.Round(height*share));
            BitmapSource scaled=sw!=bitmap.PixelWidth||sh!=bitmap.PixelHeight?new TransformedBitmap(bitmap,new ScaleTransform(sw/(double)bitmap.PixelWidth,sh/(double)bitmap.PixelHeight)):bitmap;
            var straight=new FormatConvertedBitmap(scaled,PixelFormats.Bgra32,null,0);int w=straight.PixelWidth,h=straight.PixelHeight;var pixels=new byte[w*h*4];straight.CopyPixels(pixels,w*4,0);
            var bytes=await Task.Run(()=>{var temp=Path.Combine(Path.GetTempPath(),"nashi-estimate-"+Guid.NewGuid().ToString("N")+"."+kind);try{Nashi.Core.PhotoCodec.EncodeCompressed(pixels,w,h,temp,kind,quality);return new FileInfo(temp).Length;}finally{if(File.Exists(temp))File.Delete(temp);}});
            if(version!=estimateVersion)return;bytes=(long)(bytes*((double)width*height/((double)w*h)));var shown=target>0?Math.Min(bytes,(long)target):bytes;
            if(shown>=original&&kind==FeatureCatalog.Extension(input)&&width==bitmap.PixelWidth&&height==bitmap.PixelHeight){estimateText.Text=Weight(original);L10n.SetText(estimateNote,"Already compact: Nashi keeps the original. Try WebP, a smaller size or Strong.");return;}
            estimateText.Text=$"{Weight(original)}  →  ~{Weight(shown)}";L10n.SetText(estimateNote,$"{Math.Max(0,1-shown/(double)original).ToString("P0",CultureInfo.CurrentCulture)} smaller"+(kind=="png"?", with fewer colors":""));
        }
        catch(Exception error)when(error is ArgumentException or IOException or ImageMagick.MagickException or InvalidOperationException or UnauthorizedAccessException){if(version==estimateVersion){estimateText.Text=Weight(original);L10n.SetText(estimateNote,"The size can't be estimated for these settings.");}}
    }
}
