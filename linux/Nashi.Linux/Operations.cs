using ImageMagick;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using ZXing;
namespace Nashi.Linux;

public sealed record Choice(string Id,string Title,string? Format=null);
/// Choices made in a tool's dialog; each tool reads only its own part.
public sealed record ToolSettings(MediaOptions? Media=null,ImageEdit? Crop=null,PhotoEdit? Photo=null,FrameOptions? Frame=null,CollageOptions? Collage=null,Redaction? Redact=null,PdfOrganization? Organize=null,Annotation? Annotate=null,DocumentOptions? Document=null,MetadataEdit? Metadata=null,CompressionOptions? Compress=null,OutputProfile? Profile=null);

/// The Linux preview offers only workflows implemented here; the shared catalog decides
/// which of them apply to a selection, so the rules match macOS and Windows.
public static class Operations
{
    private static readonly string[] ImageOutputs=["jpg","png","webp","tiff","avif","bmp","heic","pdf"];
    private static readonly string[] ImplementedTools=["compress","removeMetadata","cropImage","editImage","annotateImage","redactImage","frameImage","createCollage","createPDF","mergePDF","splitPDF","organizePDF","muteVideo","normalizeAudio","changeVideoSpeed","joinVideos","audioChannels","videoSnapshots","splitVideo","audioToVideo","trimVideo","trimAudio","cropVideo","redactVideo","redactAudio","extractArchive","readQRCode"];

    public static string? Executable(string name)
    {
        // Absolute PATH entries only: a relative entry would resolve against the launch folder.
        foreach(var folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(':',StringSplitOptions.RemoveEmptyEntries))
        {
            if(!Path.IsPathFullyQualified(folder))continue;var path=Path.Combine(folder,name);
            try{if(!OperatingSystem.IsWindows()&&File.Exists(path)&&(File.GetUnixFileMode(path)&UnixFileMode.UserExecute)!=0)return Path.GetFullPath(path);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
        }
        return null;
    }
    public static string? Office=>Executable("soffice")??Executable("libreoffice");
    /// Media and web tools: the user's installed ffmpeg, ffprobe, yt-dlp and deno first, downloads only for what is missing.
    public static Toolchain Toolset=>new(Path.Combine(LinuxPaths.DataDirectory,"tools"),Environment.GetEnvironmentVariable("PATH"));
    /// ExifTool, downloaded once into the tools folder like on Windows (it needs the system perl).
    public static MetadataService Exif=>new(Path.Combine(LinuxPaths.DataDirectory,"tools"));
    public static bool IsMediaOnly(IReadOnlyList<string> files)=>files.All(f=>FeatureCatalog.IsAudio(f)||FeatureCatalog.IsVideo(f)&&FeatureCatalog.Extension(f)!="gif");
    private static readonly string[] OfficeFree=["jpg","png","txt","md"];
    /// FFmpeg from Settings → Set up media tools, or the system's.
    public static string? Ffmpeg=>Find("ffmpeg");
    public static string? Ffprobe=>Find("ffprobe");
    private static string? Find(string name){try{return Toolset.Executable(name);}catch(FileNotFoundException){return null;}}
    public static MediaEngine? Media=>Ffmpeg is {} ffmpeg&&Ffprobe is {} ffprobe?new(ffmpeg,ffprobe):null;

    public static string[] Formats(IReadOnlyList<string> files)
    {
        var formats=FeatureCatalog.Formats(files);
        // The bundled ImageMagick decodes HEIC but encodes it through FFmpeg, as on Windows.
        if(files.All(FeatureCatalog.IsImage))return formats.Where(ImageOutputs.Contains).Where(f=>f!="heic"||Ffmpeg is not null).ToArray();
        if(files.All(f=>FeatureCatalog.IsVideo(f)||FeatureCatalog.IsAudio(f)))return Media is null?[]:formats.Where(f=>MediaEngine.AudioFormats.Contains(f)||MediaEngine.VideoFormats.Contains(f)).ToArray();
        if(files.All(FeatureCatalog.IsArchive))return formats.Where(f=>f is "zip" or "tar" or "gz").ToArray();
        if(files.All(IsSubtitle))return formats;
        // Documents go through LibreOffice; PDF pages and text do not need it. Pages files also need deno.
        var office=Office is not null;var pages=office&&Toolset.Ready;
        if(files.All(IsPdf))return formats.Where(f=>OfficeFree.Contains(f)||office&&(f!="pages"||pages)).ToArray();
        if(files.All(IsDocument))return office?formats.Where(f=>f!="pages"&&!files.Any(i=>FeatureCatalog.Extension(i)=="pages")||pages).ToArray():[];
        return [];
    }
    public static Choice[] Tools(IReadOnlyList<string> files)
    {
        var media=files.All(f=>FeatureCatalog.IsVideo(f)||FeatureCatalog.IsAudio(f));
        return FeatureCatalog.Tools(files).Where(ImplementedTools.Contains)
            .Where(id=>!media||Media is not null)
            .Where(id=>id!="readQRCode"||files.All(f=>FeatureCatalog.IsImage(f)||IsPdf(f)))
            .Select(id=>new Choice(id,FeatureCatalog.ToolNames[id])).ToArray();
    }

    public static async Task<IReadOnlyList<string>> RunAsync(Choice choice,IReadOnlyList<string> inputs,string destination,IProgress<OperationProgress> progress,CancellationToken token,ToolSettings? settings=null)
    {
        var (media,edit,photo,frame,collage,redact,organize,annotate,document,metadata,compress,profile)=settings??new();
        if(choice.Id=="convert")
        {
            var format=choice.Format!;
            // Settings → Format defaults; without a saved profile these are the Windows defaults.
            var p=profile??new OutputProfile();
            if(inputs.All(FeatureCatalog.IsImage))return await Task.Run(()=>format=="pdf"?ImagesToPdf(inputs,destination,progress,token):Images(inputs,destination,format,(int)Math.Round(p.Quality*100),p.PreserveMetadata,progress,token),token);
            if(inputs.All(f=>FeatureCatalog.IsVideo(f)||FeatureCatalog.IsAudio(f)))return await RequireMedia().ConvertAsync(inputs,format,destination,new MediaOptions{Quality=p.Quality,AudioBitrate=p.AudioBitrate,SampleDepth=p.SampleDepth,Height=p.MaxVideoHeight>0?p.MaxVideoHeight:null,GifFrameRate=p.GifFrameRate,GifWidth=p.GifWidth,PreserveMetadata=p.PreserveMetadata},progress,token);
            // Archives, subtitles, PDFs and documents: the conversion code ported from Windows.
            if(format!="txt"&&format!="md"&&format!="png"&&format!="jpg"&&!inputs.All(f=>FeatureCatalog.IsArchive(f)||IsSubtitle(f))&&Office is null)throw new InvalidOperationException("Install LibreOffice with your package manager to convert documents.");
            return await new DocumentConversion(Office,Toolset).ConvertAsync(inputs,format,destination,document??new DocumentOptions(p.Dpi,p.Layout,p.EpubLayout),progress,token);
        }
        return choice.Id switch
        {
            "cropImage"=>await Task.Run(()=>Images(inputs,destination,null,92,true,progress,token,image=>Crop(image,edit??throw new ArgumentException("Drag a crop inside the image."))),token),
            "editImage"=>await Task.Run(()=>Images(inputs,destination,null,92,true,progress,token,(photo??throw new ArgumentException("Choose photo adjustments.")).Apply),token),
            "annotateImage"=>await Task.Run(()=>Images(inputs,destination,null,92,true,progress,token,image=>Annotations.Apply(image,annotate??throw new ArgumentException("Draw at least one mark."))),token),
            "redactImage"=>await Task.Run(()=>Images(inputs,destination,null,92,true,progress,token,image=>Compositions.Redact(image,redact??throw new ArgumentException("Draw at least one area to redact."))),token),
            "frameImage"=>await Task.Run(()=>Images(inputs,destination,"png",92,true,progress,token,image=>Replace(image,Compositions.Frame(image,frame??new()))),token),
            "createCollage"=>await Task.Run(()=>Collage(inputs,destination,collage??new(),progress,token),token),
            "removeMetadata" when IsMediaOnly(inputs)=>await RequireMedia().ToolAsync("removeMetadata",inputs,destination,new MediaOptions{RemoveAllMetadata=metadata?.RemoveAll??true,Metadata=new(metadata?.Updates??new Dictionary<string,string>())},progress,token),
            // ExifTool edits tags in place of a re-encode, so image pixels and PDF content stay byte-identical.
            "removeMetadata" when Exif.Executable is not null=>await Exif.WriteAsync(inputs,destination,metadata?.RemoveAll??true,metadata is {RemoveAll:false} m?m.Updates:null,progress:progress,token:token),
            "removeMetadata" when metadata is {RemoveAll:false}=>throw new InvalidOperationException("Set up ExifTool to edit metadata fields."),
            "compress" when inputs.All(IsPdf)=>await Compression.PdfAsync(inputs,destination,compress??new(),progress,token),
            "removeMetadata" when inputs.All(IsPdf)=>await Task.Run(()=>DocumentEngine.Pdf(choice.Id,inputs,destination,null,token),token),
            "compress" when inputs.All(FeatureCatalog.IsImage)=>await Task.Run(()=>Compression.Images(inputs,destination,compress??new(),progress,token),token),
            "compress"=>await RequireMedia().ToolAsync("compress",inputs,destination,new MediaOptions{Quality=(compress?.Quality??80)/100d,TargetBytes=compress?.TargetBytes,ReductionPercent=compress?.ReductionPercent},progress,token),
            "removeMetadata" when inputs.All(FeatureCatalog.IsImage)=>await Task.Run(()=>Images(inputs,destination,null,92,false,progress,token),token),
            "createPDF"=>await Task.Run(()=>ImagesToPdf(inputs,destination,progress,token),token),
            "mergePDF"=>await Task.Run(()=>DocumentEngine.Pdf("mergePDFs",inputs,destination,null,token),token),
            "organizePDF"=>await Task.Run(()=>{var o=organize??throw new ArgumentException("Keep at least one page.");return DocumentEngine.Pdf("organizePDF",inputs,destination,o.Pages,token,pageRotations:o.Rotations,uniformWidth:o.UniformWidth);},token),
            "splitPDF"=>await Task.Run(()=>DocumentEngine.Pdf("splitPDF",inputs,destination,null,token),token),
            // One transaction for every archive, so a cancel or failure on the second leaves nothing behind.
            "extractArchive"=>await Task.Run(()=>{using var tx=new ExportTransaction(destination);var staged=inputs.SelectMany(i=>ArchiveEngine.Extract(i,tx.StagingDirectory,null,progress,token)).ToList();return tx.Publish(staged,token);},token),
            "compress" or "removeMetadata" or "muteVideo" or "normalizeAudio" or "changeVideoSpeed" or "joinVideos" or "audioChannels" or "videoSnapshots" or "splitVideo" or "audioToVideo" or "trimVideo" or "trimAudio" or "cropVideo" or "redactVideo" or "redactAudio"=>await RequireMedia().ToolAsync(choice.Id,inputs,destination,media??new MediaOptions(),progress,token),
            _=>throw new NotSupportedException("This tool is not available in the Linux preview yet.")
        };
    }
    /// A relative box in video pixels. FFmpeg needs at least 2×2 pixels inside the frame, also for a box
    /// drawn in the last column or row.
    public static MediaRegion Region(ImageEdit a,double w,double h){var x=Math.Max(0,Math.Min(Math.Floor(a.X*w),w-2));var y=Math.Max(0,Math.Min(Math.Floor(a.Y*h),h-2));return new(x,y,Math.Clamp(Math.Round(a.Width*w),2,w-x),Math.Clamp(Math.Round(a.Height*h),2,h-y));}
    /// One full-size video frame as PNG, for the crop and redact editors.
    public static async Task<byte[]> VideoFrameAsync(string input,double seconds,CancellationToken token=default)
    {
        var folder=Directory.CreateTempSubdirectory("nashi-frame-").FullName;
        try
        {
            var file=Path.Combine(folder,"frame.png");
            await ProcessRunner.RunAsync(Ffmpeg??throw new FileNotFoundException("Install FFmpeg with your package manager to use media tools."),["-hide_banner","-loglevel","error","-ss",seconds.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture),"-i",input,"-frames:v","1","-y",file],token);
            return File.Exists(file)?await File.ReadAllBytesAsync(file,token):throw new InvalidDataException("Could not read a frame from this video.");
        }
        finally{try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    private static MediaEngine RequireMedia()=>Media??throw new FileNotFoundException("Install FFmpeg with your package manager to use media tools.");

    /// Re-encodes images. A null format keeps each source format; metadata is kept only for conversions.
    public static IReadOnlyList<string> Images(IReadOnlyList<string> inputs,string destination,string? format,int quality,bool keepMetadata,IProgress<OperationProgress>? progress,CancellationToken token,Action<MagickImage>? transform=null)
    {
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();
        foreach(var input in inputs)
        {
            token.ThrowIfCancellationRequested();
            var ext=format??FeatureCatalog.Extension(input);if(ext=="svg")ext="png";
            using var image=PhotoCodec.Read(input);transform?.Invoke(image);
            if(!keepMetadata){var profile=image.GetColorProfile();image.Strip();if(profile is not null)image.SetProfile(profile);}
            image.Quality=(uint)quality;
            if(ext is "jpg" or "bmp"&&image.HasAlpha){image.BackgroundColor=MagickColors.White;image.Alpha(AlphaAction.Remove);}
            var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+"."+ext);
            if(ext=="heic")PhotoCodec.EncodePng(image.ToByteArray(MagickFormat.Png),output,ext,quality,Ffmpeg,token);
            else image.Write(output,PhotoCodec.Format(ext));
            outputs.Add(output);
            progress?.Report(new(outputs.Count/(double)inputs.Count*.98,"Saving images…"));
        }
        return tx.Publish(outputs,token);
    }

    /// The relative selection is applied to each image's own size, at least one pixel.
    public static void Crop(MagickImage image,ImageEdit edit)
    {
        if(new[]{edit.X,edit.Y,edit.Width,edit.Height}.Any(v=>!double.IsFinite(v))||edit.X<0||edit.Y<0||edit.Width<=0||edit.Height<=0||edit.X+edit.Width>1.0001||edit.Y+edit.Height>1.0001)throw new ArgumentException("Drag a crop inside the image.");
        var x=(int)Math.Round(edit.X*image.Width);var y=(int)Math.Round(edit.Y*image.Height);
        var w=(uint)Math.Clamp(Math.Round(edit.Width*image.Width),1,image.Width-x);var h=(uint)Math.Clamp(Math.Round(edit.Height*image.Height),1,image.Height-y);
        image.Crop(new MagickGeometry(x,y,w,h));image.ResetPage();
    }

    private static void Replace(MagickImage image,MagickImage result){using(result){var exif=image.GetExifProfile();image.Read(result.ToByteArray(MagickFormat.Png),new MagickReadSettings{Format=MagickFormat.Png});if(exif is not null){exif.SetValue(ExifTag.Orientation,(ushort)1);image.SetProfile(exif);}}}

    public static IReadOnlyList<string> Collage(IReadOnlyList<string> inputs,string destination,CollageOptions options,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);var images=new List<MagickImage>();
        try
        {
            foreach(var input in inputs){token.ThrowIfCancellationRequested();images.Add(PhotoCodec.Read(input));progress?.Report(new(images.Count/(double)inputs.Count*.7,"Reading images…"));}
            using var result=Compositions.Collage(images,options,token);progress?.Report(new(.9,"Saving collage…"));
            var file=Path.Combine(tx.StagingDirectory,"Collage.png");result.Write(file,MagickFormat.Png);return tx.Publish([file],token);
        }
        finally{foreach(var image in images)image.Dispose();}
    }

    public static IReadOnlyList<string> ImagesToPdf(IReadOnlyList<string> inputs,string destination,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);
        var file=Path.Combine(tx.StagingDirectory,inputs.Count==1?Path.GetFileNameWithoutExtension(inputs[0])+".pdf":"Images.pdf");
        using(var pdf=new PdfDocument())
        {
            var completed=0;
            foreach(var input in inputs)
            {
                token.ThrowIfCancellationRequested();
                using var source=PhotoCodec.Read(input);var width=source.Width;var height=source.Height;
                // PDFsharp reads only 8-bit RGB(A) PNGs; grayscale, palette and 16-bit PNGs would fail.
                using var png=new MemoryStream(source.ToByteArray(source.HasAlpha?MagickFormat.Png32:MagickFormat.Png24));using var image=XImage.FromStream(png);
                var page=pdf.AddPage();page.Width=XUnit.FromPoint(width*72/96d);page.Height=XUnit.FromPoint(height*72/96d);
                using(var g=XGraphics.FromPdfPage(page))g.DrawImage(image,0,0,page.Width.Point,page.Height.Point);
                progress?.Report(new(++completed/(double)inputs.Count*.98,"Adding pages…"));
            }
            pdf.Save(file);
        }
        return tx.Publish([file],token);
    }

    private static bool IsPdf(string file)=>FeatureCatalog.Extension(file)=="pdf";
    private static bool IsSubtitle(string file)=>FeatureCatalog.Extension(file) is "srt" or "vtt";
    public static bool IsDocument(string file)=>FeatureCatalog.Extension(file) is "docx" or "pages" or "md" or "rtf" or "rtfd" or "textclipping" or "html" or "epub" or "txt" or "xlsx" or "xls" or "ods" or "csv" or "tsv" or "ppt" or "pptx";
    /// Images and every PDF page.
    public static string[] ReadQr(IReadOnlyList<string> inputs)
    {
        var reader=new BarcodeReaderGeneric{AutoRotate=true,Options={TryHarder=true,TryInverted=true,PossibleFormats=[BarcodeFormat.QR_CODE]}};var found=new List<string>();
        void Decode(MagickImage image)
        {
            using var pixels=image.GetPixels();var rgb=pixels.ToByteArray(PixelMapping.RGB)!;
            var results=reader.DecodeMultiple(new RGBLuminanceSource(rgb,(int)image.Width,(int)image.Height,RGBLuminanceSource.BitmapFormat.RGB24));
            if(results is not null)found.AddRange(results.Select(r=>r.Text));
        }
        foreach(var input in inputs)
        {
            if(FeatureCatalog.IsImage(input)){using var image=PhotoCodec.Read(input);Decode(image);}
            // Each page at 180 DPI, as on Windows.
            else if(IsPdf(input)){using var pdf=PdfRaster.Open(input);for(var i=0;i<pdf.PageCount;i++){using var page=new MagickImage(pdf.RenderPage(i,180));Decode(page);}}
        }
        return found.Distinct().ToArray();
    }
}
