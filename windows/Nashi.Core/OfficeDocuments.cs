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
            if(ext is "csv" or "tsv")args.Add(DelimitedInputFilter(input,ext));else if(ext=="txt")args.Add("--infilter=Text (encoded):UTF8,LF");else if(ext=="md")args.Add("--infilter=Markdown");
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
    internal static string DelimitedInputFilter(string input,string extension)
    {
        var (delimiter,language)=DelimitedLayout(input,extension);
        // LibreOffice evaluates formulas when CSV import option 13 is omitted.
        return "--infilter=Text - txt - csv (StarCalc):"+delimiter+",34,76,1,,"+language+",true,true,,,,,false";
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
        var resource=zip.GetEntry(entry)??throw new InvalidDataException("The document is missing "+entry);if(resource.Length>20_000_000)throw new InvalidDataException("Document XML is too large.");var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20_000_000};
        using(var scan=resource.Open())using(var depth=XmlReader.Create(scan,settings))while(depth.Read())if(depth.Depth>256)throw new InvalidDataException("This document nests its content too deeply to convert.");
        using var stream=resource.Open();using var reader=XmlReader.Create(stream,settings);return XDocument.Load(reader);
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
                    var letter=word.Letters.FirstOrDefault();var font=letter?.FontName??"Arial";var drawn=letter?.PointSize??12;var size=Math.Clamp(double.IsFinite(drawn)?drawn:12,1,300);var style="font-size:"+size.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+"pt;"+(font.Contains("Bold",StringComparison.OrdinalIgnoreCase)?"font-weight:bold;":"")+(font.Contains("Italic",StringComparison.OrdinalIgnoreCase)||font.Contains("Oblique",StringComparison.OrdinalIgnoreCase)?"font-style:italic;":"");if(!first)text.Append(' ');first=false;text.Append("<span style=\"").Append(style).Append("\">").Append(Escape(word.Text)).Append("</span>");
                }blocks.Add((page.Height-line.Max(w=>w.BoundingBox.Top),text.Append("</p>").ToString()));
            }
            foreach(var image in page.GetImages())
            {
                // PdfPig measures a picture along its own sides and keeps its pixels unturned. A turned or mirrored picture covers the
                // page area its corners span; where that area maps onto a render, the render shows it the way the page does.
                var box=image.BoundingBox;UglyToad.PdfPig.Core.PdfPoint[] corners=[box.BottomLeft,box.BottomRight,box.TopLeft,box.TopRight];
                double left=corners.Min(c=>c.X),right=corners.Max(c=>c.X),bottom=corners.Min(c=>c.Y),top=corners.Max(c=>c.Y);
                // Only the part on the page is rendered, so a render holds nothing beside the picture and keeps that part's size.
                double visibleLeft=Math.Max(left,0),visibleTop=Math.Max(page.Height-top,0);var area=new PdfArea(visibleLeft,visibleTop,Math.Min(right,page.Width)-visibleLeft,Math.Min(page.Height-bottom,page.Height)-visibleTop);
                // A picture with no part on the page is not shown, so it is left out. PdfPig places pictures on the page as shown
                // (from the crop box's corner, turned with the page), so this holds on any page.
                if(!(area.Width>0&&area.Height>0))continue;
                var upright=Math.Abs(box.BottomLeft.Y-box.BottomRight.Y)<.01&&Math.Abs(box.BottomLeft.X-box.TopLeft.X)<.01&&box.BottomRight.X>box.BottomLeft.X&&box.TopLeft.Y>box.BottomLeft.Y;
                byte[]? data=null;var mime="image/png";var (width,height)=(area.Width,area.Height);if(!upright&&mappable){needed=true;data=pageImage?.Invoke(page.Number,area);}
                if(data is null)
                {
                    if(image.TryGetPng(out var png))data=png;else if(image.RawBytes.Length>=2&&image.RawBytes[0]==0xff&&image.RawBytes[1]==0xd8){data=image.RawBytes.ToArray();mime="image/jpeg";}if(data?.Length>20_000_000)continue;
                    // Without a render a turned picture keeps its own proportions, unturned, rather than being stretched over the area.
                    if(data is not null)(width,height)=(box.Width,box.Height);
                    // A render is bounded by the page and DPI, like the whole-page renders below.
                    else if(upright&&mappable){needed=true;data=pageImage?.Invoke(page.Number,area);}if(data is null)continue;
                }
                blocks.Add((page.Height-top,"<p><img style=\""+PictureStyle(width,height)+"\" src=\"data:"+mime+";base64,"+Convert.ToBase64String(data)+"\"/></p>"));
            }
            // A rotated page renders turned, so the render's own proportions give the height.
            if(blocks.Count==0){needed=true;if(pageImage?.Invoke(page.Number,null) is {} rendered)blocks.Add((0,"<p><img style=\""+PictureStyle(page.Width,PngSize(rendered) is {} size?page.Width*size.Height/size.Width:page.Height)+"\" src=\"data:image/png;base64,"+Convert.ToBase64String(rendered)+"\"/></p>"));}
            if(needed)render.Add(page.Number);
            html.Append("<section style=\"page-break-after:always\">");foreach(var block in blocks.OrderBy(b=>b.Top))html.Append(block.Html);html.Append("</section>");
        }
        pagesToRender=render.ToArray();return html.Append("</body></html>").ToString();
    }
    // LibreOffice's HTML import takes a picture's height from its pixels at 96 DPI whatever height:auto says, and ignores
    // max-width and @page: it lays the text out on A4 or Letter with 2 cm and 1 cm side margins. A picture therefore gets
    // both sizes, as the PDF shows it, scaled down in proportion to fit a 510 × 735 pt column.
    public static string PictureStyle(double width,double height)
    {
        if(!(double.IsFinite(width)&&double.IsFinite(height)&&width>0&&height>0))return "max-width:100%;height:auto";
        var scale=Math.Min(1,Math.Min(510/width,735/height));return "width:"+CssNumber(width*scale)+"pt;height:"+CssNumber(height*scale)+"pt";
    }
    /// The pixel size from a PNG's header, or null for anything else.
    public static (int Width,int Height)? PngSize(byte[] png)
    {
        if(png.Length<24||!png.AsSpan(0,8).SequenceEqual(new byte[]{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A}))return null;
        var width=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));var height=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));return width>0&&height>0?(width,height):null;
    }
    public static string SanitizedHtml(string html)
    {
        html=Regex.Replace(html,@"<(script|iframe|object|embed)\b[^>]*>[\s\S]*?</\1\s*>","",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2));
        html=Regex.Replace(html,@"<link\b[^>]*>|<base\b[^>]*>","",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2));
        html=Regex.Replace(html,"""\s(?:src|background)\s*=\s*(?:"(?!data:)[^"]*"|'(?!data:)[^']*'|(?!data:)[^\s"'<>`]+)""","",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2));
        return Regex.Replace(html,@"url\s*\([^)]*\)","none",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2));
    }
}
