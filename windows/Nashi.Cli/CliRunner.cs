using Nashi.Core;
using System.Text.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Nashi.Cli;

/// <summary>
/// Small, read-only Windows CLI entry point. Conversion and tool commands stay
/// out of scope until the cross-platform contract is agreed in issue #21.
/// </summary>
public static class CliRunner
{
    private const string Usage = "Usage: nashi-cli catalog\n       nashi-cli inspect FILE [--json]\n       nashi-cli --help\n\nConversion and tool execution are not implemented in this first Windows slice.";

    public static int Run(string[] args,TextWriter output,TextWriter error)
    {
        var machineError=args.Contains("--json",StringComparer.Ordinal);
        var commands=args.Where(arg=>arg!="--json").ToArray();
        try
        {
            if(commands.Length==1 && commands[0] is "--help" or "-h" or "help")
            {
                output.WriteLine(Usage);
                return 0;
            }
            if(commands.Length==1 && commands[0]=="catalog")
            {
                var response=new
                {
                    conversionMatrix=FeatureCatalog.Conversions,
                    tools=FeatureCatalog.ToolNames.Keys.OrderBy(x=>x,StringComparer.Ordinal).ToArray()
                };
                output.WriteLine(JsonSerializer.Serialize(response));
                return 0;
            }
            if(commands.Length==2 && commands[0]=="inspect")
            {
                var fullPath=Path.GetFullPath(commands[1]);
                if(!File.Exists(fullPath))throw new FileNotFoundException("Input file does not exist.",fullPath);
                output.WriteLine(JsonSerializer.Serialize(Inspect(fullPath)));
                return 0;
            }
            throw new ArgumentException(Usage);
        }
        catch(Exception ex)
        {
            var code=ex is FileNotFoundException?"not_found":ex is ArgumentException?"usage":"read_failed";
            if(machineError)
                error.WriteLine(JsonSerializer.Serialize(new {error=ex.Message,code}));
            else
                error.WriteLine(ex.Message);
            return ex is ArgumentException?64:ex is FileNotFoundException?2:1;
        }
    }

    private static Dictionary<string,object?> Inspect(string file)
    {
        var category=Category(file);
        var result=new Dictionary<string,object?> {["category"]=category};
        if(category=="image")
        {
            using var image=PhotoCodec.Read(file);
            result["width"]=image.Width;
            result["height"]=image.Height;
        }
        else if(category=="pdf")
        {
            using var pdf=PdfDocument.Open(file);
            result["pages"]=pdf.NumberOfPages;
            result["text"]=string.Join("\n\n",pdf.GetPages().Select(p=>ContentOrderTextExtractor.GetText(p)));
        }
        return result;
    }

    private static string Category(string file)
    {
        var ext=FeatureCatalog.Extension(file);
        if(FeatureCatalog.IsImage(file))return "image";
        if(FeatureCatalog.IsVideo(file))return "video";
        if(FeatureCatalog.IsAudio(file))return "audio";
        return ext switch
        {
            "pdf"=>"pdf",
            "doc" or "docx" or "pages" or "md" or "txt" or "rtf" or "rtfd" or "textclipping" or "html" or "htm" or "odt"=>"document",
            "ppt" or "pptx" or "key"=>"presentation",
            "xlsx" or "xls" or "ods" or "csv" or "tsv" or "numbers"=>"spreadsheet",
            "epub"=>"ebook",
            "srt" or "vtt"=>"subtitle",
            "zip" or "tar" or "gz" or "rar"=>"archive",
            _=>"other"
        };
    }
}
