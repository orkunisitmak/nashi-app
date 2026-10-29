using System.IO.Compression;
using System.Security.Cryptography;
using ImageMagick;
using Nashi.Core;
using Nashi.Linux;
using PdfSharp.Pdf.IO;

if(!OperatingSystem.IsLinux())throw new PlatformNotSupportedException("Run Linux checks on Linux.");
var root=Path.Combine(Path.GetTempPath(),"nashi-linux-verify-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);var assertions=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);assertions++;Console.WriteLine("PASS "+label);}
async Task Reject(Func<Task> action,string label)
{try{await action();}catch(Exception e)when(e is ArgumentException or InvalidDataException or IOException or OperationCanceledException){Check(true,label);return;}throw new Exception("Did not reject: "+label);}
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
try
{
    var output=Path.Combine(root,"output");Directory.CreateDirectory(output);
    var image=Path.Combine(root,"a space ; $(touch sentinel).png");
    using(var pixels=new MagickImage(MagickColors.Gold,80,40))pixels.Write(image);
    var original=Hash(image);
    var resized=await Operations.RunAsync("resize",[image],output,new(){Width=20,Format="png"});
    using(var check=PhotoCodec.Read(resized[0]))Check(check.Width==20&&check.Height==10,"real resize preserves aspect ratio");
    var resizedAgain=await Operations.RunAsync("resize",[image],output,new(){Width=20});
    Check(resized[0]!=resizedAgain[0]&&File.Exists(resized[0]),"exports choose unique filenames");
    var cropped=await Operations.RunAsync("crop",[image],output,new(){X=5,Y=5,Width=12,Height=8});
    using(var check=PhotoCodec.Read(cropped[0]))Check(check.Width==12&&check.Height==8,"crop applies its rectangle");
    var rotated=await Operations.RunAsync("rotate",[image],output,new(){Rotation=90});
    using(var check=PhotoCodec.Read(rotated[0]))Check(check.Width==40&&check.Height==80,"rotation changes real pixels");
    var adjusted=await Operations.RunAsync("editImage",[image],output,new(){Adjustments=new(){Brightness=-.5}});
    using(var a=PhotoCodec.Read(image))using(var b=PhotoCodec.Read(adjusted[0]))Check(!a.ToByteArray(MagickFormat.Rgb).SequenceEqual(b.ToByteArray(MagickFormat.Rgb)),"photo adjustment changes pixel values");
    foreach(var format in new[]{"jpg","webp","avif","tiff","bmp","gif"})
    {
        // RasterEngine supports GIF output even when a catalog route intentionally omits it.
        var converted=RasterEngine.Export("convert",[image],format,output,new());
        using var decoded=PhotoCodec.Read(converted[0]);Check(decoded.Width==80&&decoded.Height==40,"independent decode "+format);
    }
    var pdfs=await Operations.RunAsync("createPDF",[image,image],output,new());
    using(var pdf=PdfReader.Open(pdfs[0],PdfDocumentOpenMode.Import))Check(pdf.PageCount==2,"image PDF has both pages");
    var reordered=await Operations.RunAsync("organizePDF",pdfs,output,new(){Pages="2,1,2"});
    using(var pdf=PdfReader.Open(reordered[0],PdfDocumentOpenMode.Import))Check(pdf.PageCount==3,"PDF reorder preserves duplicates");
    var split=await Operations.RunAsync("splitPDF",reordered,output,new(){Pages="3,1-2",PagesPerFile=2});
    Check(split.Count==2,"PDF split respects grouping");
    var merged=await Operations.RunAsync("mergePDFs",split,output,new());
    using(var pdf=PdfReader.Open(merged[0],PdfDocumentOpenMode.Import))Check(pdf.PageCount==3,"PDF merge round trip");
    await Reject(()=>Operations.RunAsync("crop",[image],output,new(){X=79,Y=0,Width=2,Height=2}),"out-of-bounds crop rejected");
    var broken=Path.Combine(root,"broken.png");File.WriteAllText(broken,"not an image");
    var before=Directory.GetFileSystemEntries(output).Order().ToArray();
    try{await Operations.RunAsync("resize",[image,broken],output,new(){Width=10});throw new Exception("Expected bad image failure");}
    catch(MagickException){Check(before.SequenceEqual(Directory.GetFileSystemEntries(output).Order()),"failed image batch publishes nothing");}
    using(var cancellation=new CancellationTokenSource())
    {
        cancellation.Cancel();await Reject(()=>Operations.RunAsync("resize",[image],output,new(){Width=10},token:cancellation.Token),"pre-cancelled export rejected");
    }
    Check(!Directory.GetDirectories(output,".nashi-*").Any(),"failed/cancelled stages cleaned");
    var folder=Path.Combine(root,"nested");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"Türkçe.txt"),"Nashi ✓");
    var zip=await Operations.RunAsync("zip",[folder,image],output,new());
    var extracted=await Operations.RunAsync("extract",zip,output,new());
    Check(File.ReadAllText(Path.Combine(extracted[0],"nested","Türkçe.txt"))=="Nashi ✓","archive Unicode/nested round trip");
    await Reject(()=>Operations.RunAsync("zip",[folder],folder,new()),"ZIP cannot recursively include its own staging folder");
    var traversal=Path.Combine(root,"unsafe.zip");using(var archive=ZipFile.Open(traversal,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry("../escape.txt").Open());writer.Write("unsafe");}
    await Reject(()=>Operations.RunAsync("extract",[traversal],output,new()),"archive traversal rejected");
    var linked=Path.Combine(folder,"link");File.CreateSymbolicLink(linked,image);
    await Reject(()=>Operations.RunAsync("zip",[folder],output,new()),"symlink input rejected by ZIP");File.Delete(linked);
    var subtitle=Path.Combine(root,"captions.srt");File.WriteAllText(subtitle,"1\n00:00:00,250 --> 00:00:01,500\nMerhaba ✓\n");
    var vtt=await Operations.RunAsync("convert",[subtitle],output,new(){Format="vtt"});
    Check(File.ReadAllText(vtt[0]).Contains("00:00:00.250 --> 00:00:01.500"),"subtitle timing round trip");
    var upper=Path.Combine(root,"A.png");File.Copy(image,upper);
    var library=new LibraryStore(Path.Combine(root,"history"));library.Record("test",[image,upper]);
    Check(library.Entries[0].Paths.Length==2,"Linux library retains case-distinct filenames");
    Check(new LibraryStore(library.DirectoryPath).Entries.Count==1,"library survives restart");
    var prefs=new PreferencesStore(Path.Combine(root,"settings"));prefs.Save(prefs.Value with{OutputFolder=output,OutputMode="folder"});
    Check(new PreferencesStore(Path.Combine(root,"settings")).Value.OutputFolder==output,"output preference survives restart");
    var oldXdg=Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    try{Environment.SetEnvironmentVariable("XDG_DATA_HOME",Path.Combine(root,"xdg"));Check(Operations.DataDirectory==Path.Combine(root,"xdg","nashi"),"XDG data path");Environment.SetEnvironmentVariable("XDG_DATA_HOME","relative");Check(Path.IsPathFullyQualified(Operations.DataDirectory),"relative XDG path ignored");}
    finally{Environment.SetEnvironmentVariable("XDG_DATA_HOME",oldXdg);}
    var oldPath=Environment.GetEnvironmentVariable("PATH");
    try
    {
        var bin=Path.Combine(root,"bin");Directory.CreateDirectory(bin);var tool=Path.Combine(bin,"ffmpeg");File.WriteAllText(tool,"fixture");
        Environment.SetEnvironmentVariable("PATH",".:relative:"+bin);
        if(OperatingSystem.IsLinux())File.SetUnixFileMode(tool,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        Check(SystemTools.Find("ffmpeg") is null,"non-executable tools and relative PATH skipped");
        if(OperatingSystem.IsLinux())File.SetUnixFileMode(tool,UnixFileMode.UserRead|UnixFileMode.UserExecute);
        Check(new Toolchain(root).Executable("ffmpeg")==tool,"Linux resolves executables without .exe");
    }
    finally{Environment.SetEnvironmentVariable("PATH",oldPath);}
    await Reject(()=>Task.Run(()=>WebDownload.Validate("http://localhost/private")),"web loopback rejected");
    var ffmpeg=SystemTools.Find("ffmpeg")??throw new Exception("Install FFmpeg for verification");
    var ffprobe=SystemTools.Find("ffprobe")??throw new Exception("Install ffprobe for verification");
    var audio=Path.Combine(root,"sound ; $(touch sentinel).wav");
    await ProcessRunner.RunAsync(ffmpeg,["-f","lavfi","-i","sine=frequency=440:duration=2","-y",audio]);
    var mp3=await Operations.RunAsync("convert",[audio],output,new(){Format="mp3"});
    var engine=new MediaEngine(ffmpeg,ffprobe);var media=await engine.InspectAsync(mp3[0]);
    Check(media.HasAudio&&media.Duration>1.9,"real FFmpeg audio conversion");
    var trimmed=await Operations.RunAsync("trimAudio",[audio],output,new(){Start=.25,End=1.25});
    var trimmedInfo=await engine.InspectAsync(trimmed[0]);Check(Math.Abs(trimmedInfo.Duration-1)<.1,"media controls affect exported duration");
    await Reject(()=>Operations.RunAsync("trimAudio",[audio],output,new(){Start=double.NaN}),"non-finite media range rejected");
    var video=Path.Combine(root,"test video.mkv");
    await ProcessRunner.RunAsync(ffmpeg,["-f","lavfi","-i","color=c=gold:s=64x32:r=10:d=2","-f","lavfi","-i","sine=frequency=440:duration=2","-c:v","mpeg4","-c:a","pcm_s16le","-shortest",video]);
    var mp4=await Operations.RunAsync("convert",[video],output,new(){Format="mp4"});
    var movie=await engine.InspectAsync(mp4[0]);Check(movie.Video?.Width==64&&movie.HasAudio,"real MP4 video/audio conversion");
    var mute=await Operations.RunAsync("muteVideo",mp4,output,new());
    Check(!(await engine.InspectAsync(mute[0])).HasAudio,"mute removes the actual audio stream");
    var clip=await Operations.RunAsync("trimVideo",mp4,output,new(){Start=.5,End=1.5});
    Check(Math.Abs((await engine.InspectAsync(clip[0])).Duration-1)<.15,"video range controls change duration");
    var fast=await Operations.RunAsync("changeVideoSpeed",mp4,output,new(){Speed=2});
    Check((await engine.InspectAsync(fast[0])).Duration<1.3,"speed affects exported video duration");
    var frame=await Operations.RunAsync("videoSnapshots",mp4,output,new(){Start=.5});
    using(var decoded=PhotoCodec.Read(frame[0]))Check(decoded.Width==64&&decoded.Height==32,"video snapshot decodes to the source dimensions");
    var segments=await Operations.RunAsync("splitVideo",mp4,output,new(){Speed=1});
    Check(segments.Count==2,"video splits at the selected interval");
    var joined=await Operations.RunAsync("joinVideos",[mp4[0],mp4[0]],output,new());
    Check((await engine.InspectAsync(joined[0])).Duration>3.8,"join preserves both video inputs");
    if(SystemTools.Find("libreoffice") is not null)
    {
        var doc=Path.Combine(root,"office.txt");File.WriteAllText(doc,"Nashi Office fixture");
        var officePdf=await Operations.RunAsync("convert",[doc],output,new(){Format="pdf"});
        Check(OfficeDocuments.PdfText(officePdf[0]).Contains("Nashi Office fixture"),"real LibreOffice PDF preserves fixture text");
    }
    else Console.WriteLine("SKIP local Office integration: LibreOffice is not installed (required in CI)");
    using(var cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(150)))
        await Reject(()=>ProcessRunner.RunAsync("/bin/sleep",["30"],cancel.Token),"cancellation terminates running child process");
    Check(Hash(image)==original,"original image unchanged after all operations");
    Check(!File.Exists(Path.Combine(root,"sentinel")),"special filenames never run as shell code");
    Check(!Directory.GetDirectories(output,".nashi-*").Any(),"all transactions cleaned up");
    Console.WriteLine($"PASS {assertions} Linux checks");return 0;
}
catch(Exception e){Console.Error.WriteLine(e);return 1;}
finally{Directory.Delete(root,true);}
