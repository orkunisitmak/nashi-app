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
            Check(Operations.Tools([png]).Select(t=>t.Id).SequenceEqual(new[]{"compress","removeMetadata","editImage","frameImage","cropImage","redactImage","readQRCode"}),"PNG tools");
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
            var cropped=Operations.RunAsync(new("cropImage","Crop Image"),[png],Path.Combine(root,"crop"),progress,token,new(Crop:new(.25,.5,.5,.5))).GetAwaiter().GetResult().Single();
            using(var result=new MagickImage(cropped))Check(result.Width==160&&result.Height==100&&result.Page.X==0,"Crop size and page");
            var shared=Operations.RunAsync(new("cropImage","Crop Image"),[png,jpg],Path.Combine(root,"crop-many"),progress,token,new(Crop:new(0,0,1,.5))).GetAwaiter().GetResult();
            Check(shared.Count==2,"Crop several images");
            try{Operations.Crop(new MagickImage(MagickColors.White,10,10),new(.8,0,.5,1));Check(false,"Crop outside the image is rejected");}catch(ArgumentException){Check(true,"Crop outside the image is rejected");}
            var edited=Operations.RunAsync(new("editImage","Edit Photo"),[png],Path.Combine(root,"edit"),progress,token,new(Photo:new(new PhotoAdjustments{Exposure=-1,Saturation=0},90,true))).GetAwaiter().GetResult().Single();
            using(var result=new MagickImage(edited)){using var pixels=result.GetPixels();var c=pixels.GetPixel(10,10).ToColor()!;Check(result.Width==200&&result.Height==320&&Math.Abs(c.R-c.B)<3&&c.R is > 70 and < 120,"Edit photo rotates, desaturates and darkens");}
            var framed=Operations.RunAsync(new("frameImage","Add BG"),[jpg],Path.Combine(root,"frame"),progress,token,new(Frame:new(Padding:40,Color:"#123456",Shadow:0,CornerRadius:30))).GetAwaiter().GetResult().Single();
            using(var result=new MagickImage(framed)){using var pixels=result.GetPixels();var corner=pixels.GetPixel(2,2).ToColor()!;var middle=pixels.GetPixel(200,140).ToColor()!;var rounded=pixels.GetPixel(41,41).ToColor()!;
                Check(result.Width==400&&result.Height==280&&FeatureCatalog.Extension(framed)=="png"&&corner.R==0x12&&corner.B==0x56&&Math.Abs(middle.R-0xE3)<4&&rounded.B==0x56,"Frame size, background, image and rounded corner");}
            var gradient=Operations.RunAsync(new("frameImage","Add BG"),[png],Path.Combine(root,"frame-gradient"),progress,token,new(Frame:new(Backdrop:"gradient",Color:"#000000",GradientEnd:"#FFFFFF",Width:500,Height:500,Shadow:12))).GetAwaiter().GetResult().Single();
            using(var result=new MagickImage(gradient)){using var pixels=result.GetPixels();Check(pixels.GetPixel(0,0).ToColor()!.R<5&&pixels.GetPixel(499,499).ToColor()!.R>250&&result.Width==500,"Gradient canvas");}
            var blue=Path.Combine(root,"Blue.png");using(var image=new MagickImage(new MagickColor("#2050C0"),100,300))image.Write(blue,MagickFormat.Png);
            Check(Operations.Tools([png,blue]).Any(t=>t.Id=="createCollage"),"Collage offered");
            var collage=Operations.RunAsync(new("createCollage","Create Collage"),[png,blue],Path.Combine(root,"collage"),progress,token,new(Collage:new(Layout:"row",Width:400,Height:200,Spacing:0,Padding:0,Order:[1,0]))).GetAwaiter().GetResult().Single();
            using(var result=new MagickImage(collage)){using var pixels=result.GetPixels();Check(Path.GetFileName(collage)=="Collage.png"&&result.Width==400&&pixels.GetPixel(50,100).ToColor()!.B>180&&pixels.GetPixel(350,100).ToColor()!.R>200,"Collage size and order");}
            try{using var _=Compositions.Collage([new MagickImage(MagickColors.Red,10,10),new MagickImage(MagickColors.Red,10,10)],new(Width:100,Height:100,Padding:60));Check(false,"Collage rejects padding that leaves no room");}catch(ArgumentException){Check(true,"Collage rejects padding that leaves no room");}
            var redacted=Operations.RunAsync(new("redactImage","Redact Photo"),[png],Path.Combine(root,"redact"),progress,token,new(Redact:new([new(0,0,.25,.5)]))).GetAwaiter().GetResult().Single();
            using(var result=new MagickImage(redacted)){using var pixels=result.GetPixels();var inside=pixels.GetPixel(40,50).ToColor()!;var outside=pixels.GetPixel(200,150).ToColor()!;Check(FeatureCatalog.Extension(redacted)=="png"&&inside.R<3&&inside.G<3&&outside.R>200,"Redact black box keeps the format");}
            var noisy=Path.Combine(root,"Noise.png");using(var image=new MagickImage("plasma:fractal",256,256)){image.Write(noisy,MagickFormat.Png);}
            foreach(var style in new[]{"pixelate","blur"})
            {
                var covered=Operations.RunAsync(new("redactImage","Redact Photo"),[noisy],Path.Combine(root,"redact-"+style),progress,token,new(Redact:new([new(0,0,.5,.5)],style))).GetAwaiter().GetResult().Single();
                using var before=new MagickImage(noisy);using var after=new MagickImage(covered);using var a=after.GetPixels();using var b=before.GetPixels();
                // Neighbouring pixels inside the area become (nearly) equal; outside the area nothing changes.
                double Step(IPixelCollection<byte> p,int x,int y){var c=p.GetPixel(x,y).ToColor()!;var d=p.GetPixel(x+1,y).ToColor()!;return Math.Abs(c.R-d.R)+Math.Abs(c.G-d.G)+Math.Abs(c.B-d.B);}
                var inside=Enumerable.Range(0,120).Sum(n=>Step(a,n,n));var original=Enumerable.Range(0,120).Sum(n=>Step(b,n,n));
                Check(inside<original/3&&a.GetPixel(200,200).ToColor()!.Equals(b.GetPixel(200,200).ToColor()),"Redact "+style);
            }
            try{using var image=new MagickImage(MagickColors.Red,10,10);Compositions.Redact(image,new([new(.5,.5,.8,.2)]));Check(false,"Redact rejects areas outside the image");}catch(ArgumentException){Check(true,"Redact rejects areas outside the image");}
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
                var mono=Operations.RunAsync(new("audioChannels","Convert Audio Channels"),[wav],Path.Combine(root,"channels"),progress,token,new(Media:new(){Channels=1})).GetAwaiter().GetResult().Single();
                Check(ProcessRunner.RunAsync(Operations.Executable("ffprobe")!,["-v","error","-select_streams","a:0","-show_entries","stream=channels","-of","csv=p=0",mono],token).GetAwaiter().GetResult().Output.Trim()=="1","Mono output");
                var clip=Path.Combine(root,"Clip.mp4");
                ProcessRunner.RunAsync(Operations.Executable("ffmpeg")!,["-hide_banner","-loglevel","error","-f","lavfi","-i","testsrc=s=320x240:d=2","-f","lavfi","-i","sine=duration=2","-shortest","-pix_fmt","yuv420p",clip],token).GetAwaiter().GetResult();
                Check(Operations.Tools([clip,clip]).Any(t=>t.Id=="joinVideos")&&Operations.Tools([clip]).Any(t=>t.Id=="changeVideoSpeed"),"Video tools offered");
                var fast=Operations.RunAsync(new("changeVideoSpeed","Change Video Speed"),[clip],Path.Combine(root,"speed"),progress,token,new(Media:new(){Speed=2})).GetAwaiter().GetResult().Single();
                var fastDuration=Operations.Media.InspectAsync(fast,token).GetAwaiter().GetResult().Duration;
                Check(fastDuration is > .8 and < 1.3,"2× speed halves the duration");
                var joined=Operations.RunAsync(new("joinVideos","Join Videos"),[clip,clip],Path.Combine(root,"join"),progress,token).GetAwaiter().GetResult().Single();
                var joinedDuration=Operations.Media.InspectAsync(joined,token).GetAwaiter().GetResult().Duration;
                Check(joinedDuration is > 3.6 and < 4.4,"Joined duration");
                var trimmed=Operations.RunAsync(new("trimVideo","Trim Video"),[clip],Path.Combine(root,"trim"),progress,token,new(Media:new(){Start=.5,End=1.5})).GetAwaiter().GetResult().Single();
                Check(Operations.Media.InspectAsync(trimmed,token).GetAwaiter().GetResult().Duration is > .8 and < 1.25,"Trim keeps the In/Out range");
                var cut=Operations.RunAsync(new("trimAudio","Trim Audio"),[wav],Path.Combine(root,"trim-audio"),progress,token,new(Media:new(){Start=0,End=1})).GetAwaiter().GetResult().Single();
                Check(Operations.Media.InspectAsync(cut,token).GetAwaiter().GetResult().Duration is > .9 and < 1.1,"Audio trim");
                var halves=Operations.RunAsync(new("splitVideo","Split Video"),[clip],Path.Combine(root,"split-video"),progress,token,new(Media:new(){EqualClips=2})).GetAwaiter().GetResult();
                Check(halves.Count==2,"Split into equal parts");
                var stills=Operations.RunAsync(new("videoSnapshots","Video Snapshots"),[clip],Path.Combine(root,"snapshots"),progress,token,new(Media:new(){SnapshotTimes=[.2,1,1.8]})).GetAwaiter().GetResult();
                Check(stills.Count==3&&stills.All(s=>new MagickImageInfo(s).Width==320),"Snapshots");
                var visual=Operations.RunAsync(new("audioToVideo","Audio Visualizer"),[wav],Path.Combine(root,"visual"),progress,token,new(Media:new(){Visualization="bars"})).GetAwaiter().GetResult().Single();
                Check(Operations.Media.InspectAsync(visual,token).GetAwaiter().GetResult().Video is not null,"Audio visualizer video");
                var croppedClip=Operations.RunAsync(new("cropVideo","Crop Video"),[clip],Path.Combine(root,"crop-video"),progress,token,new(Media:new(){Regions=[new(80,60,160,120)]})).GetAwaiter().GetResult().Single();
                var croppedInfo=Operations.Media.InspectAsync(croppedClip,token).GetAwaiter().GetResult();Check(croppedInfo.Video is {Width:160,Height:120}&&croppedInfo.HasAudio,"Crop video keeps the box and the sound");
                var hidden=Operations.RunAsync(new("redactVideo","Redact Video"),[clip],Path.Combine(root,"redact-video"),progress,token,new(Media:new(){Regions=[new(0,0,100,100)],RedactionMode="solid"})).GetAwaiter().GetResult().Single();
                using(var frame=new MagickImage(Operations.VideoFrameAsync(hidden,1,token).GetAwaiter().GetResult())){using var pixels=frame.GetPixels();var c=pixels.GetPixel(50,50).ToColor()!;var open=pixels.GetPixel(250,200).ToColor()!;Check(frame.Width==320&&c.R<20&&c.G<20&&c.B<20&&open.R+open.G+open.B>60,"Redact video covers the box");}
                foreach(var (style,quiet) in new[]{("mute",true),("bleep",false)})
                {
                    var bleeped=Operations.RunAsync(new("redactAudio","Bleep Audio"),[wav],Path.Combine(root,"bleep-"+style),progress,token,new(Media:new(){AudioRedactions=[new(.5,1.2)],RedactionMode=style})).GetAwaiter().GetResult().Single();
                    // Loudest sample inside and outside the range, in dB, from FFmpeg's volumedetect.
                    double Peak(double start,double length)=>double.Parse(System.Text.RegularExpressions.Regex.Match(ProcessRunner.RunAsync(Operations.Executable("ffmpeg")!,["-hide_banner","-ss",start.ToString(System.Globalization.CultureInfo.InvariantCulture),"-t",length.ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",bleeped,"-af","volumedetect","-f","null","-"],token).GetAwaiter().GetResult().Error,@"max_volume: (-?[\d.]+|-inf) dB").Groups[1].Value.Replace("-inf","-200"),System.Globalization.CultureInfo.InvariantCulture);
                    var inside=Peak(.6,.5);var outside=Peak(1.5,.3);
                    Check(FeatureCatalog.Extension(bleeped)=="wav"&&outside>-30&&(quiet?inside<-80:inside is > -25 and < -5),"Bleep audio "+style);
                }
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
