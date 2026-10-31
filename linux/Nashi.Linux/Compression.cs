using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ImageMagick;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
namespace Nashi.Linux;

/// Compress options as on Windows: a quality, optionally a target size or a reduction, and for
/// images a smaller size (a scale or a longest edge).
public sealed record CompressionOptions(int Quality=80,long? TargetBytes=null,double? ReductionPercent=null,double? Scale=null,int? LongestEdge=null);

public static class Compression
{
    public static long? Target(long original,long? bytes,double? percent)
    {
        if(bytes is <=0||percent is {} n&&(!double.IsFinite(n)||n<=0||n>=100))throw new ArgumentException("Choose a positive target size or a reduction between 0 and 100%.");
        var target=bytes??(percent is {} p?(long)(original*(1-p/100)):null);if(target is <=0)throw new ArgumentException("Choose a larger target size.");return target;
    }

    /// Lowers the quality first, then the size by 20% steps, until the target is met (24 tries, like Windows).
    /// A result larger than an unresized original in the same format is replaced by the original.
    public static IReadOnlyList<string> Images(IReadOnlyList<string> inputs,string destination,CompressionOptions options,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        if(options.Quality is <1 or >100)throw new ArgumentException("Choose a quality between 1 and 100%.");
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();
        foreach(var input in inputs)
        {
            var ext=FeatureCatalog.Extension(input);if(ext=="svg")ext="png";
            var target=Target(new FileInfo(input).Length,options.TargetBytes,options.ReductionPercent);
            using var image=PhotoCodec.Read(input);var profile=image.GetColorProfile();image.Strip();if(profile is not null)image.SetProfile(profile);
            var resized=false;
            if(options.Scale is {} scale&&scale is >0 and <1){image.Resize((uint)Math.Max(1,Math.Round(image.Width*scale)),(uint)Math.Max(1,Math.Round(image.Height*scale)));resized=true;}
            else if(options.LongestEdge is {} edge&&Math.Max(image.Width,image.Height)>edge){image.Resize(new MagickGeometry((uint)edge,(uint)edge));resized=true;}
            var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+"."+ext);var quality=options.Quality;var done=false;
            for(var n=0;n<24&&!done;n++)
            {
                token.ThrowIfCancellationRequested();progress?.Report(new(null,$"Fitting image · attempt {n+1}…"));
                using(var copy=(MagickImage)image.Clone()){copy.Quality=(uint)quality;if(ext is "jpg" or "bmp"&&copy.HasAlpha){copy.BackgroundColor=MagickColors.White;copy.Alpha(AlphaAction.Remove);}
                    if(ext=="heic")PhotoCodec.EncodePng(copy.ToByteArray(MagickFormat.Png),output,ext,quality,Operations.Ffmpeg,token);else copy.Write(output,PhotoCodec.Format(ext));}
                if(target is null||new FileInfo(output).Length<=target){done=true;break;}
                if(quality>15&&ext is not ("png" or "bmp" or "tiff"))quality=Math.Max(1,(int)(quality*.72));
                else{image.Resize((uint)Math.Max(1,(int)(image.Width*.8)),(uint)Math.Max(1,(int)(image.Height*.8)));resized=true;}
            }
            if(!done)throw new IOException("The requested image size could not be reached. Choose a larger target.");
            if(!resized&&FeatureCatalog.Extension(input)==ext&&new FileInfo(input).Length<new FileInfo(output).Length)File.Copy(input,output,true);
            outputs.Add(output);progress?.Report(new(outputs.Count/(double)inputs.Count*.98,"Saving images…"));
        }
        return tx.Publish(outputs,token);
    }

    /// Ported from Windows: PDFs with text, forms, links or annotations are rewritten losslessly and keep
    /// all of that; scanned PDFs are re-rendered page by page as JPEG until they fit.
    public static async Task<IReadOnlyList<string>> PdfAsync(IReadOnlyList<string> inputs,string destination,CompressionOptions options,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();
        foreach(var input in inputs)
        {
            var original=new FileInfo(input).Length;var target=Target(original,options.TargetBytes,options.ReductionPercent);
            using var source=PdfReader.Open(input,PdfDocumentOpenMode.Import);var catalog=source.Internals.Catalog.Elements;
            var featureful=catalog.ContainsKey("/AcroForm")||catalog.ContainsKey("/Names")||catalog.ContainsKey("/Outlines")||catalog.ContainsKey("/AF")||catalog.ContainsKey("/Collection")||source.Pages.Cast<PdfPage>().Any(p=>p.Elements.ContainsKey("/Annots"));
            if(featureful||!string.IsNullOrWhiteSpace(OfficeDocuments.PdfText(input)))
            {
                string file;
                if(featureful){file=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Compressed.pdf");File.Copy(input,file);}
                else file=DocumentEngine.Pdf("compress",[input],tx.StagingDirectory,token:token)[0];
                if(new FileInfo(file).Length>original)File.Copy(input,file,true);
                if(target is {} limit&&new FileInfo(file).Length>limit)throw new IOException("That PDF size cannot be reached while keeping selectable text, links and annotations. Choose a larger target.");
                outputs.Add(file);continue;
            }
            var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Compressed.pdf");var dpi=144;var quality=options.Quality;var success=false;
            for(var attempt=0;attempt<14;attempt++)
            {
                token.ThrowIfCancellationRequested();if(File.Exists(output))File.Delete(output);
                var pages=await Task.Run(()=>PdfRaster.RenderAll(input,dpi,token),token);
                using(var pdf=new PdfDocument())
                {
                    for(var n=0;n<pages.Length;n++)
                    {
                        token.ThrowIfCancellationRequested();using var page=new MagickImage(pages[n]);page.Quality=(uint)Math.Clamp(quality,1,100);
                        using var jpeg=new MemoryStream(page.ToByteArray(MagickFormat.Jpeg));using var picture=XImage.FromStream(jpeg);
                        var originalPage=source.Pages[n];var rotated=originalPage.Rotate%180!=0;var added=pdf.AddPage();added.Width=rotated?originalPage.Height:originalPage.Width;added.Height=rotated?originalPage.Width:originalPage.Height;
                        using(var drawing=XGraphics.FromPdfPage(added))drawing.DrawImage(picture,0,0,added.Width.Point,added.Height.Point);
                        progress?.Report(new(null,$"Compressing PDF · attempt {attempt+1} · page {n+1}/{pages.Length}…"));
                    }
                    pdf.Save(output);
                }
                if(target is null||new FileInfo(output).Length<=target){success=true;break;}
                dpi=Math.Max(36,(int)(dpi*.8));quality=Math.Max(5,(int)(quality*.75));
            }
            if(!success&&!(target is {} fits&&original<=fits))throw new IOException("The requested PDF size could not be reached. Choose a larger target.");
            if(!success||new FileInfo(output).Length>original)File.Copy(input,output,true);outputs.Add(output);
        }
        return tx.Publish(outputs,token);
    }
}

/// Balanced/Strong preset, quality, target size or reduction and, for images, a smaller size.
internal sealed class CompressDialog:Window
{
    private readonly ComboBox preset=new(){ItemsSource=new[]{"Balanced","Strong"},SelectedIndex=0,MinWidth=160};
    private readonly NumericUpDown quality=new(){Minimum=1,Maximum=100,Value=80,FormatString="0",Width=140};
    private readonly NumericUpDown target=new(){Minimum=0,Maximum=10_000_000,Value=0,FormatString="0",Width=140};
    private readonly NumericUpDown reduction=new(){Minimum=0,Maximum=99,Value=0,FormatString="0",Width=140};
    private readonly ComboBox size=new(){ItemsSource=new[]{"Original dimensions","75%","50%","25%","2560 px longest edge","1920 px longest edge","1280 px longest edge"},SelectedIndex=0,MinWidth=200};
    private readonly TextBlock problem=new(){Foreground=Brushes.IndianRed,TextWrapping=TextWrapping.Wrap};
    public CompressionOptions? Result{get;private set;}
    public CompressDialog(string title,bool images,bool media)
    {
        Title=title;SizeToContent=SizeToContent.WidthAndHeight;CanResize=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        preset.SelectionChanged+=(_,_)=>quality.Value=preset.SelectedIndex==0?80:55;
        static Control Row(string label,Control control)=>new DockPanel{Children={new TextBlock{Text=label,Width=210,VerticalAlignment=VerticalAlignment.Center},control}};
        var panel=new StackPanel{Margin=new(20),Spacing=10,Children={new TextBlock{Text=title,FontWeight=Avalonia.Media.FontWeight.SemiBold,FontSize=16},Row("Compression preset",preset),Row("Quality (%)",quality),Row("Target size (KB; 0 = auto)",target),Row("Reduction (%; 0 = auto)",reduction)}};
        if(images)panel.Children.Add(Row("Image size",size));
        if(media)panel.Children.Add(new TextBlock{Text="Media is re-encoded until it fits the target; audio keeps its sample rate.",Opacity=.7,FontSize=12,TextWrapping=TextWrapping.Wrap,MaxWidth=420});
        var ok=new Button{Content="Compress",IsDefault=true,FontWeight=Avalonia.Media.FontWeight.SemiBold,Background=NashiTheme.Primary,Foreground=new SolidColorBrush(Color.Parse("#243B2B"))};ok.Click+=(_,_)=>Accept();
        var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>Close();
        panel.Children.Add(problem);panel.Children.Add(new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right,Children={cancel,ok}});
        this[!BackgroundProperty]=new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Paper");Content=panel;
    }
    public CompressionOptions Current
    {
        get
        {
            var kb=(long)(target.Value??0);var percent=(double)(reduction.Value??0);
            if(kb>0&&percent>0)throw new ArgumentException("Choose either a target size or a reduction, not both.");
            return new((int)(quality.Value??80),kb>0?kb*1024:null,percent>0?percent:null,size.SelectedIndex switch{1=>.75,2=>.5,3=>.25,_=>null},size.SelectedIndex switch{4=>2560,5=>1920,6=>1280,_=>null});
        }
    }
    private void Accept(){try{Result=Current;Close();}catch(ArgumentException error){problem.Text=Localizer.T(error.Message);}}
}
