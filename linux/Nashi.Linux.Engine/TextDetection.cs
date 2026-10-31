using System.Globalization;
using System.Text.RegularExpressions;
using ImageMagick;
using Nashi.Core;
namespace Nashi.Windows;

public sealed record DetectedText(string Text,MediaRegion Region,double Confidence);

// Tesseract's documented TSV word boxes use top-left source-image pixels:
// https://tesseract-ocr.github.io/tessdoc/Command-Line-Usage.html#tsv-output
public static class TextDetection
{
    public const int MaximumWords=10000;
    private static readonly Regex Language=new(@"^[A-Za-z0-9_]+(?:/[A-Za-z0-9_]+)*(?:\+[A-Za-z0-9_]+(?:/[A-Za-z0-9_]+)*)*$",RegexOptions.CultureInvariant);
    public static async Task<string[]> LanguagesAsync(CancellationToken token=default)
    {
        var result=await ProcessRunner.RunAsync(Executable(),["--list-langs"],token);
        var languages=result.Output.Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Where(l=>l!="osd"&&!l.Contains('+')&&Language.IsMatch(l)).Distinct(StringComparer.Ordinal).OrderBy(l=>l=="eng"?0:1).ThenBy(l=>l,StringComparer.Ordinal).ToArray();
        if(languages.Length==0)throw new InvalidOperationException("Install a Tesseract OCR language to detect text.");return languages;
    }
    private static string Executable()=>SystemTools.Find("tesseract")??throw new FileNotFoundException("Install Tesseract and an OCR language to detect text.");
    public static async Task<IReadOnlyList<DetectedText>> DetectAsync(string input,string? language=null,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();if(language is not null&&!Language.IsMatch(language))throw new ArgumentException("Choose an installed OCR language.",nameof(language));
        var installed=await LanguagesAsync(token);language??=installed[0];if(language.Split('+').Any(l=>!installed.Contains(l,StringComparer.Ordinal)))throw new ArgumentException("The selected OCR language is not installed.",nameof(language));
        var work=Path.Combine(Path.GetTempPath(),"NashiTextDetection-"+Guid.NewGuid().ToString("N"));if(OperatingSystem.IsWindows())Directory.CreateDirectory(work);else Directory.CreateDirectory(work,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        try{
            var file=Path.Combine(work,"source.png");
            var dimensions=await Task.Run(()=>{token.ThrowIfCancellationRequested();using var image=PhotoCodec.Read(input);if((long)image.Width*image.Height>100_000_000)throw new InvalidDataException("Detect text in images up to 100 MP. Crop or resize the image first.");image.BackgroundColor=MagickColors.White;image.Alpha(AlphaAction.Remove);image.Write(file,MagickFormat.Png);return(Width:checked((int)image.Width),Height:checked((int)image.Height));},token);
            var reader=new TsvReader(dimensions.Width,dimensions.Height);
            await ProcessRunner.RunAsync(Executable(),[file,"stdout","-l",language,"--psm","11","tsv"],token,reader.Accept);
            token.ThrowIfCancellationRequested();return reader.Finish();
        }finally{Directory.Delete(work,true);}
    }
    public static IReadOnlyList<DetectedText> ParseTsv(string tsv,int width,int height)
    {
        if(width<1||height<1)throw new ArgumentOutOfRangeException(nameof(width));
        var reader=new TsvReader(width,height);using var lines=new StringReader(tsv);while(lines.ReadLine() is {} line)reader.Accept(line);return reader.Finish();
    }
    private sealed class TsvReader(int width,int height)
    {
        private readonly List<DetectedText> words=[];
        private bool header,overflow;
        internal void Accept(string line)
        {
            // This callback must keep draining stdout even if a limit is hit;
            // throwing here could leave the child blocked on its output pipe.
            if(line.StartsWith("level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext",StringComparison.Ordinal)){header=true;return;}
            if(!header||overflow)return;
            var fields=line.Split('\t',12);if(fields.Length!=12||fields[0]!="5"||fields[1]!="1"||string.IsNullOrWhiteSpace(fields[11]))return;
            if(!int.TryParse(fields[6],NumberStyles.Integer,CultureInfo.InvariantCulture,out var x)||!int.TryParse(fields[7],NumberStyles.Integer,CultureInfo.InvariantCulture,out var y)||!int.TryParse(fields[8],NumberStyles.Integer,CultureInfo.InvariantCulture,out var w)||!int.TryParse(fields[9],NumberStyles.Integer,CultureInfo.InvariantCulture,out var h)||w<=0||h<=0||!double.TryParse(fields[10],NumberStyles.Float,CultureInfo.InvariantCulture,out var confidence)||!double.IsFinite(confidence)||confidence<0||confidence>100)return;
            // Include antialiased edge pixels, while keeping every box inside
            // the oriented image used by both the editor and export engine.
            var left=Math.Clamp((long)x-2,0,width);var top=Math.Clamp((long)y-2,0,height);var right=Math.Clamp((long)x+w+2,0,width);var bottom=Math.Clamp((long)y+h+2,0,height);if(right<=left||bottom<=top)return;
            if(words.Count==MaximumWords){overflow=true;return;}
            words.Add(new(fields[11].Trim(),new(left,top,right-left,bottom-top),confidence));
        }
        internal IReadOnlyList<DetectedText> Finish()
        {
            if(width<1||height<1)throw new ArgumentOutOfRangeException(nameof(width));
            if(!header)throw new InvalidDataException("Tesseract did not return word coordinates. Ensure its TSV configuration is installed.");
            if(overflow)throw new InvalidDataException("The image contains too many text areas. Crop or split it before detecting text.");return words;
        }
    }
}
