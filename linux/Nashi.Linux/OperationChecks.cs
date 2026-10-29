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
            Check(Operations.Tools([png]).Select(t=>t.Id).SequenceEqual(new[]{"compress","removeMetadata","readQRCode"}),"PNG tools");
            foreach(var format in Operations.Formats([png]))
            {
                var output=Operations.RunAsync(new("convert",format,format),[png],Path.Combine(root,"convert-"+format),progress,token).GetAwaiter().GetResult().Single();
                Check(new FileInfo(output).Length>0&&FeatureCatalog.Extension(output)==format,"Export "+format);
                if(format!="pdf"){using var result=new MagickImage(output);Check(result.Width==320&&result.Height==200,"Dimensions "+format);}
            }
            var jpg=Directory.GetFiles(Path.Combine(root,"convert-jpg")).Single();
            var again=Operations.RunAsync(new("convert","jpg","jpg"),[jpg],Path.Combine(root,"convert-jpg"),progress,token).GetAwaiter().GetResult().Single();
            Check(again!=jpg&&File.Exists(jpg)&&Path.GetFileName(again)=="Pear 2.jpg","Existing output is never overwritten");

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
            }
            Check(Hash(png)==originalHash,"Original is unchanged");
            Console.WriteLine($"{{\"passed\":{passed},\"scope\":\"Linux preview image, PDF, QR, archive and audio exports\"}}");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine($"FAILED after {passed} checks: {error}");return 1;}
    }
    private static string Hash(string file)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
    private static void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);passed++;}
}
