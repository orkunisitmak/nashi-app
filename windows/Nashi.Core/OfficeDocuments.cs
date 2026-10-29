using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
namespace Nashi.Core;
public sealed record DocumentOptions(int Dpi=144,string Layout="editable",string EpubLayout="reflowable",string Password="");
public sealed partial class OfficeDocuments(string office,string deno,string assets)
{
    private static string Escape(string value)=>WebUtility.HtmlEncode(value);
    private static readonly TimeSpan ConversionTimeout=TimeSpan.FromMinutes(5);
    public const string TimeoutMessage="Office did not finish converting this document within 5 minutes. Try again, or cancel and choose another format.";
    public async Task<string> OfficeAsync(string input,string format,string outputDirectory,CancellationToken token)
    {
        Directory.CreateDirectory(outputDirectory);// LibreOffice creates deeply nested profile/cache paths. Keep its job short
        // even when the destination is an already nested export transaction.
        var job=Path.Combine(Path.GetTempPath(),"NashiOffice-"+Guid.NewGuid().ToString("N"));var profile=Path.Combine(job,"profile");var rendered=Path.Combine(job,"out");Directory.CreateDirectory(Path.Combine(profile,"user"));Directory.CreateDirectory(rendered);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(profile,"user","registrymodifications.xcu"),"<?xml version=\"1.0\"?><oor:items xmlns:oor=\"http://openoffice.org/2001/registry\"><item oor:path=\"/org.openoffice.Office.Common/Security/Scripting\"><prop oor:name=\"MacroSecurityLevel\" oor:op=\"fuse\"><value>3</value></prop></item><item oor:path=\"/org.openoffice.Office.Common/Load\"><prop oor:name=\"UpdateLinkMode\" oor:op=\"fuse\"><value>0</value></prop></item></oor:items>",token);
            var original=Path.GetFullPath(input);var ext=FeatureCatalog.Extension(input);
            if(new[]{"docx","doc","pptx","ppt","xlsx","xls","ods","odt","rtf","pdf","epub"}.Contains(ext)){input=Path.Combine(job,"Source."+ext);File.Copy(original,input);}
            var spreadsheet=new[]{"csv","tsv","xls","xlsx","ods"}.Contains(ext);var slides=new[]{"ppt","pptx"}.Contains(ext);
            var filter=format switch{"docx"=>"docx:Office Open XML Text","pptx"=>"pptx:Impress MS PowerPoint 2007 XML","xlsx"=>"xlsx:Calc MS Excel 2007 XML","pdf"=>"pdf:"+(spreadsheet?"calc_pdf_Export":slides?"impress_pdf_Export":"writer_pdf_Export"),"txt"=>"txt:Text (encoded):UTF8,LF","csv"=>"csv:Text - txt - csv (StarCalc):44,34,76,1,,0,false,true,true,false,false,0","tsv"=>"csv:Text - txt - csv (StarCalc):9,34,76,1,,0,false,true,true,false,false,0","html"=>"html:HTML (StarWriter)","md"=>"md:Markdown","epub"=>"epub:EPUB",_=>format};
            var args=new List<string>{"-env:UserInstallation="+new Uri(profile+Path.DirectorySeparatorChar).AbsoluteUri,"--headless","--nologo","--nodefault","--nolockcheck","--nofirststartwizard","--norestore"};
            if(ext is "csv" or "tsv"){var (delimiter,language)=DelimitedLayout(input,ext);args.Add("--infilter=Text - txt - csv (StarCalc):"+delimiter+",34,76,1,,"+language+",true,true");}else if(ext=="txt")args.Add("--infilter=Text (encoded):UTF8,LF");else if(ext=="md")args.Add("--infilter=Markdown");
            if(ext=="html"&&format is "xlsx" or "ods" or "csv" or "tsv")args.Add("--infilter=HTML (StarCalc)");
            if(ext=="pdf"&&format=="pptx")args.Add("--infilter=impress_pdf_import");
            args.AddRange(["--convert-to",filter,"--outdir",rendered,Path.GetFullPath(input)]);
            // A stuck soffice otherwise keeps Nashi busy until the user cancels; stop it after a generous limit.
            using var limit=CancellationTokenSource.CreateLinkedTokenSource(token);limit.CancelAfter(ConversionTimeout);ProcessResult result;
            try{result=await ProcessRunner.RunAsync(office,args,limit.Token);}
            catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new TimeoutException(TimeoutMessage);}
            var output=Path.Combine(rendered,Path.GetFileNameWithoutExtension(input)+"."+(format=="tsv"?"csv":format));if(!File.Exists(output)||new FileInfo(output).Length==0)throw new IOException("Office could not convert this document to "+format.ToUpperInvariant()+". "+(result.Error+" "+result.Output).Trim());var destination=SafePaths.Unique(outputDirectory,Path.GetFileNameWithoutExtension(original)+"."+(format=="tsv"?"csv":format));File.Move(output,destination);return destination;
        }
        finally{if(Directory.Exists(job))Directory.Delete(job,true);}
    }
    // Excel saves semicolon-separated "CSV" where the comma is the decimal mark (Turkish,
    // German…). Read those with the user's locale so 1,5 stays a number, not text.
    // Semicolons are the delimiter only when every record has the same number of them and
    // the commas don't line up the same way; anything ambiguous stays comma-separated.
    internal static (int Delimiter,int Language) DelimitedLayout(string input,string ext)
    {
        if(ext=="tsv")return(9,1033);
        using var reader=new StreamReader(input,Encoding.UTF8,true);var quoted=false;int commas=0,semicolons=0;var records=new List<(int Commas,int Semicolons)>();
        for(int c,read=0;records.Count<50&&read<1_000_000&&(c=reader.Read())>=0;read++)
        {
            if(c=='"')quoted=!quoted;else if(quoted)continue;else if(c==',')commas++;else if(c==';')semicolons++;
            else if(c is '\n' or '\r'){if(commas+semicolons>0)records.Add((commas,semicolons));commas=semicolons=0;}
        }
        if(commas+semicolons>0&&reader.Peek()<0)records.Add((commas,semicolons));
        bool Aligned(Func<(int Commas,int Semicolons),int> count)=>records.Count>0&&count(records[0])>0&&records.All(r=>count(r)==count(records[0]));
        var culture=System.Globalization.CultureInfo.CurrentCulture;var commaDecimal=culture.NumberFormat.NumberDecimalSeparator==","&&culture.LCID is not (127 or 4096);
        return Aligned(r=>r.Semicolons)&&!Aligned(r=>r.Commas)?(59,commaDecimal?culture.LCID:1033):(44,1033);
    }
    public async Task<string> PagesReadAsync(string input,string work,CancellationToken token)
    {
        if(new FileInfo(input).Length>200_000_000)throw new InvalidDataException("Pages input exceeds 200 MB.");var json=Path.Combine(work,"pages.json");await PagesCall("read",input,json,token);using var model=JsonDocument.Parse(await File.ReadAllTextAsync(json,token));var html=Path.Combine(work,"Pages document.html");await File.WriteAllTextAsync(html,ModelHtml(model.RootElement),token);return html;
    }
    public async Task<string> PagesWriteAsync(string docx,string output,CancellationToken token)
    {
        var json=output+".json";try{await File.WriteAllTextAsync(json,JsonSerializer.Serialize(ReadDocx(docx)),token);await PagesCall("write",json,output,token);return output;}finally{if(File.Exists(json))File.Delete(json);}
    }
    private Task<ProcessResult> PagesCall(string mode,string input,string output,CancellationToken token)
    {
        var bundle=Path.Combine(assets,"PagesInterop.js");var script=Path.Combine(assets,"pages-windows.js");return ProcessRunner.RunAsync(deno,["run","--no-prompt","--no-config","--cached-only","--allow-read="+bundle.Replace(",",",,")+","+Path.GetFullPath(input).Replace(",",",,"),"--allow-write="+Path.GetFullPath(output).Replace(",",",,"),script,bundle,mode,Path.GetFullPath(input),Path.GetFullPath(output)],token);
    }
    private static XDocument Xml(ZipArchive zip,string entry)
    {
        var resource=zip.GetEntry(entry)??throw new InvalidDataException("The document is missing "+entry);if(resource.Length>20_000_000)throw new InvalidDataException("Document XML is too large.");using var stream=resource.Open();using var reader=XmlReader.Create(stream,new(){DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20_000_000});return XDocument.Load(reader);
    }
    public static string PdfText(string input,string password="")
    {
        using var pdf=UglyToad.PdfPig.PdfDocument.Open(input,new(){Password=password});return string.Join("\n\n",pdf.GetPages().Select(p=>ContentOrderTextExtractor.GetText(p)));
    }
    // XmlReader picks the encoding like this (XML 1.0 appendix F); reading the text first has to
    // as well: a byte order mark, UTF-32 or UTF-16 without one ("<" with NUL bytes), then the
    // declaration (EPUB 2 chapters may declare ISO-8859-1 or windows-1252), else UTF-8. Bytes that
    // aren't valid in that encoding are an error there too, not U+FFFD.
    public static string XmlText(byte[] data)
    {
        var bytes=data.AsSpan();Encoding encoding=new UTF8Encoding(false,true);var bom=0;
        if(bytes.StartsWith(new byte[]{0xef,0xbb,0xbf}))bom=3;
        else if(bytes.StartsWith(new byte[]{0xff,0xfe,0,0})){encoding=new UTF32Encoding(false,false,true);bom=4;}
        else if(bytes.StartsWith(new byte[]{0,0,0xfe,0xff})){encoding=new UTF32Encoding(true,false,true);bom=4;}
        else if(bytes.StartsWith(new byte[]{0xff,0xfe})){encoding=new UnicodeEncoding(false,false,true);bom=2;}
        else if(bytes.StartsWith(new byte[]{0xfe,0xff})){encoding=new UnicodeEncoding(true,false,true);bom=2;}
        else if(bytes.StartsWith(new byte[]{0x3c,0,0,0}))encoding=new UTF32Encoding(false,false,true);
        else if(bytes.StartsWith(new byte[]{0,0,0,0x3c}))encoding=new UTF32Encoding(true,false,true);
        else if(bytes.StartsWith(new byte[]{0x3c,0}))encoding=new UnicodeEncoding(false,false,true);
        else if(bytes.StartsWith(new byte[]{0,0x3c}))encoding=new UnicodeEncoding(true,false,true);
        else if(Regex.Match(Encoding.Latin1.GetString(data,0,Array.IndexOf(data,(byte)'>')+1),"^<\\?xml[^>]*?\\sencoding\\s*=\\s*[\"']([A-Za-z][A-Za-z0-9._-]*)[\"']") is {Success:true} declared)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try{if(Encoding.GetEncoding(declared.Groups[1].Value,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback) is not (UnicodeEncoding or UTF32Encoding) and var named)encoding=named;}
            catch(ArgumentException){throw new XmlException("System does not support '"+declared.Groups[1].Value+"' encoding.");}
        }
        try{return encoding.GetString(data,bom,data.Length-bom);}
        catch(DecoderFallbackException error){throw new XmlException("Invalid character in the given encoding.",error);}
    }
    /// Replaces HTML named entities (&amp;nbsp; &amp;mdash;) with character references so XHTML
    /// parses without its DTD. The five XML entities and unknown names stay as written, and so do
    /// CDATA sections, comments and processing instructions, which hold literal text.
    public static string NumericEntities(string xml)=>Regex.Replace(xml,"<!\\[CDATA\\[[\\s\\S]*?\\]\\]>|<!--[\\s\\S]*?-->|<\\?[\\s\\S]*?\\?>|&([A-Za-z][A-Za-z0-9]{1,31});",m=>!m.Groups[1].Success||m.Groups[1].Value is "amp" or "lt" or "gt" or "quot" or "apos"||WebUtility.HtmlDecode(m.Value) is var decoded&&decoded==m.Value?m.Value:string.Concat(decoded.EnumerateRunes().Select(r=>"&#"+r.Value+";")));
    /// A region of a PDF page in points, measured from the page's top-left corner.
    public readonly record struct PdfArea(double Left,double Top,double Width,double Height);
    public static string PdfHtml(string input,string password="")=>PdfHtml(input,password,null,out _);
    // Pages with no extractable words or images (CCITT/JBIG2/JPX scans, vector-only pages) would
    // convert to blank pages, and images PdfPig cannot decode would vanish from pages with text.
    // Those pages are reported; pageImage then supplies a rendered PNG of the whole page (area null)
    // or of one image's area. Areas are only asked for where the page maps directly onto a render:
    // no rotation, and the crop box is the media box at the origin.
    public static string PdfHtml(string input,string password,Func<int,PdfArea?,byte[]?>? pageImage,out int[] pagesToRender)
    {
        using var pdf=UglyToad.PdfPig.PdfDocument.Open(input,new(){Password=password});var html=new StringBuilder("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/></head><body>");var render=new List<int>();
        foreach(var page in pdf.GetPages())
        {
            var blocks=new List<(double Top,string Html)>();var needed=false;var media=page.MediaBox.Bounds;var crop=page.CropBox.Bounds;
            var mappable=page.Rotation.Value==0&&media.Left==0&&media.Bottom==0&&crop.Left==media.Left&&crop.Bottom==media.Bottom&&crop.Width==media.Width&&crop.Height==media.Height;
            foreach(var line in page.GetWords().GroupBy(w=>Math.Round(w.BoundingBox.Bottom/4)).OrderByDescending(g=>g.Key))
            {
                var text=new StringBuilder("<p style=\"margin:0 0 5pt\">");var first=true;foreach(var word in line.OrderBy(w=>w.BoundingBox.Left))
                {
                    var letter=word.Letters.FirstOrDefault();var font=letter?.FontName??"Arial";var size=Math.Clamp(letter?.FontSize??12,1,300);var style="font-size:"+size.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+"pt;"+(font.Contains("Bold",StringComparison.OrdinalIgnoreCase)?"font-weight:bold;":"")+(font.Contains("Italic",StringComparison.OrdinalIgnoreCase)||font.Contains("Oblique",StringComparison.OrdinalIgnoreCase)?"font-style:italic;":"");if(!first)text.Append(' ');first=false;text.Append("<span style=\"").Append(style).Append("\">").Append(Escape(word.Text)).Append("</span>");
                }blocks.Add((page.Height-line.Max(w=>w.BoundingBox.Top),text.Append("</p>").ToString()));
            }
            foreach(var image in page.GetImages())
            {
                byte[]? data=null;var mime="image/png";if(image.TryGetPng(out var png))data=png;else if(image.RawBytes.Length>=2&&image.RawBytes[0]==0xff&&image.RawBytes[1]==0xd8){data=image.RawBytes.ToArray();mime="image/jpeg";}if(data?.Length>20_000_000)continue;
                // A render is bounded by the page and DPI, like the whole-page renders below.
                if(data is null&&mappable){needed=true;data=pageImage?.Invoke(page.Number,new(image.BoundingBox.Left,page.Height-image.BoundingBox.Top,image.BoundingBox.Width,image.BoundingBox.Height));}if(data is null)continue;
                blocks.Add((page.Height-image.BoundingBox.Top,"<p><img style=\"max-width:100%;width:"+image.BoundingBox.Width.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+"pt;height:auto\" src=\"data:"+mime+";base64,"+Convert.ToBase64String(data)+"\"/></p>"));
            }
            if(blocks.Count==0){needed=true;if(pageImage?.Invoke(page.Number,null) is {} rendered)blocks.Add((0,"<p><img style=\"max-width:100%;width:"+page.Width.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+"pt;height:auto\" src=\"data:image/png;base64,"+Convert.ToBase64String(rendered)+"\"/></p>"));}
            if(needed)render.Add(page.Number);
            html.Append("<section style=\"page-break-after:always\">");foreach(var block in blocks.OrderBy(b=>b.Top))html.Append(block.Html);html.Append("</section>");
        }
        pagesToRender=render.ToArray();return html.Append("</body></html>").ToString();
    }
    public static string SanitizedHtml(string html)
    {
        html=Regex.Replace(html,@"<(script|iframe|object|embed)\b[^>]*>[\s\S]*?</\1\s*>","",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2));
        html=Regex.Replace(html,@"<link\b[^>]*>|<base\b[^>]*>","",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2));
        html=Regex.Replace(html,"\\s(?:src|background)\\s*=\\s*([\"'])(?!data:)[^\"']*\\1","",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2));
        return Regex.Replace(html,@"url\s*\([^)]*\)","none",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2));
    }
}
