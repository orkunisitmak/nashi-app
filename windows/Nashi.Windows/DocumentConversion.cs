using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Xml.Linq;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;

public sealed class DocumentConversion(OfficeRuntime runtime,Toolchain tools)
{
    public async Task<IReadOnlyList<string>> ConvertAsync(IReadOnlyList<string> inputs,string format,string destination,DocumentOptions options,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();
        for(var i=0;i<inputs.Count;i++)
        {
            token.ThrowIfCancellationRequested();var input=inputs[i];var ext=FeatureCatalog.Extension(input);var work=Path.Combine(tx.StagingDirectory,"work-"+i);Directory.CreateDirectory(work);string source=input;
            if(!FeatureCatalog.Formats([input]).Contains(format))throw new ArgumentException("This conversion is not available for "+Path.GetFileName(input)+".");
            progress?.Report(new(null,"Preparing "+Path.GetFileName(input)+"…"));
            if(FeatureCatalog.IsArchive(input)){outputs.AddRange(ArchiveEngine.Convert(input,format,tx.StagingDirectory,options.Password,progress,token));continue;}
            if(ext is "srt" or "vtt")
            {var path=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+"."+format);await File.WriteAllTextAsync(path,SubtitleCodec.Convert(LegacyTextCodec.Decode(await File.ReadAllBytesAsync(input,token)),format),new UTF8Encoding(false),token);outputs.Add(path);continue;}
            var engine=runtime.Executable is {} office?new OfficeDocuments(office,tools.Ready?tools.Executable("deno"):"",AppContext.BaseDirectory):null;
            if(ext=="pages"){if(engine is null||!tools.Ready)throw new InvalidOperationException("Set up office and media tools once to enable native Pages interoperability.");source=await engine.PagesReadAsync(input,work,token);}
            if(ext=="rtfd"){source=await RichTextAttachments.Materialize(input,work,token);}
            if(ext=="textclipping")source=await MaterializeClipping(input,work,token);
            if(ext is "txt" or "md" or "csv" or "tsv"){var normalized=Path.Combine(work,Path.GetFileName(input));await LegacyTextCodec.ToUtf8Async(input,normalized,token);source=normalized;}
            if(ext=="epub")source=EpubToHtml(input,work,token);
            if(ext=="pdf"&&format is "txt" or "md")
            {var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+"."+format);await File.WriteAllTextAsync(output,await PdfDocumentMedia.Text(input,options.Password,token),new UTF8Encoding(false),token);outputs.Add(output);continue;}
            if(ext=="pdf"&&format=="pptx")source=await PdfDocumentMedia.Slides(input,work,options.Dpi,options.Password,token);
            if(ext=="pdf"&&format=="epub"&&options.EpubLayout=="fixed")
            {var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+".epub");EpubFixed(await NativePdf.RenderAsync(input,options.Dpi,token,options.Password),output,Path.GetFileNameWithoutExtension(input));outputs.Add(output);continue;}
            if(ext=="pdf"&&format is not ("png" or "jpg" or "pptx"))
            {
                source=Path.Combine(work,format=="xlsx"?"Tables.html":"PDF document.html");var html=options.Layout=="preserve"?await PdfPreservedHtml(input,work,options.Dpi,token,options.Password):format=="xlsx"?PdfTables(input,options.Password):await EditablePdfHtml(input,options,token);await File.WriteAllTextAsync(source,html,token);
            }
            if(format is "png" or "jpg")
            {
                var pdf=ext=="pdf"?input:await Require(engine).OfficeAsync(source,"pdf",work,token);var pages=await NativePdf.RenderAsync(pdf,options.Dpi,token,ext=="pdf"?options.Password:"");
                for(var page=0;page<pages.Length;page++){token.ThrowIfCancellationRequested();var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+$" Page {page+1}."+format);var bytes=pages[page];if(format=="png")await File.WriteAllBytesAsync(output,bytes,token);else PhotoCodec.EncodePng(bytes,output,"jpg",90);outputs.Add(output);progress?.Report(new((i+(page+1d)/pages.Length)/inputs.Count*.98,"Rendering page "+(page+1)));}continue;
            }
            if(format=="pages")
            {
                if(!tools.Ready)throw new InvalidOperationException("Set up media tools once to enable the Pages runtime.");var docx=ext=="docx"?input:await Require(engine).OfficeAsync(source,"docx",work,token);var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+".pages");outputs.Add(await Require(engine).PagesWriteAsync(docx,output,token));continue;
            }
            if(format=="epub"&&options.EpubLayout=="fixed")
            {var pdf=await Require(engine).OfficeAsync(source,"pdf",work,token);var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+".epub");EpubFixed(await NativePdf.RenderAsync(pdf,options.Dpi,token,ext=="pdf"?options.Password:""),output,Path.GetFileNameWithoutExtension(input));outputs.Add(output);continue;}
            if(FeatureCatalog.Extension(source)=="html"){var safe=Path.Combine(work,"Safe.html");await File.WriteAllTextAsync(safe,OfficeDocuments.SanitizedHtml(await File.ReadAllTextAsync(source,token)),token);source=safe;}
            var converted=await Require(engine).OfficeAsync(source,format,work,token);var target=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+"."+format);File.Move(converted,target);outputs.Add(target);progress?.Report(new((i+1d)/inputs.Count*.98,"Saving document…"));
        }
        return tx.Publish(outputs,token);
    }
    private static OfficeDocuments Require(OfficeDocuments? engine)=>engine??throw new InvalidOperationException("Choose Set up office tools once to enable document, slide and spreadsheet conversions.");
    // Scanned pages and images PdfPig cannot decode come from a PDFium render of just those pages:
    // the whole page when nothing else was found, otherwise only the image's area.
    private static async Task<string> EditablePdfHtml(string input,DocumentOptions options,CancellationToken token)
    {
        var html=OfficeDocuments.PdfHtml(input,options.Password,null,out var needed);if(needed.Length==0)return html;
        var pages=await NativePdf.RenderPagesAsync(input,needed,options.Dpi,token,options.Password);
        return OfficeDocuments.PdfHtml(input,options.Password,(n,area)=>pages.TryGetValue(n,out var png)?area is {} a?CropRendered(png,a,options.Dpi):png:null,out _);
    }
    private static byte[]? CropRendered(byte[] png,OfficeDocuments.PdfArea area,int dpi)
    {
        var page=ImageEngine.FromPng(png);var scale=dpi/72d;var x=Math.Clamp((int)Math.Floor(area.Left*scale),0,page.PixelWidth);var y=Math.Clamp((int)Math.Floor(area.Top*scale),0,page.PixelHeight);
        var width=Math.Min(page.PixelWidth-x,(int)Math.Ceiling(area.Width*scale));var height=Math.Min(page.PixelHeight-y,(int)Math.Ceiling(area.Height*scale));
        return width<1||height<1?null:ImageEngine.PngBytes(new CroppedBitmap(page,new System.Windows.Int32Rect(x,y,width,height)));
    }
    private static async Task<string> PdfPreservedHtml(string input,string work,int dpi,CancellationToken token,string password="")
    {
        var pages=await NativePdf.RenderAsync(input,dpi,token,password);return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/></head><body style=\"margin:0\">"+string.Join("",pages.Select(p=>"<p style=\"page-break-after:always;margin:0\"><img style=\"width:100%;height:auto\" src=\"data:image/png;base64,"+Convert.ToBase64String(p)+"\"/></p>"))+"</body></html>";
    }
    private static string PdfTables(string input,string password)
    {
        using var pdf=UglyToad.PdfPig.PdfDocument.Open(input,new(){Password=password});var html=new StringBuilder("<html><head><meta charset=\"utf-8\"/></head><body>");
        foreach(var page in pdf.GetPages())
        {
            html.Append("<table>");foreach(var line in page.GetWords().OrderByDescending(w=>w.BoundingBox.Bottom).GroupBy(w=>Math.Round(w.BoundingBox.Bottom/4)).OrderByDescending(g=>g.Key))
            {html.Append("<tr>");var ordered=line.OrderBy(w=>w.BoundingBox.Left).ToArray();var text=new StringBuilder();double right=double.NaN;foreach(var word in ordered){if(double.IsFinite(right)&&word.BoundingBox.Left-right>12){html.Append("<td>").Append(WebUtility.HtmlEncode(text.ToString())).Append("</td>");text.Clear();}if(text.Length>0)text.Append(' ');text.Append(word.Text);right=word.BoundingBox.Right;}html.Append("<td>").Append(WebUtility.HtmlEncode(text.ToString())).Append("</td></tr>");}html.Append("</table>");
        }return html.Append("</body></html>").ToString();
    }
    private static string EpubToHtml(string input,string work,CancellationToken token)
    {
        var extracted=ArchiveEngine.Extract(input,work,null,null,token)[0];var container=ReadXml(Path.Combine(extracted,"META-INF","container.xml"));var file=container.Descendants().First(e=>e.Name.LocalName=="rootfile").Attribute("full-path")!.Value;var packagePath=SafePaths.ArchiveEntry(extracted,file);var package=ReadXml(packagePath);var baseFolder=Path.GetDirectoryName(packagePath)!;
        var items=package.Descendants().Where(e=>e.Name.LocalName=="item").ToDictionary(e=>e.Attribute("id")!.Value,e=>e.Attribute("href")!.Value);var html=new StringBuilder("<html><head><meta charset=\"utf-8\"/></head><body>");
        foreach(var reference in package.Descendants().Where(e=>e.Name.LocalName=="itemref"))
        {
            token.ThrowIfCancellationRequested();var href=items[reference.Attribute("idref")!.Value];var path=Path.GetFullPath(Path.Combine(baseFolder,Uri.UnescapeDataString(href.Split('#')[0])));if(!path.StartsWith(extracted+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid EPUB chapter path.");var chapter=ReadXml(path);foreach(var image in chapter.Descendants().Where(e=>e.Name.LocalName=="img")){var src=image.Attribute("src");if(src is null||Uri.TryCreate(src.Value,UriKind.Absolute,out _))continue;var full=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,Uri.UnescapeDataString(src.Value)));if(!full.StartsWith(extracted+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(full)||new FileInfo(full).Length>20_000_000)throw new InvalidDataException("Invalid EPUB image path.");src.Value="data:image/"+(FeatureCatalog.Extension(full)=="jpg"?"jpeg":FeatureCatalog.Extension(full))+";base64,"+Convert.ToBase64String(File.ReadAllBytes(full));}
            var body=chapter.Descendants().FirstOrDefault(e=>e.Name.LocalName=="body");if(body is not null)foreach(var node in body.Nodes())html.Append(node.ToString());
        }
        var output=Path.Combine(work,"Book.html");File.WriteAllText(output,OfficeDocuments.SanitizedHtml(html.Append("</body></html>").ToString()));return output;
    }
    // EPUB 2 chapters use XHTML named entities (&nbsp; &mdash;) that only the DTD declares,
    // and the DTD stays unread. Turn them into character references before parsing.
    private static XDocument ReadXml(string path)
    {
        var settings=new System.Xml.XmlReaderSettings{DtdProcessing=System.Xml.DtdProcessing.Ignore,XmlResolver=null,MaxCharactersInDocument=20_000_000};
        if(new FileInfo(path).Length>80_000_000){using var large=System.Xml.XmlReader.Create(path,settings);return XDocument.Load(large);}
        using var reader=System.Xml.XmlReader.Create(new StringReader(OfficeDocuments.NumericEntities(OfficeDocuments.XmlText(File.ReadAllBytes(path)))),settings);return XDocument.Load(reader);
    }
    private static async Task<string> MaterializeClipping(string input,string work,CancellationToken token)
    {
        var bytes=await File.ReadAllBytesAsync(input,token);byte[]? resource=null;var sidecar=Path.Combine(Path.GetDirectoryName(input)!,"._"+Path.GetFileName(input));if(File.Exists(sidecar)&&new FileInfo(sidecar).Length<200_000_000)resource=LegacyTextCodec.AppleDoubleResource(await File.ReadAllBytesAsync(sidecar,token));
        var content=LegacyTextCodec.Clipping(bytes,resource);var output=Path.Combine(work,"Clipping."+content.Extension);await File.WriteAllBytesAsync(output,content.Bytes,token);return output;
    }
    private static void EpubFixed(byte[][] pages,string output,string title)
    {
        using var zip=ZipFile.Open(output,ZipArchiveMode.Create);
        void Write(string name,string text,CompressionLevel compression=CompressionLevel.Optimal){using var writer=new StreamWriter(zip.CreateEntry(name,compression).Open(),new UTF8Encoding(false));writer.Write(text);}
        Write("mimetype","application/epub+zip",CompressionLevel.NoCompression);Write("META-INF/container.xml","<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");var manifest=new StringBuilder();var spine=new StringBuilder();var nav=new StringBuilder();
        for(var i=0;i<pages.Length;i++){using(var stream=zip.CreateEntry($"OEBPS/page{i}.png").Open())stream.Write(pages[i]);var image=ImageEngine.FromPng(pages[i]);Write($"OEBPS/page{i}.xhtml",$"<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Page {i+1}</title><meta name=\"viewport\" content=\"width={image.PixelWidth},height={image.PixelHeight}\"/></head><body style=\"margin:0\"><img src=\"page{i}.png\" style=\"width:100%;height:100%\" alt=\"Page {i+1}\"/></body></html>");manifest.Append($"<item id=\"p{i}\" href=\"page{i}.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"i{i}\" href=\"page{i}.png\" media-type=\"image/png\"/>");spine.Append($"<itemref idref=\"p{i}\"/>");nav.Append($"<li><a href=\"page{i}.xhtml\">Page {i+1}</a></li>");}
        Write("OEBPS/nav.xhtml","<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head><title>Contents</title></head><body><nav epub:type=\"toc\"><ol>"+nav+"</ol></nav></body></html>");Write("OEBPS/content.opf","<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"book\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"book\">urn:uuid:"+Guid.NewGuid()+"</dc:identifier><dc:title>"+WebUtility.HtmlEncode(title)+"</dc:title><dc:language>en</dc:language><meta property=\"dcterms:modified\">"+DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")+"</meta><meta property=\"rendition:layout\">pre-paginated</meta></metadata><manifest><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>"+manifest+"</manifest><spine>"+spine+"</spine></package>");
    }
}
