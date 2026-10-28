using Nashi.Core;
using Nashi.Windows;
using ImageMagick;
using System.Security.Cryptography;
using PdfSharp.Pdf.IO;
using ZXing;
using ZXing.Common;

var root=Path.Combine(Path.GetTempPath(),"NashiLinuxVerify-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var passed=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
async Task MustFail(Func<Task> action,string label){try{await action();}catch(Exception e)when(e is ArgumentException or InvalidDataException or OperationCanceledException or IOException){Check(true,label);return;}throw new Exception("Expected failure: "+label);}
try
{
    var engine=new LinuxEngine();var output=Path.Combine(root,"out");Directory.CreateDirectory(output);
    var image=Path.Combine(root,"pear.png");using(var source=new MagickImage(MagickColors.Gold,160,120))source.Write(image);
    var leaf=Path.Combine(root,"leaf.png");using(var source=new MagickImage(MagickColors.Green,80,160))source.Write(leaf);
    var unusual=Path.Combine(root,"file:Linux.png");File.Copy(image,unusual);var unusualExport=await engine.RunAsync("convert",[unusual],output,new(),"jpg");Check(File.Exists(unusualExport.Single()),"Linux colon filename");
    var tree=Path.Combine(root,"folder");Directory.CreateDirectory(tree);await File.WriteAllTextAsync(Path.Combine(tree,"file:Linux.txt"),"kept");
    var copied=await FileTransfer.RunAsync([tree],Path.Combine(root,"copied"),false,null,default);Check(File.Exists(Path.Combine(copied.Single(),"file:Linux.txt")),"folder copy with Linux names");
    await MustFail(()=>FileTransfer.RunAsync([tree],Path.Combine(tree,"inside"),false,null,default),"copy destination outside source");
    var moved=await FileTransfer.RunAsync(copied,Path.Combine(root,"moved"),true,null,default);Check(Directory.Exists(moved.Single())&&!Directory.Exists(copied.Single()),"copy before delete folder move");
    var gradient=await engine.RunAsync("frameImage",[image],output,new(){Frame=new(Backdrop:"gradient",Color:"#ffffff",GradientEnd:"#000000",Width:300,Height:240,Padding:20)});using(var decoded=PhotoCodec.Read(gradient.Single()))Check(decoded.Width==300&&decoded.Height==240,"gradient frame canvas");
    var featured=await engine.RunAsync("createCollage",[image,leaf],output,new(){Collage=new(Layout:"featured",Width:500,Height:300,Padding:10,Order:[1,0])});using(var decoded=PhotoCodec.Read(featured.Single())){using var pixels=decoded.GetPixels();Check(pixels.GetPixel(50,50).ToColor()!.G>pixels.GetPixel(50,50).ToColor()!.R,"featured collage order");}
    var sized=await engine.RunAsync("editImage",[image],output,new(){Width=90,Height=90});using(var decoded=PhotoCodec.Read(sized.Single()))Check(decoded.Width==90&&decoded.Height==90,"explicit image dimensions");
    var hash=SHA256.HashData(File.ReadAllBytes(image));
    foreach(var format in new[]{"jpg","webp","avif","bmp","tiff"})
    {
        var result=await engine.RunAsync("convert",[image],output,new(),format);using var decoded=PhotoCodec.Read(result.Single());Check(decoded.Width==160&&decoded.Height==120,"image "+format+" dimensions");
    }
    var crop=await engine.RunAsync("cropImage",[image],output,new(){X=20,Y=10,Width=60,Height=40});using(var decoded=PhotoCodec.Read(crop.Single()))Check(decoded.Width==60&&decoded.Height==40,"crop dimensions");
    await MustFail(()=>engine.RunAsync("cropImage",[image],output,new(){X=-1,Width=60,Height=40}),"reject invalid crop");
    var edit=await engine.RunAsync("editImage",[image],output,new(){Rotation=90,Adjustments=new(){Saturation=.5}});using(var decoded=PhotoCodec.Read(edit.Single()))Check(decoded.Width==120&&decoded.Height==160,"photo edits and rotation");
    var framed=await engine.RunAsync("frameImage",[image],output,new(){Frame=new(Padding:20)});using(var decoded=PhotoCodec.Read(framed.Single()))Check(decoded.Width==200&&decoded.Height==160,"frame dimensions");
    var collage=await engine.RunAsync("createCollage",[image,image],output,new(){Collage=new(Width:500,Height:300,Padding:10)});using(var decoded=PhotoCodec.Read(collage.Single()))Check(decoded.Width==500&&decoded.Height==300,"collage dimensions");
    var redact=await engine.RunAsync("redactImage",[image],output,new(){X=0,Y=0,Width=40,Height=40});using(var decoded=PhotoCodec.Read(redact.Single())){using var pixels=decoded.GetPixels();Check(pixels.GetPixel(10,10).ToColor()!.R==0,"redaction covers pixels");}
    var annotation=await engine.RunAsync("annotateImage",[image],output,new(){Text="Nashi",X=10,Y=10});Check(!SHA256.HashData(File.ReadAllBytes(annotation.Single())).SequenceEqual(hash),"annotation changes output");
    var compressed=await engine.RunAsync("compress",[image],output,new(){Quality=40});Check(File.Exists(compressed.Single()),"image compression");
    var pdf=await engine.RunAsync("createPDF",[image,image],output,new());using(var decoded=PdfReader.Open(pdf.Single(),PdfDocumentOpenMode.Import))Check(decoded.PageCount==2,"image PDF page count");
    var split=await engine.RunAsync("splitPDF",pdf,output,new());Check(split.Count==2,"PDF split");
    var merge=await engine.RunAsync("mergePDF",split,output,new());using(var decoded=PdfReader.Open(merge.Single(),PdfDocumentOpenMode.Import))Check(decoded.PageCount==2,"PDF merge");
    var organize=await engine.RunAsync("organizePDF",pdf,output,new(){PageRanges="2,1,2"});using(var decoded=PdfReader.Open(organize.Single(),PdfDocumentOpenMode.Import))Check(decoded.PageCount==3,"PDF reorder and duplicate");
    var pdfCompressed=await engine.RunAsync("compress",pdf,output,new(){Quality=50});Check(File.Exists(pdfCompressed.Single()),"PDF compression");
    if(SystemTools.Find("pdftoppm") is not null)
    {
        var rendered=await engine.RunAsync("convert",pdf,output,new(),"png");Check(rendered.Count==2,"PDF page rendering");
    }
    var subtitle=Path.Combine(root,"captions.srt");await File.WriteAllTextAsync(subtitle,"1\n00:00:01,000 --> 00:00:02,000\nSalam, Nashi!\n");
    var vtt=await engine.RunAsync("convert",[subtitle],output,new(),"vtt");Check((await File.ReadAllTextAsync(vtt.Single())).Contains("WEBVTT"),"subtitle conversion");
    var archive=await engine.RunAsync("zip",[image,subtitle],output,new());var extracted=await engine.RunAsync("extractArchive",archive,output,new());Check(Directory.GetFiles(extracted.Single()).Length==2,"archive round trip");
    var tar=await engine.RunAsync("convert",archive,output,new(),"tar");Check(new FileInfo(tar.Single()).Length>0,"archive conversion");
    var library=new LibraryStore(Path.Combine(root,"state"));var upper=Path.Combine(root,"A.txt");var lower=Path.Combine(root,"a.txt");await File.WriteAllTextAsync(upper,"A");await File.WriteAllTextAsync(lower,"a");library.Record("case",[upper,lower]);Check(new LibraryStore(library.DirectoryPath).Entries.Single().Paths.Length==2,"Linux case-sensitive library");
    var writer=new BarcodeWriterPixelData{Format=BarcodeFormat.QR_CODE,Options=new EncodingOptions{Width=240,Height=240}};var qr=writer.Write("https://github.com/orkunisitmak/nashi-app");
    var qrPath=Path.Combine(root,"qr.png");using(var bitmap=new MagickImage(qr.Pixels,new PixelReadSettings((uint)qr.Width,(uint)qr.Height,StorageType.Char,PixelMapping.BGRA)))bitmap.Write(qrPath);
    var read=await engine.RunAsync("readQRCode",[qrPath],output,new());Check((await File.ReadAllTextAsync(read.Single())).Contains("orkunisitmak"),"QR decoding");
    using(var cancel=new CancellationTokenSource()){cancel.Cancel();await MustFail(()=>engine.RunAsync("convert",[image],output,new(),"jpg",token:cancel.Token),"cancellation");}
    Check(!Directory.GetDirectories(output,".nashi-*").Any(),"transactions cleaned up");Check(SHA256.HashData(File.ReadAllBytes(image)).SequenceEqual(hash),"original unchanged");
    if(SystemTools.Find("ffmpeg") is {} ffmpeg&&SystemTools.Find("ffprobe") is {} ffprobe)
    {
        var audio=Path.Combine(root,"tone.wav");await ProcessRunner.RunAsync(ffmpeg,["-v","error","-f","lavfi","-i","sine=frequency=440:duration=2",audio]);
        var converted=await engine.RunAsync("convert",[audio],output,new(),"flac");var info=await new MediaEngine(ffmpeg,ffprobe).InspectAsync(converted.Single());Check(info.HasAudio&&Math.Abs(info.Duration-2)<.1,"shared FFmpeg conversion");
        var trimmed=await engine.RunAsync("trimAudio",[audio],output,new(){Media=new(){Start=.25,End=1.25}});info=await new MediaEngine(ffmpeg,ffprobe).InspectAsync(trimmed.Single());Check(Math.Abs(info.Duration-1)<.1,"shared audio trim");
    }
    if(SystemTools.Find("exiftool") is not null)
    {
        var metadata=new MetadataService(Path.Combine(root,"metadata-tools"));
        var tagged=await engine.RunAsync("removeMetadata",[image],output,new(){RemoveAllMetadata=false,Metadata=new(){{"EXIF:Artist","Nashi Linux"}}});
        var tags=await metadata.ReadAsync(tagged.Single());Check(tags.TryGetValue("EXIF:Artist",out var artist)&&artist=="Nashi Linux","system ExifTool writes image tags");
        var cleaned=await engine.RunAsync("removeMetadata",tagged,output,new());tags=await metadata.ReadAsync(cleaned.Single());Check(!tags.ContainsKey("EXIF:Artist"),"system ExifTool removes image tags");
        var preserved=await engine.RunAsync("convert",tagged,output,new(){PreserveImageMetadata=true},"jpg");tags=await metadata.ReadAsync(preserved.Single());Check(tags.TryGetValue("EXIF:Artist",out artist)&&artist=="Nashi Linux","image conversion preserves requested tags");
    }
    else Console.WriteLine("SKIP ExifTool integration: executable unavailable");
    if(SystemTools.Find("libreoffice") is not null||SystemTools.Find("soffice") is not null)
    {
        var document=Path.Combine(root,"Linux document.txt");const string text="Nashi Linux document integration fixture.";await File.WriteAllTextAsync(document,text);
        var docx=await engine.RunAsync("convert",[document],output,new(),"docx");
        using(var zip=System.IO.Compression.ZipFile.OpenRead(docx.Single())){var entry=zip.GetEntry("word/document.xml");Check(entry is not null,"LibreOffice produces DOCX package");using var reader=new StreamReader(entry!.Open());Check((await reader.ReadToEndAsync()).Contains(text),"DOCX retains source text");}
        var documentPdf=await engine.RunAsync("convert",docx,output,new(),"pdf");using(var decoded=UglyToad.PdfPig.PdfDocument.Open(documentPdf.Single()))Check(decoded.NumberOfPages==1&&decoded.GetPage(1).Text.Contains("Nashi"),"LibreOffice DOCX to PDF retains text");
        var recovered=await engine.RunAsync("convert",documentPdf,output,new(),"txt");Check((await File.ReadAllTextAsync(recovered.Single())).Contains("Nashi"),"PDF text conversion round trip");
    }
    else Console.WriteLine("SKIP LibreOffice integration: executable unavailable");
    Console.WriteLine($"All {passed} Linux checks passed.");
}
finally{Directory.Delete(root,true);}
