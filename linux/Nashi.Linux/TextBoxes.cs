using System.Globalization;
using Nashi.Core;
namespace Nashi.Linux;

/// Word boxes from Tesseract's TSV output, in the pixels of the image that was read.
public static class TextBoxes
{
    public sealed record Box(int X,int Y,int Width,int Height,string Word);
    public static IReadOnlyList<Box> Find(byte[] image)
    {
        var tesseract=Operations.Executable("tesseract")??throw new InvalidOperationException("Install tesseract to find text.");
        // A private (0700) temporary folder: the picture may show exactly what is about to be redacted.
        var folder=Directory.CreateTempSubdirectory("nashi-text-").FullName;var file=Path.Combine(folder,"picture.png");
        try
        {
            File.WriteAllBytes(file,image);
            var installed=PdfDocumentMedia.LanguagesFor(tesseract,CancellationToken.None).GetAwaiter().GetResult();
            var args=new List<string>{file,"stdout"};if(installed.Length>0){args.Add("-l");args.Add(string.Join('+',installed));}args.Add("tsv");
            return Parse(ProcessRunner.RunAsync(tesseract,args).GetAwaiter().GetResult().Output);
        }
        finally{try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    /// Level 5 rows are words; a little padding covers ascenders and descenders.
    public static IReadOnlyList<Box> Parse(string tsv)
    {
        var boxes=new List<Box>();
        foreach(var line in tsv.Split('\n').Skip(1))
        {
            var f=line.Split('\t');if(f.Length<12||f[0]!="5"||string.IsNullOrWhiteSpace(f[11]))continue;
            if(!double.TryParse(f[10],NumberStyles.Float,CultureInfo.InvariantCulture,out var confidence)||confidence<30)continue;
            int I(int n)=>int.Parse(f[n],CultureInfo.InvariantCulture);var pad=Math.Max(2,I(9)/8);
            boxes.Add(new(Math.Max(0,I(6)-pad),Math.Max(0,I(7)-pad),I(8)+pad*2,I(9)+pad*2,f[11].Trim()));
        }
        return boxes;
    }
}
