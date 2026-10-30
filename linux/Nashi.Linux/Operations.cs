using ImageMagick;
using Nashi.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using ZXing;
namespace Nashi.Linux;

public sealed record Choice(string Id,string Title,string? Format=null);
/// Choices made in a tool's dialog; each tool reads only its own part.
public sealed record ToolSettings(MediaOptions? Media=null,ImageEdit? Crop=null,PhotoEdit? Photo=null,FrameOptions? Frame=null,CollageOptions? Collage=null,Redaction? Redact=null);

/// The Linux preview offers only workflows implemented here; the shared catalog decides
/// which of them apply to a selection, so the rules match macOS and Windows.
public static class Operations
{
    private static readonly string[] ImageOutputs=["jpg","png","webp","tiff","avif","bmp","heic","pdf"];
    private static readonly string[] ImplementedTools=["compress","removeMetadata","cropImage","editImage","redactImage","frameImage","createCollage","createPDF","mergePDF","splitPDF","muteVideo","normalizeAudio","changeVideoSpeed","joinVideos","audioChannels","videoSnapshots","splitVideo","audioToVideo","trimVideo","trimAudio","cropVideo","redactVideo","redactAudio","extractArchive","readQRCode"];

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
    public static MediaEngine? Media=>Executable("ffmpeg") is {} ffmpeg&&Executable("ffprobe") is {} ffprobe?new(ffmpeg,ffprobe):null;

    public static string[] Formats(IReadOnlyList<string> files)
    {
        var formats=FeatureCatalog.Formats(files);
        // The bundled ImageMagick decodes HEIC but encodes it through FFmpeg, as on Windows.
        if(files.All(FeatureCatalog.IsImage))return formats.Where(ImageOutputs.Contains).Where(f=>f!="heic"||Executable("ffmpeg") is not null).ToArray();
        if(files.All(f=>FeatureCatalog.IsVideo(f)||FeatureCatalog.IsAudio(f)))return Media is null?[]:formats.Where(f=>MediaEngine.AudioFormats.Contains(f)||MediaEngine.VideoFormats.Contains(f)).ToArray();
        if(files.All(FeatureCatalog.IsArchive))return formats.Where(f=>f is "zip" or "tar" or "gz").ToArray();
        if(files.All(f=>FeatureCatalog.Extension(f)=="pdf"))return formats.Where(f=>f=="txt").ToArray();
        return [];
    }
    public static Choice[] Tools(IReadOnlyList<string> files)
    {
        var media=files.All(f=>FeatureCatalog.IsVideo(f)||FeatureCatalog.IsAudio(f));
        return FeatureCatalog.Tools(files).Where(ImplementedTools.Contains)
            .Where(id=>!media||Media is not null)
            .Where(id=>id is not ("removeMetadata" or "readQRCode")||files.All(FeatureCatalog.IsImage))
            .Where(id=>id!="compress"||!files.Any(f=>FeatureCatalog.Extension(f)=="pdf"))
            .Select(id=>new Choice(id,FeatureCatalog.ToolNames[id])).ToArray();
    }

    public static async Task<IReadOnlyList<string>> RunAsync(Choice choice,IReadOnlyList<string> inputs,string destination,IProgress<OperationProgress> progress,CancellationToken token,ToolSettings? settings=null)
    {
        var (media,edit,photo,frame,collage,redact)=settings??new();
        if(choice.Id=="convert")
        {
            var format=choice.Format!;
            if(inputs.All(FeatureCatalog.IsImage))return await Task.Run(()=>format=="pdf"?ImagesToPdf(inputs,destination,progress,token):Images(inputs,destination,format,85,true,progress,token),token);
            if(inputs.All(FeatureCatalog.IsArchive))return await Task.Run(()=>inputs.SelectMany(i=>ArchiveEngine.Convert(i,format,destination,null,progress,token)).ToArray(),token);
            if(inputs.All(f=>FeatureCatalog.Extension(f)=="pdf"))return await Task.Run(()=>PdfText(inputs,destination,token),token);
            return await RequireMedia().ConvertAsync(inputs,format,destination,new MediaOptions(),progress,token);
        }
        return choice.Id switch
        {
            "cropImage"=>await Task.Run(()=>Images(inputs,destination,null,92,true,progress,token,image=>Crop(image,edit??throw new ArgumentException("Drag a crop inside the image."))),token),
            "editImage"=>await Task.Run(()=>Images(inputs,destination,null,92,true,progress,token,(photo??throw new ArgumentException("Choose photo adjustments.")).Apply),token),
            "redactImage"=>await Task.Run(()=>Images(inputs,destination,null,92,true,progress,token,image=>Compositions.Redact(image,redact??throw new ArgumentException("Draw at least one area to redact."))),token),
            "frameImage"=>await Task.Run(()=>Images(inputs,destination,"png",92,true,progress,token,image=>Replace(image,Compositions.Frame(image,frame??new()))),token),
            "createCollage"=>await Task.Run(()=>Collage(inputs,destination,collage??new(),progress,token),token),
            "compress" when inputs.All(FeatureCatalog.IsImage)=>await Task.Run(()=>Images(inputs,destination,null,70,false,progress,token),token),
            "removeMetadata"=>await Task.Run(()=>Images(inputs,destination,null,92,false,progress,token),token),
            "createPDF"=>await Task.Run(()=>ImagesToPdf(inputs,destination,progress,token),token),
            "mergePDF"=>await Task.Run(()=>DocumentEngine.Pdf("mergePDFs",inputs,destination,null,token),token),
            "splitPDF"=>await Task.Run(()=>DocumentEngine.Pdf("splitPDF",inputs,destination,null,token),token),
            "extractArchive"=>await Task.Run(()=>inputs.SelectMany(i=>ArchiveEngine.Extract(i,destination,null,progress,token)).ToArray(),token),
            "compress" or "muteVideo" or "normalizeAudio" or "changeVideoSpeed" or "joinVideos" or "audioChannels" or "videoSnapshots" or "splitVideo" or "audioToVideo" or "trimVideo" or "trimAudio" or "cropVideo" or "redactVideo" or "redactAudio"=>await RequireMedia().ToolAsync(choice.Id,inputs,destination,media??new MediaOptions(),progress,token),
            _=>throw new NotSupportedException("This tool is not available in the Linux preview yet.")
        };
    }
    /// One full-size video frame as PNG, for the crop and redact editors.
    public static async Task<byte[]> VideoFrameAsync(string input,double seconds,CancellationToken token=default)
    {
        var folder=Directory.CreateTempSubdirectory("nashi-frame-").FullName;
        try
        {
            var file=Path.Combine(folder,"frame.png");
            await ProcessRunner.RunAsync(Executable("ffmpeg")??throw new FileNotFoundException("Install FFmpeg with your package manager to use media tools."),["-hide_banner","-loglevel","error","-ss",seconds.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture),"-i",input,"-frames:v","1","-y",file],token);
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
            if(ext=="heic")PhotoCodec.EncodePng(image.ToByteArray(MagickFormat.Png),output,ext,quality,Executable("ffmpeg"),token);
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
                using var png=new MemoryStream(source.ToByteArray(MagickFormat.Png));using var image=XImage.FromStream(png);
                var page=pdf.AddPage();page.Width=XUnit.FromPoint(width*72/96d);page.Height=XUnit.FromPoint(height*72/96d);
                using(var g=XGraphics.FromPdfPage(page))g.DrawImage(image,0,0,page.Width.Point,page.Height.Point);
                progress?.Report(new(++completed/(double)inputs.Count*.98,"Adding pages…"));
            }
            pdf.Save(file);
        }
        return tx.Publish([file],token);
    }

    private static IReadOnlyList<string> PdfText(IReadOnlyList<string> inputs,string destination,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();
        foreach(var input in inputs)
        {
            token.ThrowIfCancellationRequested();var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+".txt");
            var text=OfficeDocuments.PdfText(input);if(string.IsNullOrWhiteSpace(text))throw new InvalidDataException("This PDF has no selectable text.");
            File.WriteAllText(output,text);outputs.Add(output);
        }
        return tx.Publish(outputs,token);
    }

    /// Images only in the Linux preview; PDF pages need a renderer first.
    public static string[] ReadQr(IReadOnlyList<string> inputs)
    {
        var reader=new BarcodeReaderGeneric{AutoRotate=true,Options={TryHarder=true,TryInverted=true,PossibleFormats=[BarcodeFormat.QR_CODE]}};var found=new List<string>();
        foreach(var input in inputs.Where(FeatureCatalog.IsImage))
        {
            using var image=PhotoCodec.Read(input);using var pixels=image.GetPixels();var rgb=pixels.ToByteArray(PixelMapping.RGB)!;
            var results=reader.DecodeMultiple(new RGBLuminanceSource(rgb,(int)image.Width,(int)image.Height,RGBLuminanceSource.BitmapFormat.RGB24));
            if(results is not null)found.AddRange(results.Select(r=>r.Text));
        }
        return found.Distinct().ToArray();
    }
}
