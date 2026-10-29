using ImageMagick;
using Nashi.Core;
using UglyToad.PdfPig;
using ZXing;
using ZXing.Common;
namespace Nashi.Linux;

/// Real exports on synthetic fixtures: `Nashi --verify-operations <empty folder>`.
internal static class OperationChecks
{
    private static int passed;
    public static int Run(string root)
    {
        try
        {
            root=Path.GetFullPath(root);Directory.CreateDirectory(root);
            if(Directory.EnumerateFileSystemEntries(root).Any())throw new InvalidOperationException("Choose an empty folder.");
            var progress=new Progress<OperationProgress>();var token=CancellationToken.None;
            var png=Path.Combine(root,"Pear.png");
            using(var image=new MagickImage(new MagickColor("#E3B859"),320,200)){image.Alpha(AlphaAction.Set);image.Write(png,MagickFormat.Png);}
            var originalHash=Hash(png);

            Check(Operations.Formats([png]).SequenceEqual(new[]{"jpg","webp","heic","tiff","avif","bmp","pdf"}),"PNG conversion formats");
            Check(Operations.Tools([png]).Select(t=>t.Id).SequenceEqual(new[]{"compress","removeMetadata","editImage","cropImage","readQRCode"}),"PNG tools");
            foreach(var format in Operations.Formats([png]))
            {
                var output=Operations.RunAsync(new("convert",format,format),[png],Path.Combine(root,"convert-"+format),progress,token).GetAwaiter().GetResult().Single();
                Check(new FileInfo(output).Length>0&&FeatureCatalog.Extension(output)==format,"Export "+format);
                if(format!="pdf"){using var result=new MagickImage(output);Check(result.Width==320&&result.Height==200,"Dimensions "+format);}
            }
            var jpg=Directory.GetFiles(Path.Combine(root,"convert-jpg")).Single();
            var again=Operations.RunAsync(new("convert","jpg","jpg"),[jpg],Path.Combine(root,"convert-jpg"),progress,token).GetAwaiter().GetResult().Single();
            Check(again!=jpg&&File.Exists(jpg)&&Path.GetFileName(again)=="Pear 2.jpg","Existing output is never overwritten");

            Check(Operations.Tools([png]).Any(t=>t.Id=="cropImage")&&Operations.Tools([png,jpg]).Any(t=>t.Id=="cropImage"),"Crop offered");
            var cropped=Operations.RunAsync(new("cropImage","Crop Image"),[png],Path.Combine(root,"crop"),progress,token,null,new(.25,.5,.5,.5)).GetAwaiter().GetResult().Single();
            using(var result=new MagickImage(cropped))Check(result.Width==160&&result.Height==100&&result.Page.X==0,"Crop size and page");
            var shared=Operations.RunAsync(new("cropImage","Crop Image"),[png,jpg],Path.Combine(root,"crop-many"),progress,token,null,new(0,0,1,.5)).GetAwaiter().GetResult();
            Check(shared.Count==2,"Crop several images");
            try{Operations.Crop(new MagickImage(MagickColors.White,10,10),new(.8,0,.5,1));Check(false,"Crop outside the image is rejected");}catch(ArgumentException){Check(true,"Crop outside the image is rejected");}
            var edited=Operations.RunAsync(new("editImage","Edit Photo"),[png],Path.Combine(root,"edit"),progress,token,null,null,new(new PhotoAdjustments{Exposure=-1,Saturation=0},90,true)).GetAwaiter().GetResult().Single();
            using(var result=new MagickImage(edited)){using var pixels=result.GetPixels();var c=pixels.GetPixel(10,10).ToColor()!;Check(result.Width==200&&result.Height==320&&Math.Abs(c.R-c.B)<3&&c.R is > 70 and < 120,"Edit photo rotates, desaturates and darkens");}
            var pdf=Operations.RunAsync(new("createPDF","Create PDF"),[png,jpg],Path.Combine(root,"pdf"),progress,token).GetAwaiter().GetResult().Single();
            using(var document=PdfDocument.Open(pdf))Check(document.NumberOfPages==2,"Images to PDF pages");
            var merged=Operations.RunAsync(new("mergePDF","Merge PDFs"),[pdf,pdf],Path.Combine(root,"merge"),progress,token).GetAwaiter().GetResult().Single();
            using(var document=PdfDocument.Open(merged))Check(document.NumberOfPages==4,"Merged PDF pages");
            var parts=Operations.RunAsync(new("splitPDF","Split PDF"),[merged],Path.Combine(root,"split"),progress,token).GetAwaiter().GetResult();
            Check(parts.Count==4,"Split PDF files");

            var qr=Path.Combine(root,"Code.png");
            var matrix=new MultiFormatWriter().encode("https://github.com/orkunisitmak/nashi-app",BarcodeFormat.QR_CODE,240,240,new Dictionary<EncodeHintType,object>{[EncodeHintType.MARGIN]=4});
            using(var image=new MagickImage(MagickColors.White,240,240)){using var pixels=image.GetPixels();for(var y=0;y<240;y++)for(var x=0;x<240;x++)if(matrix[x,y])pixels.SetPixel(x,y,[0,0,0]);image.Write(qr,MagickFormat.Png);}
            Check(Operations.ReadQr([qr]).SequenceEqual(new[]{"https://github.com/orkunisitmak/nashi-app"}),"QR decode");

            var zip=DocumentEngine.Zip([png],Path.Combine(root,"zip"));
            Check(Operations.Formats([zip]).Contains("tar"),"Archive formats");
            var extracted=Operations.RunAsync(new("extractArchive","Extract Archive"),[zip],Path.Combine(root,"extract"),progress,token).GetAwaiter().GetResult();
            Check(extracted.Count>0,"Archive extraction");

            if(Operations.Media is not null)
            {
                var wav=Path.Combine(root,"Tone.wav");
                ProcessRunner.RunAsync(Operations.Executable("ffmpeg")!,["-hide_banner","-loglevel","error","-f","lavfi","-i","sine=frequency=440:duration=2","-ar","44100",wav],token).GetAwaiter().GetResult();
                Check(File.Exists(wav),"Audio fixture");
                var mp3=Operations.RunAsync(new("convert","mp3","mp3"),[wav],Path.Combine(root,"media"),progress,token).GetAwaiter().GetResult().Single();
                Check(new FileInfo(mp3).Length>0,"Audio conversion");
                var normalized=Operations.RunAsync(new("normalizeAudio","Normalize Volume"),[wav],Path.Combine(root,"normalize"),progress,token).GetAwaiter().GetResult();
                Check(normalized.Count==1,"Normalize audio");
                var mono=Operations.RunAsync(new("audioChannels","Convert Audio Channels"),[wav],Path.Combine(root,"channels"),progress,token,new(){Channels=1}).GetAwaiter().GetResult().Single();
                Check(ProcessRunner.RunAsync(Operations.Executable("ffprobe")!,["-v","error","-select_streams","a:0","-show_entries","stream=channels","-of","csv=p=0",mono],token).GetAwaiter().GetResult().Output.Trim()=="1","Mono output");
                var clip=Path.Combine(root,"Clip.mp4");
                ProcessRunner.RunAsync(Operations.Executable("ffmpeg")!,["-hide_banner","-loglevel","error","-f","lavfi","-i","testsrc=s=320x240:d=2","-f","lavfi","-i","sine=duration=2","-shortest","-pix_fmt","yuv420p",clip],token).GetAwaiter().GetResult();
                Check(Operations.Tools([clip,clip]).Any(t=>t.Id=="joinVideos")&&Operations.Tools([clip]).Any(t=>t.Id=="changeVideoSpeed"),"Video tools offered");
                var fast=Operations.RunAsync(new("changeVideoSpeed","Change Video Speed"),[clip],Path.Combine(root,"speed"),progress,token,new(){Speed=2}).GetAwaiter().GetResult().Single();
                var fastDuration=Operations.Media.InspectAsync(fast,token).GetAwaiter().GetResult().Duration;
                Check(fastDuration is > .8 and < 1.3,"2× speed halves the duration");
                var joined=Operations.RunAsync(new("joinVideos","Join Videos"),[clip,clip],Path.Combine(root,"join"),progress,token).GetAwaiter().GetResult().Single();
                var joinedDuration=Operations.Media.InspectAsync(joined,token).GetAwaiter().GetResult().Duration;
                Check(joinedDuration is > 3.6 and < 4.4,"Joined duration");
                var trimmed=Operations.RunAsync(new("trimVideo","Trim Video"),[clip],Path.Combine(root,"trim"),progress,token,new(){Start=.5,End=1.5}).GetAwaiter().GetResult().Single();
                Check(Operations.Media.InspectAsync(trimmed,token).GetAwaiter().GetResult().Duration is > .8 and < 1.25,"Trim keeps the In/Out range");
                var cut=Operations.RunAsync(new("trimAudio","Trim Audio"),[wav],Path.Combine(root,"trim-audio"),progress,token,new(){Start=0,End=1}).GetAwaiter().GetResult().Single();
                Check(Operations.Media.InspectAsync(cut,token).GetAwaiter().GetResult().Duration is > .9 and < 1.1,"Audio trim");
                var halves=Operations.RunAsync(new("splitVideo","Split Video"),[clip],Path.Combine(root,"split-video"),progress,token,new(){EqualClips=2}).GetAwaiter().GetResult();
                Check(halves.Count==2,"Split into equal parts");
                var stills=Operations.RunAsync(new("videoSnapshots","Video Snapshots"),[clip],Path.Combine(root,"snapshots"),progress,token,new(){SnapshotTimes=[.2,1,1.8]}).GetAwaiter().GetResult();
                Check(stills.Count==3&&stills.All(s=>new MagickImageInfo(s).Width==320),"Snapshots");
                var visual=Operations.RunAsync(new("audioToVideo","Audio Visualizer"),[wav],Path.Combine(root,"visual"),progress,token,new(){Visualization="bars"}).GetAwaiter().GetResult().Single();
                Check(Operations.Media.InspectAsync(visual,token).GetAwaiter().GetResult().Video is not null,"Audio visualizer video");
                Check(TrimDialog.Parse("1:15")==75&&TrimDialog.Parse("1:01:15.5")==3675.5&&TrimDialog.Parse("x") is null&&TrimDialog.Format(75.25)=="1:15.2","Time fields");
            }
            Check(Hash(png)==originalHash,"Original is unchanged");
            Console.WriteLine($"{{\"passed\":{passed},\"scope\":\"Linux preview image, PDF, QR, archive, audio and video exports\"}}");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine($"FAILED after {passed} checks: {error}");return 1;}
    }
    private static string Hash(string file)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
    private static void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);passed++;}
}
