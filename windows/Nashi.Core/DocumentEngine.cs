using System.IO.Compression;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Nashi.Core;

public static partial class DocumentEngine
{
    public static IReadOnlyList<string> Pdf(string tool,IReadOnlyList<string> inputs,string destination,int[]? pages=null,CancellationToken token=default,Dictionary<int,int>? rotations=null,int pagesPerFile=1,int[]? pageRotations=null,string password="",bool uniformWidth=false,string pageRanges="")
    {
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();
        if(tool=="mergePDFs")
        {
            if(inputs.Count<2)throw new ArgumentException("Choose at least two PDFs.");
            using var merged=new PdfDocument();
            foreach(var file in inputs){token.ThrowIfCancellationRequested();using var source=PdfReader.Open(file,password,PdfDocumentOpenMode.Import);RequireRebuildable(source,tool);foreach(var page in source.Pages)merged.AddPage(page);}
            var output=SafePaths.Unique(tx.StagingDirectory,"Merged.pdf");merged.Save(output);outputs.Add(output);
        }
        else foreach(var input in inputs)
        {
            token.ThrowIfCancellationRequested();using var source=PdfReader.Open(input,password,PdfDocumentOpenMode.Import);RequireRebuildable(source,tool);
            if(tool=="splitPDF")
            {
                if(pagesPerFile<1||pagesPerFile>1000)throw new ArgumentException("Choose between 1 and 1,000 pages per file.");
                var selected=pages??PageRange(pageRanges,source.PageCount);if(selected.Length==0||selected.Any(p=>p<1||p>source.PageCount))throw new ArgumentException("Choose valid PDF pages.");
                for(var i=0;i<selected.Length;i+=pagesPerFile)
                {
                    token.ThrowIfCancellationRequested();using var part=new PdfDocument();foreach(var page in selected.Skip(i).Take(pagesPerFile))part.AddPage(source.Pages[page-1]);
                    var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+$" Page {i+1}.pdf");part.Save(output);outputs.Add(output);
                }
            }
            else if(tool=="organizePDF")
            {
                if(pages is null||pages.Length==0||pages.Any(p=>p<1||p>source.PageCount))throw new ArgumentException("Enter page numbers within this PDF, in the desired order.");
                if(uniformWidth&&(source.Internals.Catalog.Elements.ContainsKey("/Names")||source.Internals.Catalog.Elements.ContainsKey("/Outlines")||source.Pages.Cast<PdfPage>().Any(p=>p.Elements.ContainsKey("/Annots"))))throw new ArgumentException("Turn off uniform width to preserve this PDF’s form fields, attachments, links or navigation.");
                using var result=new PdfDocument();for(var n=0;n<pages.Length;n++){var page=pages[n];var added=result.AddPage(source.Pages[page-1]);added.Rotate=(added.Rotate+(pageRotations is not null?pageRotations[n]:rotations?.GetValueOrDefault(page)??0))%360;}
                var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Organized.pdf");result.Save(output);if(uniformWidth&&tool=="organizePDF")UniformWidths(output,token);outputs.Add(output);
            }
            else if(tool=="compress")
            {
                // A rebuilt copy is saved without the password and permissions, which macOS keeps. PDFsharp cannot re-create them
                // (the typed password may be the owner's), so a protected PDF is kept byte for byte instead of losing its protection.
                if(Protected(source)){var kept=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Compressed.pdf");File.Copy(input,kept);outputs.Add(kept);continue;}
                // Compression keeps the document properties, as on macOS; only metadata cleanup clears them.
                using var result=new PdfDocument();foreach(var page in source.Pages)result.AddPage(page);result.Options.CompressContentStreams=true;result.Info.Title=source.Info.Title;result.Info.Author=source.Info.Author;result.Info.Subject=source.Info.Subject;result.Info.Keywords=source.Info.Keywords;result.Info.Creator=source.Info.Creator;
                var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Compressed.pdf");result.Save(output);outputs.Add(output);
            }
            else if(tool=="removeMetadata")
            {
                using var result=new PdfDocument();var copies=new Dictionary<PdfPage,PdfPage>();foreach(var page in source.Pages)copies[page]=result.AddPage(page);CopyOutlines(source,result,copies);CopyPageLabels(source,result);result.Options.CompressContentStreams=true;result.Info.Title="";result.Info.Author="";result.Info.Subject="";result.Info.Keywords="";
                var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+(tool=="compress"?" Compressed.pdf":" Metadata.pdf"));result.Save(output);outputs.Add(output);
            }
            else throw new ArgumentException("Unknown PDF tool.");
        }
        return tx.Publish(outputs,token);
    }
    /// Whether the PDF was encrypted (a user or owner password); every /Encrypt dictionary names its /Filter.
    public static bool Protected(PdfDocument pdf)=>pdf.SecurityHandler.Elements.ContainsKey("/Filter");
    // These tools copy pages into a new document, which leaves the catalog's form field registry
    // and attachments behind: fields go dead and files disappear without a word. Refuse instead,
    // as macOS does where it cannot keep them. Unlike macOS, metadata cleanup still accepts tagged
    // and layered PDFs: most PDFs saved from Word are tagged, and refusing them would make the tool
    // unusable for them. Their tags and layer settings are dropped, as they were before.
    private static void RequireRebuildable(PdfDocument source,string tool)
    {
        if(tool=="compress")return;var catalog=source.Internals.Catalog.Elements;
        var widgets=source.Pages.Cast<PdfPage>().Any(p=>p.Elements.GetArray("/Annots") is {} annotations&&annotations.Elements.Any(e=>(e is PdfSharp.Pdf.Advanced.PdfReference reference?reference.Value:e) is PdfDictionary annotation&&annotation.Elements.GetName("/Subtype")=="/Widget"));
        var attachments=catalog.GetDictionary("/Names")?.Elements.ContainsKey("/EmbeddedFiles")==true||catalog.ContainsKey("/AF")||catalog.ContainsKey("/Collection");
        // Some generators write an empty /AcroForm; only fields (or an XFA form) are lost.
        var form=catalog.GetDictionary("/AcroForm");var fields=form is not null&&(form.Elements.GetArray("/Fields") is {Elements.Count:>0}||form.Elements.ContainsKey("/XFA"));
        if(!fields&&!widgets&&!attachments)return;
        throw new InvalidDataException(tool=="removeMetadata"?"This PDF has form fields or attachments that metadata cleanup cannot keep. No copy was exported.":"This PDF has form fields or attachments that merging, splitting or reordering pages cannot keep. No copy was exported.");
    }
    private static void CopyOutlines(PdfDocument source,PdfDocument result,Dictionary<PdfPage,PdfPage> copies)
    {
        static PdfItem? Value(PdfItem? item)=>item is PdfSharp.Pdf.Advanced.PdfReference reference?reference.Value:item;var visited=new HashSet<object>(ReferenceEqualityComparer.Instance);
        static string? Key(PdfItem? item)=>item switch{PdfName name=>name.Value,PdfString text=>text.Value,PdfStringObject text=>text.Value,_=>null};Dictionary<string,PdfItem?>? names=null;
        PdfArray? Target(PdfItem? item)
        {
            item=Value(item);if(Key(item) is not {} key)return item as PdfArray;
            if(names is null)
            {
                names=new(StringComparer.Ordinal);var catalog=source.Internals.Catalog.Elements;var seen=new HashSet<object>(ReferenceEqualityComparer.Instance);
                if(Value(catalog["/Dests"]) is PdfDictionary dests)foreach(var name in dests.Elements.Keys)names.TryAdd(name,dests.Elements[name]);
                void Walk(PdfDictionary? node,int depth){if(node is null||depth>=64||!seen.Add(node))return;if(Value(node.Elements["/Names"]) is PdfArray pairs)for(var i=0;i+1<pairs.Elements.Count;i+=2)if(Key(Value(pairs.Elements[i])) is {} name)names.TryAdd(name,pairs.Elements[i+1]);if(Value(node.Elements["/Kids"]) is PdfArray kids)foreach(var kid in kids.Elements)Walk(Value(kid) as PdfDictionary,depth+1);}
                Walk(Value((Value(catalog["/Names"]) as PdfDictionary)?.Elements["/Dests"]) as PdfDictionary,0);
            }
            if(!names.TryGetValue(key,out var found)&&!names.TryGetValue(key.StartsWith('/')?key[1..]:"/"+key,out found))return null;
            var value=Value(found);return (value is PdfDictionary wrapper?Value(wrapper.Elements["/D"]):value) as PdfArray;
        }
        void Copy(PdfDictionary? item,PdfOutlineCollection into,int depth)
        {
            for(;item is not null&&depth<512&&visited.Add(item);item=Value(item.Elements["/Next"]) as PdfDictionary)
            {
                var action=Value(item.Elements["/A"]) as PdfDictionary;var target=Target(item.Elements["/Dest"])??(action?.Elements.GetName("/S")=="/GoTo"?Target(action.Elements["/D"]):null);var outline=new PdfOutline{Title=Value(item.Elements["/Title"]) is PdfString or PdfStringObject or PdfName?item.Elements.GetString("/Title"):"",Opened=Value(item.Elements["/Count"]) is PdfInteger{Value:>0}};into.Add(outline);
                double? At(int index)=>target!.Elements.Count>index&&Value(target.Elements[index]) is PdfInteger or PdfReal?target.Elements.GetReal(index):null;
                if(target is {Elements.Count:>0}&&Value(target.Elements[0]) is PdfPage page&&copies.TryGetValue(page,out var copy)){outline.DestinationPage=copy;if(target.Elements.Count>1&&Value(target.Elements[1]) is PdfName fit&&Enum.TryParse<PdfPageDestinationType>(fit.Value.TrimStart('/'),true,out var type)&&Enum.IsDefined(type)&&string.Equals(type.ToString(),fit.Value.TrimStart('/'),StringComparison.OrdinalIgnoreCase))
                {
                    outline.PageDestinationType=type;
                    if(type==PdfPageDestinationType.Xyz){outline.Left=At(2);outline.Top=At(3);outline.Zoom=At(4);}else if(type is PdfPageDestinationType.FitH or PdfPageDestinationType.FitBH)outline.Top=At(2);else if(type is PdfPageDestinationType.FitV or PdfPageDestinationType.FitBV)outline.Left=At(2);else if(type==PdfPageDestinationType.FitR){outline.Left=At(2);outline.Bottom=At(3)??0;outline.Right=At(4)??0;outline.Top=At(5);}
                }}
                Copy(Value(item.Elements["/First"]) as PdfDictionary,outline.Outlines,depth+1);
            }
        }
        var root=Value(source.Internals.Catalog.Elements["/Outlines"]) as PdfDictionary;if(Value(root?.Elements["/First"]) is PdfDictionary first&&visited.Add(root!)){Copy(first,result.Outlines,0);result.Internals.Catalog.Elements["/PageMode"]=Value(source.Internals.Catalog.Elements["/PageMode"]) as PdfName??new PdfName("/UseNone");}
    }
    // Page labels (i, ii, A-1…) number pages by position, and cleanup keeps every page in order, so the catalog's number tree is
    // copied as it is. macOS refuses these files; LaTeX and most book PDFs carry labels, so refusing would leave them uncleanable.
    // Indirect nodes stay indirect and shared ones are copied once; anything that cannot be copied faithfully is refused.
    private static void CopyPageLabels(PdfDocument source,PdfDocument result)
    {
        if(!source.Internals.Catalog.Elements.ContainsKey("/PageLabels"))return;var copies=new Dictionary<PdfItem,PdfItem>(ReferenceEqualityComparer.Instance);var count=0;
        static InvalidDataException Refused()=>new("This PDF contains custom page labels. Metadata cleanup cannot safely preserve those features. No copy was exported.");
        PdfItem Copy(PdfItem? item,int depth)
        {
            var value=item is PdfSharp.Pdf.Advanced.PdfReference reference?reference.Value:item;if(value is null||depth>32||++count>100_000)throw Refused();
            if(copies.TryGetValue(value,out var copied))return copied;
            PdfItem Indirect(PdfObject copy){if(item is not PdfSharp.Pdf.Advanced.PdfReference)return copy;result.Internals.AddObject(copy);return copies[value]=copy.Reference!;}
            switch(value)
            {
                case PdfDictionary{Stream:null} dictionary:{var copy=new PdfDictionary(result);var link=Indirect(copy);foreach(var key in dictionary.Elements.Keys)copy.Elements[key]=Copy(dictionary.Elements[key],depth+1);return link;}
                case PdfArray array:{var copy=new PdfArray(result);var link=Indirect(copy);foreach(var element in array.Elements)copy.Elements.Add(Copy(element,depth+1));return link;}
                case PdfStringObject text:return new PdfString(text.Value,PdfStringEncoding.Unicode);
                case PdfIntegerObject number:return new PdfInteger(number.Value);
                case PdfNameObject name:return new PdfName(name.Value);
                case PdfName or PdfInteger or PdfLongInteger or PdfReal or PdfString or PdfBoolean or PdfNull:return value;
                default:throw Refused();
            }
        }
        result.Internals.Catalog.Elements["/PageLabels"]=Copy(source.Internals.Catalog.Elements["/PageLabels"],0);
    }
    public static string Zip(IReadOnlyList<string> inputs,string destination,IProgress<OperationProgress>? progress=null,CancellationToken token=default)
    {
        if(inputs.Count==0)throw new ArgumentException("Choose files to zip.");
        using var tx=new ExportTransaction(destination);var output=Path.Combine(tx.StagingDirectory,"Archive.zip");
        var usedNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var entries=0;long total=0;
        using(var zip=ZipFile.Open(output,ZipArchiveMode.Create))
        {
            void Add(string input,string name,int depth)
            {
                token.ThrowIfCancellationRequested();if(depth>64||++entries>10000||(File.GetAttributes(input)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Zip regular files and folders without links (10,000 item limit).");
                if(Directory.Exists(input)){zip.CreateEntry(name.TrimEnd('/')+"/");foreach(var child in Directory.EnumerateFileSystemEntries(input))Add(child,name+"/"+Path.GetFileName(child),depth+1);}
                else{total=checked(total+new FileInfo(input).Length);if(total>4L*1024*1024*1024)throw new InvalidDataException("Choose up to 4 GB of files.");using var source=File.OpenRead(input);using var target=zip.CreateEntry(name,CompressionLevel.Optimal).Open();var bytes=new byte[81920];int read;while((read=source.Read(bytes))>0){token.ThrowIfCancellationRequested();target.Write(bytes,0,read);}}
            }
            for(var i=0;i<inputs.Count;i++)
            {
                var input=Path.GetFullPath(inputs[i]).TrimEnd(Path.DirectorySeparatorChar);if(!File.Exists(input)&&!Directory.Exists(input))throw new FileNotFoundException("A selected item is missing.");
                // A destination inside a selected folder must never pack its own staging directory.
                if(Directory.Exists(input)&&Path.GetFullPath(destination).StartsWith(input+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Directory.Exists(input)&&Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar).Equals(input,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Save the archive outside the folder being zipped.");
                var name=Path.GetFileName(input);var n=2;while(!usedNames.Add(name))name=Path.GetFileNameWithoutExtension(input)+$" {n++}"+Path.GetExtension(input);Add(input,name,0);progress?.Report(new((i+1d)/inputs.Count*.98,"Packing files…"));
            }
        }
        return tx.Publish([output],token)[0];
    }
    public static IReadOnlyList<string> Unzip(string input,string destination,IProgress<OperationProgress>? progress=null,CancellationToken token=default)
    {
        const long maxBytes=4L*1024*1024*1024;const int maxEntries=10000;
        using var zip=ZipFile.OpenRead(input);
        if(zip.Entries.Count>maxEntries)throw new InvalidDataException("Archive has too many entries (10,000 limit).");
        long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in zip.Entries)
        {
            total=checked(total+entry.Length);if(total>maxBytes)throw new InvalidDataException("Archive expands beyond 4 GB.");
            if(((entry.ExternalAttributes>>16)&0xF000)==0xA000||(entry.ExternalAttributes&0x400)!=0)throw new InvalidDataException("Archive contains a link.");
            if(!names.Add(entry.FullName.TrimEnd('/','\\')))throw new InvalidDataException("Archive contains duplicate names.");
        }
        using var tx=new ExportTransaction(destination);var folder=Path.Combine(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Extracted");Directory.CreateDirectory(folder);
        var paths=zip.Entries.Select(e=>(Entry:e,Path:SafePaths.ArchiveEntry(folder,e.FullName.TrimEnd('/','\\')))).ToList();long copied=0;
        foreach(var pair in paths)
        {
            token.ThrowIfCancellationRequested();var entry=pair.Entry;
            if(entry.FullName.EndsWith('/')||entry.FullName.EndsWith('\\')){Directory.CreateDirectory(pair.Path);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(pair.Path)!);using var source=entry.Open();using var target=new FileStream(pair.Path,FileMode.CreateNew);
            var buffer=new byte[81920];long entryBytes=0;int count;
            while((count=source.Read(buffer))>0){token.ThrowIfCancellationRequested();entryBytes+=count;copied+=count;if(entryBytes>entry.Length||copied>maxBytes)throw new InvalidDataException("Archive size exceeds its declared limit.");target.Write(buffer,0,count);progress?.Report(new(total==0?null:(double)copied/total*.98,"Extracting files…"));}
        }
        return tx.Publish([folder],token);
    }
}
