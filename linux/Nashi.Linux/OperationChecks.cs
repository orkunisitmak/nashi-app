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
            Check(Operations.Tools([png]).Select(t=>t.Id).SequenceEqual(new[]{"compress","removeMetadata","editImage","annotateImage","frameImage","cropImage","redactImage","readQRCode"}),"PNG tools");
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
            {
                var photo=Path.Combine(root,"Big.jpg");using(var image=new MagickImage("plasma:fractal",1200,800)){image.Quality=95;image.Write(photo,MagickFormat.Jpeg);}
                var size=new FileInfo(photo).Length;ToolSettings Squeeze(CompressionOptions o)=>new(Compress:o);
                var half=Operations.RunAsync(new("compress","Compress"),[photo],Path.Combine(root,"compress-half"),progress,token,Squeeze(new(ReductionPercent:50))).GetAwaiter().GetResult().Single();
                Check(new FileInfo(half).Length<=size/2&&new MagickImageInfo(half).Width==1200,"Compress to half the size by quality first");
                var tiny=Operations.RunAsync(new("compress","Compress"),[photo],Path.Combine(root,"compress-tiny"),progress,token,Squeeze(new(TargetBytes:15_000))).GetAwaiter().GetResult().Single();
                Check(new FileInfo(tiny).Length<=15_000&&new MagickImageInfo(tiny).Width<1200,"A small target also shrinks the image");
                var edge=Operations.RunAsync(new("compress","Compress"),[photo],Path.Combine(root,"compress-edge"),progress,token,Squeeze(new(LongestEdge:600))).GetAwaiter().GetResult().Single();
                Check(new MagickImageInfo(edge).Width==600&&new MagickImageInfo(edge).Height==400,"Longest-edge size preset");
                try{Compression.Target(1000,-1,null);Check(false,"Bad targets are rejected");}catch(ArgumentException){Check(true,"Bad targets are rejected");}
            }
            Check(Operations.Tools([png]).Any(t=>t.Id=="annotateImage")&&!Operations.Tools([png,jpg]).Any(t=>t.Id=="annotateImage"),"Annotate offered for one image");
            {
                var canvas=Path.Combine(root,"Canvas.png");using(var image=new MagickImage(MagickColors.White,400,300))image.Write(canvas,MagickFormat.Png);
                var lens=Path.Combine(root,"Lens.png");using(var image=new MagickImage(MagickColors.White,400,300)){new ImageMagick.Drawing.Drawables().FillColor(MagickColors.Black).Rectangle(195,145,205,155).Draw(image);image.Write(lens,MagickFormat.Png);}
                MagickColor At(string file,int x,int y){using var image=new MagickImage(file);using var pixels=image.GetPixels();return (MagickColor)pixels.GetPixel(x,y).ToColor()!;}
                string Draw(string file,string name,params Mark[] marks)=>Operations.RunAsync(new("annotateImage","Annotate Photo"),[file],Path.Combine(root,"annotate-"+name),progress,token,new(Annotate:new(marks))).GetAwaiter().GetResult().Single();
                var box=Draw(canvas,"rectangle",new Mark("rectangle",100,100,300,200,"#FF0000",6));
                Check(At(box,100,150).R>200&&At(box,100,150).G<60&&At(box,200,150).G>250,"Annotate rectangle is an outline, not a fill");
                var oval=Draw(canvas,"ellipse",new Mark("ellipse",100,100,300,200,"#0000FF",6));
                Check(At(oval,100,150).B>200&&At(oval,100,150).R<60&&At(oval,200,150).R>250&&At(oval,102,102).R>250,"Annotate ellipse");
                var marker=Draw(canvas,"marker",new Mark("marker",0,0,0,0,"#000000",5,Points:[(50,50),(350,50)]));
                var ink=At(marker,200,50);Check(ink.R is > 60 and < 200&&At(marker,200,120).R>250,"Annotate marker is translucent");
                var dot=Draw(canvas,"dot",new Mark("freehand",0,0,0,0,"#000000",10,Points:[(200,150)]),new Mark("marker",0,0,0,0,"#000000",4,Points:[(20,20),(80,20)]));
                Check(At(dot,200,150).R<40&&At(dot,50,20).R<200,"Annotate a click and a two-point stroke");
                var pen=Draw(canvas,"freehand",new Mark("freehand",0,0,0,0,"#000000",4,Points:[(50,250),(200,240),(350,250)]));
                Check(At(pen,50,250).R<40&&At(pen,200,150).R>250,"Annotate freehand");
                var arrow=Draw(canvas,"arrow",new Mark("arrow",50,150,350,150,"#00AA00",4));
                // The head is 16 px long and 16 px wide just before the tip, wider than the 4 px shaft.
                Check(At(arrow,340,150).G>150&&At(arrow,340,155).G>150&&At(arrow,200,155).R>250&&At(arrow,200,150).R<60,"Annotate arrow head");
                var text=Draw(canvas,"text",new Mark("text",20,20,0,0,"#000000",4,"Şğİ Nashi",48));
                using(var image=new MagickImage(text)){using var pixels=image.GetPixels();var dark=0;for(var y=20;y<80;y++)for(var x=20;x<300;x++)if(pixels.GetPixel(x,y).ToColor()!.R<100)dark++;Check(dark>500,"Annotate text with Turkish letters");}
                var number=Draw(canvas,"number",new Mark("number",200,150,0,0,"#2E9E4F",4,"1",40));
                Check(At(number,200-20,150).G>120&&At(number,200-20,150).R<80&&At(number,200+30,150).R>250,"Annotate number badge");
                var callout=Draw(canvas,"callout",new Mark("callout",50,50,350,150,"#111111",4,"Hello",40));
                Check(At(callout,60,140).R<30&&At(callout,345,60).R<30&&At(callout,200,250).R>250,"Annotate callout box");
                var magnified=Draw(lens,"magnifier",new Mark("magnifier",180,130,220,170,"#FF0000",2));
                // The 10 px black square becomes 20 px wide at the same center.
                Check(At(magnified,192,150).R<40&&At(lens,192,150).R>250&&At(magnified,215,150).R>200,"Annotate magnifier zooms 2×");
                var sticker=Path.Combine(root,"Sticker.png");using(var image=new MagickImage(new MagickColor("#0000FF"),10,10))image.Write(sticker,MagickFormat.Png);
                var placed=Draw(canvas,"image",new Mark("image",300,20,380,100,Picture:File.ReadAllBytes(sticker)));
                Check(At(placed,340,60).B>240&&At(placed,340,60).R<10&&At(placed,290,60).R>250,"Annotate places an image");
                var stack=Draw(canvas,"order",new Mark("callout",50,50,350,250,"#000000",4,"",40),new Mark("rectangle",100,100,300,200,"#FF0000",6));
                Check(At(stack,100,150).R>200&&At(stack,100,150).G<60,"Annotate draws marks in order");
                try{Annotations.Apply(new MagickImage(MagickColors.White,10,10),new([new("line",0,0,5,5,"red")]));Check(false,"Annotate rejects bad colors");}catch(ArgumentException){Check(true,"Annotate rejects bad colors");}
                try{Annotations.Apply(new MagickImage(MagickColors.White,10,10),new([]));Check(false,"Annotate needs a mark");}catch(ArgumentException){Check(true,"Annotate needs a mark");}
                var moved=MarkGeometry.Moved(new Mark("freehand",1,1,2,2,Points:[(1,1),(2,2)]),10,5);var scaled=MarkGeometry.Scaled(new([new Mark("text",10,10,10,10,FontSize:40)]),.5).Marks[0];
                Check(moved.Points![1]==(12,7)&&moved.X1==11&&scaled.X1==5&&scaled.FontSize==20,"Annotation move and preview scale");
            }
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
            Check(Operations.Tools([pdf]).Select(t=>t.Id).Intersect(new[]{"compress","removeMetadata"}).Count()==2,"PDF compress and metadata offered");
            {
                // A scanned (image-only) PDF is re-rendered as JPEG pages until it fits.
                var scan=Path.Combine(root,"Scan.png");using(var image=new MagickImage("plasma:fractal",1600,2200))image.Write(scan,MagickFormat.Png);
                var scanned=Operations.ImagesToPdf([scan],Path.Combine(root,"scan-big"),null,token).Single();var before=new FileInfo(scanned).Length;
                var fitted=Operations.RunAsync(new("compress","Compress"),[scanned],Path.Combine(root,"pdf-fit"),progress,token,new(Compress:new(TargetBytes:before/4))).GetAwaiter().GetResult().Single();
                using(var document=PdfDocument.Open(fitted))Check(new FileInfo(fitted).Length<=before/4&&document.NumberOfPages==1,"Scanned PDF fits a target size");
            }
            foreach(var (tool,suffix) in new[]{("compress"," Compressed.pdf"),("removeMetadata"," Metadata.pdf")})
            {
                var output=Operations.RunAsync(new(tool,tool),[pdf],Path.Combine(root,"pdf-"+tool),progress,token).GetAwaiter().GetResult().Single();
                using var document=PdfDocument.Open(output);Check(output.EndsWith(suffix,StringComparison.Ordinal)&&document.NumberOfPages==2,"PDF "+tool);
            }
            Check(Operations.Formats([pdf]).SequenceEqual(Operations.Office is null?new[]{"jpg","png","txt","md"}:Operations.Toolset.Ready?new[]{"docx","pages","pptx","xlsx","jpg","png","epub","txt","md"}:new[]{"docx","pptx","xlsx","jpg","png","epub","txt","md"}),"PDF formats");
            var pageImages=Operations.RunAsync(new("convert","PNG","png"),[pdf],Path.Combine(root,"pdf-png"),progress,token).GetAwaiter().GetResult();
            using(var first=new MagickImage(pageImages[0])){using var pixels=first.GetPixels();var c=pixels.GetPixel((int)first.Width/2,(int)first.Height/2).ToColor()!;
                // Pear.png is 320×200 px placed at 96 DPI, so the page is 240×150 pt and renders to 480×300 px at 144 DPI.
                Check(pageImages.Count==2&&Path.GetFileName(pageImages[1])=="Images Page 2.png"&&first.Width==480&&first.Height==300&&Math.Abs(c.R-0xE3)<6&&Math.Abs(c.B-0x59)<8,"PDF pages to PNG");}
            Check(Operations.Tools([pdf]).Any(t=>t.Id=="organizePDF")&&!Operations.Tools([pdf,pdf]).Any(t=>t.Id=="organizePDF"),"Organize PDF offered for one PDF");
            var organized=Operations.RunAsync(new("organizePDF","Organize PDF"),[pdf],Path.Combine(root,"organize"),progress,token,new(Organize:new([2,1,1],[0,90,0],false))).GetAwaiter().GetResult().Single();
            using(var document=PdfDocument.Open(organized))Check(Path.GetFileName(organized)=="Images Organized.pdf"&&document.NumberOfPages==3&&document.GetPage(2).Rotation.Value==90&&document.GetPage(1).Rotation.Value==0,"Organize order, duplicate and rotation");
            using(var raster=PdfRaster.Open(organized)){using var turned=new MagickImage(raster.RenderPage(1,72));Check(raster.PageCount==3&&turned.Width==150&&turned.Height==240,"Rotated page renders turned");}
            using(var raster=PdfRaster.Open(pdf)){using var thumb=new MagickImage(raster.Thumbnail(0,260));Check(thumb.Width==260,"Thumbnail long side");}
            var srt=Path.Combine(root,"Talk.srt");File.WriteAllText(srt,"1\n00:00:01,000 --> 00:00:02,500\nHello\n\n2\n00:00:03,000 --> 00:00:04,000\n<i>World</i>\n");
            Check(Operations.Formats([srt]).SequenceEqual(new[]{"vtt","txt"}),"Subtitle formats");
            if(Operations.Office is not null)Documents(root,progress,token);
            else Console.Error.WriteLine("LibreOffice not found: document conversions skipped.");
            var vtt=File.ReadAllText(Operations.RunAsync(new("convert","VTT","vtt"),[srt],Path.Combine(root,"subtitle-vtt"),progress,token).GetAwaiter().GetResult().Single());
            Check(vtt.StartsWith("WEBVTT",StringComparison.Ordinal)&&vtt.Contains("00:00:01.000 --> 00:00:02.500"),"SRT to VTT");
            var plain=File.ReadAllText(Operations.RunAsync(new("convert","TXT","txt"),[srt],Path.Combine(root,"subtitle-txt"),progress,token).GetAwaiter().GetResult().Single());
            Check(plain=="Hello\n\nWorld\n","Subtitle to text");

            var qr=Path.Combine(root,"Code.png");
            var matrix=new MultiFormatWriter().encode("https://github.com/orkunisitmak/nashi-app",BarcodeFormat.QR_CODE,240,240,new Dictionary<EncodeHintType,object>{[EncodeHintType.MARGIN]=4});
            using(var image=new MagickImage(MagickColors.White,240,240)){using var pixels=image.GetPixels();for(var y=0;y<240;y++)for(var x=0;x<240;x++)if(matrix[x,y])pixels.SetPixel(x,y,[0,0,0]);image.Write(qr,MagickFormat.Png);}
            Check(Operations.ReadQr([qr]).SequenceEqual(new[]{"https://github.com/orkunisitmak/nashi-app"}),"QR decode");
            var qrPdf=Operations.RunAsync(new("createPDF","Create PDF"),[png,qr],Path.Combine(root,"qr-pdf"),progress,token).GetAwaiter().GetResult().Single();
            using(var document=PdfDocument.Open(qrPdf))Check(document.NumberOfPages==2,"Grayscale PNG to PDF");
            Check(Operations.Tools([qrPdf]).Any(t=>t.Id=="readQRCode")&&Operations.ReadQr([qrPdf]).SequenceEqual(new[]{"https://github.com/orkunisitmak/nashi-app"}),"QR code on a PDF page");

            var zip=DocumentEngine.Zip([png],Path.Combine(root,"zip"));
            Check(Operations.Formats([zip]).Contains("tar"),"Archive formats");
            var extracted=Operations.RunAsync(new("extractArchive","Extract Archive"),[zip],Path.Combine(root,"extract"),progress,token).GetAwaiter().GetResult();
            Check(extracted.Count>0,"Archive extraction");

            if(Operations.Media is not null)
            {
                var wav=Path.Combine(root,"Tone.wav");
                ProcessRunner.RunAsync(Operations.Ffmpeg!,["-hide_banner","-loglevel","error","-f","lavfi","-i","sine=frequency=440:duration=2","-ar","44100",wav],token).GetAwaiter().GetResult();
                Check(File.Exists(wav),"Audio fixture");
                var mp3=Operations.RunAsync(new("convert","mp3","mp3"),[wav],Path.Combine(root,"media"),progress,token).GetAwaiter().GetResult().Single();
                Check(new FileInfo(mp3).Length>0,"Audio conversion");
                var normalized=Operations.RunAsync(new("normalizeAudio","Normalize Volume"),[wav],Path.Combine(root,"normalize"),progress,token).GetAwaiter().GetResult();
                Check(normalized.Count==1,"Normalize audio");
                var mono=Operations.RunAsync(new("audioChannels","Convert Audio Channels"),[wav],Path.Combine(root,"channels"),progress,token,new(Media:new(){Channels=1})).GetAwaiter().GetResult().Single();
                Check(ProcessRunner.RunAsync(Operations.Ffprobe!,["-v","error","-select_streams","a:0","-show_entries","stream=channels","-of","csv=p=0",mono],token).GetAwaiter().GetResult().Output.Trim()=="1","Mono output");
                var clip=Path.Combine(root,"Clip.mp4");
                ProcessRunner.RunAsync(Operations.Ffmpeg!,["-hide_banner","-loglevel","error","-f","lavfi","-i","testsrc=s=320x240:d=2","-f","lavfi","-i","sine=duration=2","-shortest","-pix_fmt","yuv420p",clip],token).GetAwaiter().GetResult();
                Check(Operations.Tools([clip,clip]).Any(t=>t.Id=="joinVideos")&&Operations.Tools([clip]).Any(t=>t.Id=="changeVideoSpeed"),"Video tools offered");
                var fast=Operations.RunAsync(new("changeVideoSpeed","Change Video Speed"),[clip],Path.Combine(root,"speed"),progress,token,new(Media:new(){Speed=2})).GetAwaiter().GetResult().Single();
                var fastDuration=Operations.Media.InspectAsync(fast,token).GetAwaiter().GetResult().Duration;
                Check(fastDuration is > .8 and < 1.3,"2× speed halves the duration");
                var joined=Operations.RunAsync(new("joinVideos","Join Videos"),[clip,clip],Path.Combine(root,"join"),progress,token).GetAwaiter().GetResult().Single();
                var joinedDuration=Operations.Media.InspectAsync(joined,token).GetAwaiter().GetResult().Duration;
                Check(joinedDuration is > 3.6 and < 4.4,"Joined duration");
                Check(Operations.Tools([wav]).Any(t=>t.Id=="removeMetadata")&&Operations.Tools([clip]).Any(t=>t.Id=="removeMetadata"),"Media metadata cleanup offered");
                var clean=Operations.RunAsync(new("removeMetadata","Remove Metadata"),[clip],Path.Combine(root,"media-metadata"),progress,token).GetAwaiter().GetResult().Single();
                Check(Operations.Media.InspectAsync(clean,token).GetAwaiter().GetResult().Duration is > 1.8 and < 2.2,"Media metadata cleanup keeps the clip");
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
                    double Peak(double start,double length)=>double.Parse(System.Text.RegularExpressions.Regex.Match(ProcessRunner.RunAsync(Operations.Ffmpeg!,["-hide_banner","-ss",start.ToString(System.Globalization.CultureInfo.InvariantCulture),"-t",length.ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",bleeped,"-af","volumedetect","-f","null","-"],token).GetAwaiter().GetResult().Error,@"max_volume: (-?[\d.]+|-inf) dB").Groups[1].Value.Replace("-inf","-200"),System.Globalization.CultureInfo.InvariantCulture);
                    var inside=Peak(.6,.5);var outside=Peak(1.5,.3);
                    Check(FeatureCatalog.Extension(bleeped)=="wav"&&outside>-30&&(quiet?inside<-80:inside is > -25 and < -5),"Bleep audio "+style);
                }
                Check(TrimDialog.Parse("1:15")==75&&TrimDialog.Parse("1:01:15.5")==3675.5&&TrimDialog.Parse("x") is null&&TrimDialog.Format(75.25)=="1:15.2","Time fields");
            }
            Metadata(root,png,progress,token);
            if(Environment.GetEnvironmentVariable("NASHI_VERIFY_WEB")=="1")Web(root,progress,token);
            {
                var source=Path.Combine(root,"move-source");var nested=Path.Combine(source,"Album","Inner");Directory.CreateDirectory(nested);
                File.WriteAllText(Path.Combine(nested,"note.txt"),"merhaba");File.CreateSymbolicLink(Path.Combine(source,"Album","link"),"/etc/hostname");
                var loose=Path.Combine(source,"Loose.txt");File.WriteAllText(loose,"tek");var target=Path.Combine(root,"move-target");
                var moved=FileActions.MoveAsync([Path.Combine(source,"Album"),loose],target,progress,token).GetAwaiter().GetResult();
                Check(moved.Count==2&&File.ReadAllText(Path.Combine(target,"Album","Inner","note.txt"))=="merhaba"&&new FileInfo(Path.Combine(target,"Album","link")).LinkTarget=="/etc/hostname"&&File.ReadAllText(Path.Combine(target,"Loose.txt"))=="tek","Move keeps folders, files and links");
                Check(!Directory.Exists(Path.Combine(source,"Album"))&&!File.Exists(loose),"Move removes the originals after publishing");
                var same=FileActions.MoveAsync([Path.Combine(target,"Loose.txt")],target,progress,token).GetAwaiter().GetResult();Check(same.Single()==Path.Combine(target,"Loose.txt"),"Moving into the same folder changes nothing");
                try{FileActions.MoveAsync([Path.Combine(target,"Album")],Path.Combine(target,"Album","Inner"),progress,token).GetAwaiter().GetResult();Check(false,"A folder cannot move into itself");}catch(ArgumentException){Check(true,"A folder cannot move into itself");}
                Check(FileActions.UriList(["/tmp/a b/ç.txt"])=="file:///tmp/a%20b/%C3%A7.txt","File URIs for the clipboard");
            }
            Check(Hash(png)==originalHash,"Original is unchanged");
            Console.WriteLine($"{{\"passed\":{passed},\"scope\":\"Linux preview image, PDF, QR, archive, audio and video exports\"}}");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine($"FAILED after {passed} checks: {error}");return 1;}
    }
    /// Real LibreOffice, PDFium, PdfPig, deno and Tesseract round trips, checked by reading the results back.
    private static void Documents(string root,IProgress<OperationProgress> progress,CancellationToken token)
    {
        string Convert(string input,string format,DocumentOptions? options=null)=>Operations.RunAsync(new("convert",format.ToUpperInvariant(),format),[input],Path.Combine(root,"doc-"+Path.GetFileNameWithoutExtension(input)+"-"+format),progress,token,new(Document:options)).GetAwaiter().GetResult().Single();
        string PdfWords(string file){using var document=PdfDocument.Open(file);return string.Join(" ",document.GetPages().Select(p=>p.Text));}
        string ZipText(string file,string entry){using var zip=System.IO.Compression.ZipFile.OpenRead(file);using var reader=new StreamReader(zip.GetEntry(entry)!.Open());return reader.ReadToEnd();}
        var md=Path.Combine(root,"Notlar.md");File.WriteAllText(md,"# Başlık\n\nMerhaba **dünya**, çalışıyor.\n\n- bir\n- iki\n");
        Check(Operations.Formats([md]).Contains("docx")&&Operations.Formats([md]).Contains("pdf"),"Markdown formats");
        var docx=Convert(md,"docx");Check(ZipText(docx,"word/document.xml").Contains("dünya"),"Markdown to DOCX");
        var docPdf=Convert(docx,"pdf");Check(PdfWords(docPdf).Contains("Merhaba"),"DOCX to PDF");
        var png=Convert(docx,"png");Check(new MagickImageInfo(png).Width>500,"DOCX to PNG pages");
        var html=Convert(docx,"html");Check(File.ReadAllText(html).Contains("dünya"),"DOCX to HTML");
        var htmlPdf=Convert(html,"pdf");Check(PdfWords(htmlPdf).Contains("Merhaba"),"HTML to PDF");
        var epub=Convert(md,"epub");Check(ZipText(epub,"mimetype")=="application/epub+zip","Markdown to EPUB");
        var epubText=Convert(epub,"txt");Check(File.ReadAllText(epubText).Contains("Merhaba"),"EPUB to text");
        var rtf=Path.Combine(root,"Mektup.rtf");File.WriteAllText(rtf,"{\\rtf1\\ansi\\ansicpg1254 {\\b Kal\\'fdn} metin \\u351?ey\\par}");
        Check(ZipText(Convert(rtf,"docx"),"word/document.xml").Contains("şey"),"RTF to DOCX keeps Turkish letters");
        if(Operations.Toolset.Ready)
        {
            var pages=Convert(docx,"pages");Check(new FileInfo(pages).Length>1000,"DOCX to Pages");
            var back=Convert(pages,"docx");Check(ZipText(back,"word/document.xml").Contains("Merhaba"),"Pages to DOCX");
        }
        var csv=Path.Combine(root,"Tablo.csv");File.WriteAllText(csv,"Ad,Puan\nAyşe,90\nMehmet,85\n");
        var xlsx=Convert(csv,"xlsx");Check(ZipText(xlsx,"xl/sharedStrings.xml").Contains("Ayşe"),"CSV to XLSX");
        var ods=Convert(xlsx,"ods");Check(ZipText(ods,"content.xml").Contains("Mehmet"),"XLSX to ODS");
        var csvBack=Convert(ods,"csv");Check(File.ReadAllText(csvBack).Contains("Ayşe,90"),"ODS to CSV");
        var sheetPdf=Convert(xlsx,"pdf");Check(PdfWords(sheetPdf).Contains("Mehmet"),"XLSX to PDF");
        var editable=Convert(docPdf,"docx",new(Layout:"editable"));Check(ZipText(editable,"word/document.xml").Contains("Merhaba"),"PDF to editable DOCX");
        var picture=Convert(docPdf,"docx",new(Layout:"preserve"));Check(ZipText(picture,"[Content_Types].xml").Contains("png")||System.IO.Compression.ZipFile.OpenRead(picture).Entries.Any(e=>e.FullName.StartsWith("word/media/",StringComparison.Ordinal)),"PDF to DOCX keeping the look");
        var slides=Convert(docPdf,"pptx");Check(System.IO.Compression.ZipFile.OpenRead(slides).Entries.Any(e=>e.FullName.StartsWith("ppt/slides/slide",StringComparison.Ordinal)),"PDF to PPTX");
        var slidesPdf=Convert(slides,"pdf");using(var a=PdfDocument.Open(docPdf))using(var b=PdfDocument.Open(slidesPdf))Check(a.NumberOfPages==b.NumberOfPages,"PPTX to PDF keeps the pages");
        var fixedEpub=Convert(docPdf,"epub",new(EpubLayout:"fixed"));Check(System.IO.Compression.ZipFile.OpenRead(fixedEpub).Entries.Any(e=>e.FullName=="OEBPS/page0.png"),"PDF to fixed EPUB");
        var tables=Convert(sheetPdf,"xlsx");Check(ZipText(tables,"xl/sharedStrings.xml").Contains("Mehmet"),"PDF tables to XLSX");
        // A scanned page: the words are only pixels, so text comes from OCR.
        if(Operations.Executable("tesseract") is not null)
        {
            var scan=Path.Combine(root,"Tarama.png");
            using(var image=new MagickImage(MagickColors.White,1400,400)){new ImageMagick.Drawing.Drawables().FillColor(MagickColors.Black).FontPointSize(110).Text(60,240,"NASHI ÇALIŞIYOR").Draw(image);image.Write(scan,MagickFormat.Png);}
            var scanned=Operations.ImagesToPdf([scan],Path.Combine(root,"scan-pdf"),null,token).Single();
            var recognized=File.ReadAllText(Convert(scanned,"txt"));Check(recognized.Contains("NASHI",StringComparison.OrdinalIgnoreCase),"Scanned PDF text through OCR");
        }
    }
    /// ExifTool edits and removal on a JPEG and a PDF; the encoded pixels must stay identical.
    private static void Metadata(string root,string png,IProgress<OperationProgress> progress,CancellationToken token)
    {
        var exif=Operations.Exif;
        if(exif.Executable is null){if(Operations.Executable("perl") is null){Console.Error.WriteLine("perl not found: ExifTool checks skipped.");return;}exif.InstallAsync(null,token).GetAwaiter().GetResult();}
        Check(exif.Executable is not null,"ExifTool set up from its verified download");
        var jpg=Path.Combine(root,"Tagged.jpg");using(var image=new MagickImage(png)){image.Format=MagickFormat.Jpeg;image.Write(jpg);}
        ToolSettings Edit(bool all,params (string,string)[] fields)=>new(Metadata:new(all,fields.ToDictionary(f=>f.Item1,f=>f.Item2)));
        var tagged=Operations.RunAsync(new("removeMetadata","Metadata"),[jpg],Path.Combine(root,"exif-edit"),progress,token,Edit(false,("EXIF:Artist","Oğuz Test"),("EXIF:Copyright","CC0"))).GetAwaiter().GetResult().Single();
        var tags=exif.ReadAsync(tagged,token).GetAwaiter().GetResult();
        Check(tags.GetValueOrDefault("EXIF:Artist")=="Oğuz Test"&&tags.GetValueOrDefault("EXIF:Copyright")=="CC0"&&Path.GetFileName(tagged)=="Tagged Metadata.jpg","Edit EXIF fields");
        string Pixels(string file){using var image=new MagickImage(file);return image.Signature;}
        Check(Pixels(tagged)==Pixels(jpg),"Metadata edit keeps the encoded pixels");
        var cleaned=Operations.RunAsync(new("removeMetadata","Metadata"),[tagged],Path.Combine(root,"exif-clean"),progress,token,Edit(true)).GetAwaiter().GetResult().Single();
        Check(!exif.ReadAsync(cleaned,token).GetAwaiter().GetResult().ContainsKey("EXIF:Artist")&&Pixels(cleaned)==Pixels(jpg),"Remove all EXIF without re-encoding");
        var pdf=Operations.ImagesToPdf([png],Path.Combine(root,"exif-pdf"),null,token).Single();
        var titled=Operations.RunAsync(new("removeMetadata","Metadata"),[pdf],Path.Combine(root,"exif-pdf-edit"),progress,token,Edit(false,("PDF:Title","Kitap"),("PDF:Author","Nashi"))).GetAwaiter().GetResult().Single();
        using(var document=PdfDocument.Open(titled))Check(document.Information.Title=="Kitap"&&document.Information.Author=="Nashi","Edit PDF title and author");
        if(Operations.Media is not null)
        {
            var wav=Path.Combine(root,"Tone.wav");var mp3=Path.Combine(root,"Song.mp3");
            ProcessRunner.RunAsync(Operations.Ffmpeg!,["-hide_banner","-loglevel","error","-i",wav,"-metadata","title=Eski","-metadata","artist=Biri",mp3],token).GetAwaiter().GetResult();
            var before=MediaMetadata.Read(Operations.Toolset,mp3,token).GetAwaiter().GetResult();Check(before.GetValueOrDefault("title")=="Eski","Read media tags");
            var renamed=Operations.RunAsync(new("removeMetadata","Metadata"),[mp3],Path.Combine(root,"media-tags"),progress,token,Edit(false,("title","Yeni Şarkı"))).GetAwaiter().GetResult().Single();
            var after=MediaMetadata.Read(Operations.Toolset,renamed,token).GetAwaiter().GetResult();
            Check(after.GetValueOrDefault("title")=="Yeni Şarkı"&&after.GetValueOrDefault("artist")=="Biri","Edit one media tag and keep the others");
        }
    }
    /// Opt-in (NASHI_VERIFY_WEB=1): real public downloads. The image is pinned by commit and SHA-256, as on Windows.
    private static void Web(string root,IProgress<OperationProgress> progress,CancellationToken token)
    {
        var web=new WebDownload(Operations.Toolset);
        var image=web.DownloadAsync("https://raw.githubusercontent.com/orkunisitmak/nashi-app/4f051ab04bc48ea2efb5338abed4990536cb4d97/Resources/Brand/NashiMark.png","image",Path.Combine(root,"web-image"),progress,token).GetAwaiter().GetResult().Single();
        Check(Hash(image).Equals("27128b31fa9f3b7cf6c21c4b377be387b1503159b0227335144a17c2badf489a",StringComparison.OrdinalIgnoreCase),"Pinned public image download");
        Check(WebMediaPayload.Parse("https://youtu.be/jNQXAC9IVRw?si=x") is {Kind:"youtube",Url:"https://www.youtube.com/watch?v=jNQXAC9IVRw"}&&WebMediaPayload.Parse("https://www.instagram.com/someone/p/ABC123/") is {Kind:"instagramPhotos"},"Link kinds");
        var thumbnail=web.DownloadAsync("https://youtu.be/jNQXAC9IVRw","thumbnail",Path.Combine(root,"web-thumb"),progress,token).GetAwaiter().GetResult().Single();
        Check(new MagickImageInfo(thumbnail).Width>=480,"YouTube thumbnail");
        var info=web.InspectAsync("https://www.youtube.com/watch?v=jNQXAC9IVRw",token).GetAwaiter().GetResult();
        Check(info.Title.Contains("zoo",StringComparison.OrdinalIgnoreCase)&&info.Duration is > 15 and < 25,"Inspect a YouTube video with yt-dlp");
        var audio=web.DownloadAsync("https://www.youtube.com/watch?v=jNQXAC9IVRw","audio",Path.Combine(root,"web-audio"),progress,token).GetAwaiter().GetResult().Single();
        Check(FeatureCatalog.Extension(audio)=="mp3"&&Operations.Media!.InspectAsync(audio,token).GetAwaiter().GetResult().Duration is > 15 and < 25,"Download YouTube audio as MP3");
        var video=web.DownloadAsync("https://www.youtube.com/watch?v=jNQXAC9IVRw","video",Path.Combine(root,"web-video"),progress,token,maximumHeight:144).GetAwaiter().GetResult().Single();
        Check(Operations.Media!.InspectAsync(video,token).GetAwaiter().GetResult().Video is {Height:<=144},"Download YouTube video with a height limit");
    }
    private static string Hash(string file)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
    private static void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);passed++;}
}
