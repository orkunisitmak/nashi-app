using System.Diagnostics;
using ImageMagick;
using Nashi.Core;
namespace Nashi.Linux.Engine;

/// `nashi --self-test [folder]` exercises the Linux engines end to end without a display:
/// real exports from generated fixtures, checked by reading the results back. Steps that need
/// an optional system tool (LibreOffice, Tesseract, FFmpeg) are reported as skipped when it is missing.
public static class SelfTest
{
    private sealed record Step(string Name,string Status,string Detail);
    public static async Task<int> RunAsync(string? folder)
    {
        var root=folder??Path.Combine(Path.GetTempPath(),"nashi-self-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var fixtures=Path.Combine(root,"fixtures");Directory.CreateDirectory(fixtures);var output=Path.Combine(root,"output");Directory.CreateDirectory(output);
        var steps=new List<Step>();
        async Task Check(string name,Func<Task<string>> body,Func<string?>? skip=null)
        {
            if(skip?.Invoke() is {} reason){steps.Add(new(name,"skip",reason));Console.WriteLine($"  skip  {name} · {reason}");return;}
            var watch=Stopwatch.StartNew();
            try{var detail=await body();steps.Add(new(name,"pass",detail));Console.WriteLine($"  pass  {name} · {detail} · {watch.ElapsedMilliseconds} ms");}
            catch(Exception error){steps.Add(new(name,"fail",error.GetType().Name+": "+error.Message));Console.WriteLine($"  FAIL  {name} · {error.GetType().Name}: {error.Message}");if(Environment.GetEnvironmentVariable("NASHI_SELF_TEST_TRACE")=="1")Console.WriteLine(error);}
        }
        Console.WriteLine("Nashi for Linux self-test · "+root);
        Magick.Ensure();
        var photo=Path.Combine(fixtures,"Photo.png");var second=Path.Combine(fixtures,"Second.jpg");var qr=Path.Combine(fixtures,"Code.png");
        using(var image=new MagickImage("gradient:#E3B859-#243B2B",new MagickReadSettings{Width=640,Height=400}))image.Write(photo,MagickFormat.Png);
        using(var image=new MagickImage("gradient:#F6E7BE-#596650",new MagickReadSettings{Width=400,Height=640}))image.Write(second,MagickFormat.Jpeg);
        WriteQr(qr,"https://github.com/orkunisitmak/nashi-app");
        IReadOnlyList<string> One(IReadOnlyList<string> files)=>files.Count==1&&(Directory.Exists(files[0])||File.Exists(files[0])&&new FileInfo(files[0]).Length>0)?files:throw new InvalidDataException("Expected one non-empty output, got "+files.Count+".");
        string Describe(string file){using var image=new MagickImage(file);return $"{Path.GetFileName(file)} {image.Width}×{image.Height} {image.Format}";}

        foreach(var format in new[]{"jpg","webp","avif","tiff","bmp","gif","pdf"})
            await Check("Convert PNG → "+format.ToUpperInvariant(),()=>Task.Run(()=>{var file=One(ImageEngine.Apply("convert",[photo],output,new(),format))[0];return format=="pdf"?Path.GetFileName(file)+" "+new FileInfo(file).Length+" B":Describe(file);}));
        await Check("Resize",()=>Task.Run(()=>Describe(One(ImageEngine.Apply("resize",[photo],output,new(Width:320)))[0])));
        await Check("Rotate 30°",()=>Task.Run(()=>Describe(One(ImageEngine.Apply("rotate",[photo],output,new(Rotation:30)))[0])));
        await Check("Crop",()=>Task.Run(()=>Describe(One(ImageEngine.Apply("crop",[photo],output,new(Crop:new PixelRect(10,20,200,100))))[0])));
        await Check("Edit photo",()=>Task.Run(()=>Describe(One(ImageEngine.Apply("editImage",[photo],output,new(){Adjustments=new(){Exposure=.3,Saturation=1.4,Vignette=.5},FlipHorizontal=true}))[0])));
        await Check("Add background",()=>Task.Run(()=>Describe(One(ImageEngine.Apply("frameImage",[photo],output,new(){Frame=new(Backdrop:"gradient")}))[0])));
        await Check("Redact (solid, pixelate, blur)",()=>Task.Run(()=>string.Join(", ",new[]{"solid","pixelate","blur"}.Select(style=>Describe(One(ImageEngine.Apply("redactImage",[photo],output,new(){RedactionStyle=style,Redactions=[new(40,40,160,90)]}))[0])))));
        await Check("Compress to 40%",()=>Task.Run(()=>{var file=One(ImageEngine.Apply("compress",[second],output,new(){ReductionPercent=60}))[0];return $"{new FileInfo(second).Length} → {new FileInfo(file).Length} B";}));
        await Check("Create collage",()=>Task.Run(()=>Describe(One(ImageEngine.Apply("createCollage",[photo,second],output,new(){Collage=new(Width:1200,Height:800,CornerRadius:18)}))[0])));
        string? imagesPdf=null;
        await Check("Create PDF from images",()=>Task.Run(()=>{imagesPdf=One(ImageEngine.Apply("createPDF",[photo,second],output,new()))[0];return Path.GetFileName(imagesPdf);}));
        await Check("Render PDF pages (PDFium)",async()=>{var pages=await LinuxPdf.RenderAsync(imagesPdf!,72);if(pages.Length!=2)throw new InvalidDataException("Expected 2 pages.");var size=Magick.Size(pages[1]);return $"{pages.Length} pages, page 2 {size.Width}×{size.Height}";},()=>imagesPdf is null?"no PDF":null);
        await Check("Merge PDFs",()=>Task.Run(()=>Path.GetFileName(One(DocumentEngine.Pdf("mergePDFs",[imagesPdf!,imagesPdf!],output))[0])),()=>imagesPdf is null?"no PDF":null);
        await Check("Split PDF",()=>Task.Run(()=>DocumentEngine.Pdf("splitPDF",[imagesPdf!],output).Count+" files"),()=>imagesPdf is null?"no PDF":null);
        await Check("Read QR code",()=>Task.Run(()=>{using var image=ImageEngine.Load(qr);var values=ImageEngine.DecodeQr(image);return values.Length==1&&values[0].Contains("nashi-app")?values[0]:throw new InvalidDataException("QR not decoded.");}));
        await Check("Read QR code from PDF",async()=>{var pdf=One(ImageEngine.Apply("convert",[qr],output,new(),"pdf"))[0];var page=(await LinuxPdf.RenderAsync(pdf,144))[0];using var image=Magick.FromPng(page);var values=ImageEngine.DecodeQr(image);return values.Length==1?values[0]:throw new InvalidDataException("QR not decoded.");});

        var zip=Path.Combine(fixtures,"Bundle.zip");
        await Check("Zip and extract archive",()=>Task.Run(()=>{var made=DocumentEngine.Zip([photo,second],fixtures);File.Move(made,zip);var extracted=One(ArchiveEngine.Extract(zip,output))[0];return Path.GetFileName(extracted)+" · "+Directory.GetFiles(extracted,"*",SearchOption.AllDirectories).Length+" files";}));

        var office=new LinuxOffice().Executable;var text=Path.Combine(fixtures,"Notes.txt");await File.WriteAllTextAsync(text,"Nashi Linux\nÇalışıyor: ğüşiöç\n");
        string? officePdf=null;
        await Check("TXT → DOCX (LibreOffice)",async()=>Path.GetFileName(One(await new DocumentConversion(new LinuxOffice(),Tools()).ConvertAsync([text],"docx",output,new(),null,CancellationToken.None))[0]),()=>office is null?"LibreOffice not installed":null);
        await Check("TXT → PDF (LibreOffice)",async()=>{officePdf=One(await new DocumentConversion(new LinuxOffice(),Tools()).ConvertAsync([text],"pdf",output,new(),null,CancellationToken.None))[0];return Path.GetFileName(officePdf);},()=>office is null?"LibreOffice not installed":null);
        await Check("PDF → TXT",async()=>{var file=One(await new DocumentConversion(new LinuxOffice(),Tools()).ConvertAsync([officePdf!],"txt",output,new(),null,CancellationToken.None))[0];var content=await File.ReadAllTextAsync(file);return content.Contains("Nashi")?"text kept":throw new InvalidDataException("Text missing: "+content);},()=>officePdf is null?"no office PDF":null);
        await Check("PDF → PNG pages",async()=>One(await new DocumentConversion(new LinuxOffice(),Tools()).ConvertAsync([officePdf!],"png",output,new(),null,CancellationToken.None)).Count+" page",()=>officePdf is null?"no office PDF":null);
        await Check("Compress PDF",async()=>Path.GetFileName(One(await PdfCompression.RunAsync([imagesPdf!],output,new(){ReductionPercent=20},"",null,CancellationToken.None))[0]),()=>imagesPdf is null?"no PDF":null);
        await Check("OCR a scanned page (Tesseract)",async()=>{var scan=Path.Combine(fixtures,"Scan.png");using(var image=new MagickImage(MagickColors.White,900,200)){new ImageMagick.Drawing.Drawables().FontPointSize(64).FillColor(MagickColors.Black).Text(40,120,"Nashi Linux").Draw(image);image.Write(scan,MagickFormat.Png);}var recognized=await PdfDocumentMedia.Recognize(await File.ReadAllBytesAsync(scan),CancellationToken.None);return recognized.Contains("Nashi",StringComparison.OrdinalIgnoreCase)?recognized.Trim():throw new InvalidDataException("Recognized: "+recognized);},()=>PdfDocumentMedia.OcrAvailable?null:"Tesseract not installed");

        var tools=Tools();string? ffmpeg=null,ffprobe=null;try{ffmpeg=tools.Executable("ffmpeg");ffprobe=tools.Executable("ffprobe");}catch(FileNotFoundException){}
        var tone=Path.Combine(fixtures,"Tone.wav");var clip=Path.Combine(fixtures,"Clip.mp4");
        if(ffmpeg is not null)
        {
            try
            {
                await ProcessRunner.RunAsync(ffmpeg,["-v","error","-f","lavfi","-i","sine=frequency=440:duration=3","-y",tone]);
                await ProcessRunner.RunAsync(ffmpeg,["-v","error","-f","lavfi","-i","testsrc=size=320x240:rate=25:duration=3","-f","lavfi","-i","sine=frequency=660:duration=3","-shortest","-pix_fmt","yuv420p","-y",clip]);
            }
            catch(IOException error){Console.WriteLine("  note  FFmpeg fixtures could not be made: "+error.Message);}
        }
        string? NoMedia()=>ffmpeg is null||ffprobe is null?"FFmpeg not found (Set up media tools)":!File.Exists(clip)?"no media fixtures":null;
        MediaEngine Media()=>new(ffmpeg!,ffprobe!);
        await Check("Inspect video",async()=>{var info=await Media().InspectAsync(clip);return $"{info.Duration:0.0}s {info.Video?.Width}×{info.Video?.Height} audio={info.HasAudio}";},NoMedia);
        await Check("WAV → MP3",async()=>Path.GetFileName(One(await Media().ConvertAsync([tone],"mp3",output,new()))[0]),NoMedia);
        await Check("MP4 → WEBM",async()=>Path.GetFileName(One(await Media().ConvertAsync([clip],"webm",output,new()))[0]),NoMedia);
        await Check("Trim video",async()=>{var file=One(await Media().ToolAsync("trimVideo",[clip],output,new(){Start=.5,End=2}))[0];var info=await Media().InspectAsync(file);return $"{info.Duration:0.0}s";},NoMedia);
        await Check("Remove audio",async()=>{var file=One(await Media().ToolAsync("muteVideo",[clip],output,new()))[0];return (await Media().InspectAsync(file)).HasAudio?throw new InvalidDataException("Audio kept."):"no audio";},NoMedia);
        await Check("Video snapshots",async()=>(await Media().ToolAsync("videoSnapshots",[clip],output,new(){SnapshotTimes=[0,1.5]})).Count+" images",NoMedia);

        var failed=steps.Count(s=>s.Status=="fail");
        Console.WriteLine($"{steps.Count(s=>s.Status=="pass")} passed, {failed} failed, {steps.Count(s=>s.Status=="skip")} skipped");
        await File.WriteAllTextAsync(Path.Combine(root,"self-test.json"),System.Text.Json.JsonSerializer.Serialize(steps,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        return failed==0?0:1;
    }
    public static Toolchain Tools()=>new(AppPaths.Tools,Environment.GetEnvironmentVariable("PATH"));
    private static void WriteQr(string path,string text)
    {
        var writer=new ZXing.BarcodeWriterPixelData{Format=ZXing.BarcodeFormat.QR_CODE,Options=new ZXing.Common.EncodingOptions{Width=360,Height=360,Margin=2}};
        var data=writer.Write(text);using var image=new MagickImage();image.ReadPixels(data.Pixels,new PixelReadSettings((uint)data.Width,(uint)data.Height,StorageType.Char,PixelMapping.BGRA));image.Write(path,MagickFormat.Png);
    }
}
