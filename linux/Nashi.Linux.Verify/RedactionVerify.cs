using ImageMagick;
using ImageMagick.Drawing;
using Nashi.Core;
using Nashi.Windows;
using System.Security.Cryptography;
using System.Text;

internal static class RedactionVerify
{
    internal static async Task Run(LinuxEngine engine,string root,string output,Action<bool,string> check,Func<Func<Task>,string,Task> mustFail)
    {
        const string header="level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n";
        const string word="5\t1\t1\t1\t1\t1\t10\t20\t20\t10\t89.5\tPRIVATE\n";
        var parsed=TextDetection.ParseTsv(header+word+"4\t1\t1\t1\t1\t0\t0\t0\t100\t100\t-1\t\n",100,100);
        check(parsed.Count==1&&parsed[0].Text=="PRIVATE"&&parsed[0].Region==new MediaRegion(8,18,24,14),"OCR TSV uses padded word boxes, not line/page boxes");
        check(TextDetection.ParseTsv(header+"5\t1\t1\t1\t1\t1\t-2\t0\t6\t5\t85\tEDGE\n",100,100).Single().Region==new MediaRegion(0,0,6,7),"OCR boxes clip to source bounds");
        check(TextDetection.ParseTsv(header+word.Replace("89.5","NaN",StringComparison.Ordinal),100,100).Count==0,"OCR rejects nonfinite confidence");
        await mustFail(()=>{TextDetection.ParseTsv("not TSV",100,100);return Task.CompletedTask;},"OCR missing word-coordinate schema fails explicitly");
        var many=new StringBuilder(header);for(var i=0;i<=TextDetection.MaximumWords;i++)many.Append(word);
        await mustFail(()=>{TextDetection.ParseTsv(many.ToString(),100,100);return Task.CompletedTask;},"OCR word limit rejects a partial detection result");
        var image=Path.Combine(root,"redaction-pattern.png");
        using(var source=new MagickImage(MagickColors.White,320,240)){
            var drawing=new Drawables().StrokeColor(MagickColors.Transparent);
            for(var y=0;y<240;y+=4)for(var x=0;x<320;x+=4)drawing.FillColor(((x+y)/4)%2==0?MagickColors.Black:MagickColors.White).Rectangle(x,y,x+3,y+3);
            drawing.Draw(source);source.Write(image);
        }
        var hash=SHA256.HashData(await File.ReadAllBytesAsync(image));var regions=new[]{new MediaRegion(20,20,64,64),new MediaRegion(160,100,80,60)};
        using var original=new MagickImage(image);using var originalPixels=original.GetPixels();
        foreach(var style in new[]{"solid","blur","pixelate"}){
            var exported=await engine.RunAsync("redactImage",[image],output,new(){Regions=regions,RedactionStyle=style});using var result=new MagickImage(exported.Single());using var pixels=result.GetPixels();
            check(result.Width==320&&result.Height==240,"multi-area redaction preserves size: "+style);
            var outsideSame=true;var selectedChanged=0;var solid=true;
            for(var y=0;y<240;y++)for(var x=0;x<320;x++){
                var before=originalPixels.GetPixel(x,y).ToColor()!;var after=pixels.GetPixel(x,y).ToColor()!;var inside=regions.Any(r=>x>=r.X&&x<r.X+r.Width&&y>=r.Y&&y<r.Y+r.Height);
                if(!inside)outsideSame&=before.R==after.R&&before.G==after.G&&before.B==after.B;
                else{if(before.R!=after.R)selectedChanged++;solid&=after.R==0&&after.G==0&&after.B==0;}
            }
            check(outsideSame,"redaction leaves unselected pixels unchanged: "+style);check(selectedChanged>1000,"both redaction areas change pixels: "+style);
            if(style=="solid")check(solid,"solid redaction covers every selected pixel with opaque black");
            else foreach(var region in regions){var color=pixels.GetPixel((int)region.X+20,(int)region.Y+20).ToColor()!;check(color.R>20&&color.R<235,"pattern detail removed inside each area: "+style);}
        }
        var transparent=Path.Combine(root,"transparent-redaction.png");using(var source=new MagickImage(MagickColors.Transparent,20,20))source.Write(transparent);
        var opaque=await engine.RunAsync("redactImage",[transparent],output,new(){Regions=[new(2,2,6,6)]});using(var rendered=new MagickImage(opaque.Single()))using(var pixels=rendered.GetPixels()){var inside=pixels.GetPixel(4,4).ToColor()!;check(inside.A==255&&inside.R==0&&inside.G==0&&inside.B==0,"redaction is opaque over transparent image pixels");check(pixels.GetPixel(0,0).ToColor()!.A==0,"redaction preserves unselected transparency");}
        var fractional=await engine.RunAsync("redactImage",[image],output,new(){Regions=[new(10.5,10.5,2,2)]});using(var rendered=new MagickImage(fractional.Single()))using(var pixels=rendered.GetPixels()){var covered=true;for(var y=10;y<13;y++)for(var x=10;x<13;x++)covered&=pixels.GetPixel(x,y).ToColor()!.R==0;check(covered,"fractional redaction bounds cover complete touched pixels");}
        foreach(var region in new[]{new MediaRegion(-.5,0,10,10),new MediaRegion(0,0,double.NaN,10),new MediaRegion(0,0,double.PositiveInfinity,10),new MediaRegion(310,0,10.5,10)})await mustFail(()=>engine.RunAsync("redactImage",[image],output,new(){Regions=[region]}),"invalid redaction geometry rejected");
        await mustFail(()=>engine.RunAsync("redactImage",[image],output,new(){Regions=regions,RedactionStyle="unknown"}),"unknown redaction style rejected");
        check(SHA256.HashData(await File.ReadAllBytesAsync(image)).SequenceEqual(hash),"multi-area redaction preserves source bytes");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();await mustFail(()=>TextDetection.DetectAsync(image,token:cancel.Token),"text detection cancellation");}
        await mustFail(()=>TextDetection.DetectAsync(image,"-l bad"),"OCR language cannot inject command options");
        if(SystemTools.Find("tesseract") is not null){
            var languages=await TextDetection.LanguagesAsync();check(languages.Length>0&&!languages.Contains("osd"),"OCR offers installed recognition languages");
            if(languages.Contains("eng")){
                var textImage=Path.Combine(root,"private-text.png");using(var source=new MagickImage(MagickColors.White,800,240)){new Drawables().Font("DejaVu-Sans").FontPointSize(72).FillColor(MagickColors.Black).Text(40,130,"PRIVATE DATA").Draw(source);source.Write(textImage);}
                var textHash=SHA256.HashData(await File.ReadAllBytesAsync(textImage));var words=await TextDetection.DetectAsync(textImage,"eng");
                var recognised=words.Any(w=>w.Text.Equals("PRIVATE",StringComparison.OrdinalIgnoreCase))&&words.Any(w=>w.Text.Equals("DATA",StringComparison.OrdinalIgnoreCase));
                if(!recognised){
                    Console.WriteLine("OCR detected: "+string.Join(" | ",words.Select(w=>$"{w.Text} [{w.Region}] confidence={w.Confidence}")));
                    var diagnostic=await ProcessRunner.RunAsync(SystemTools.Find("tesseract")!,[textImage,"stdout","-l","eng","--psm","11","tsv"]);Console.WriteLine("OCR raw TSV: "+diagnostic.Output);Console.WriteLine("OCR stderr: "+diagnostic.Error);
                    if(Environment.GetEnvironmentVariable("NASHI_VERIFY_EVIDENCE_DIRECTORY") is {} evidence){Directory.CreateDirectory(evidence);File.Copy(textImage,Path.Combine(evidence,"ocr-fixture.png"),true);await File.WriteAllTextAsync(Path.Combine(evidence,"ocr-diagnostic.tsv"),diagnostic.Output);}
                }
                check(recognised,"real Tesseract detects fixture words");
                using(var source=new MagickImage(textImage))using(var pixels=source.GetPixels()){
                    var missed=0;for(var y=0;y<240;y++)for(var x=0;x<800;x++)if(pixels.GetPixel(x,y).ToColor()!.R<100&&!words.Any(w=>x>=w.Region.X&&x<w.Region.X+w.Region.Width&&y>=w.Region.Y&&y<w.Region.Y+w.Region.Height))missed++;
                    check(missed==0,"detected padded regions enclose fixture text pixels");
                }
                var hidden=await engine.RunAsync("redactImage",[textImage],output,new(){Regions=words.Select(w=>w.Region).ToArray()});using(var rendered=new MagickImage(hidden.Single()))using(var pixels=rendered.GetPixels()){var covered=words.All(w=>{var c=pixels.GetPixel((int)w.Region.X,(int)w.Region.Y).ToColor()!;return c.R==0&&c.G==0&&c.B==0;});check(covered,"detected OCR words feed real redaction export");}
                check(SHA256.HashData(await File.ReadAllBytesAsync(textImage)).SequenceEqual(textHash),"OCR and redaction keep the source image unchanged");
            }else Console.WriteLine("SKIP English OCR fixture: eng language unavailable");
        }else Console.WriteLine("SKIP OCR integration: Tesseract unavailable");
    }
}
