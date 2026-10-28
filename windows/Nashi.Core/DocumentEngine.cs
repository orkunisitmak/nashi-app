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
            else if(tool is "compress" or "removeMetadata")
            {
                using var result=new PdfDocument();foreach(var page in source.Pages)result.AddPage(page);result.Options.CompressContentStreams=true;result.Info.Title="";result.Info.Author="";result.Info.Subject="";result.Info.Keywords="";
                var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+(tool=="compress"?" Compressed.pdf":" Metadata.pdf"));result.Save(output);outputs.Add(output);
            }
            else throw new ArgumentException("Unknown PDF tool.");
        }
        return tx.Publish(outputs,token);
    }
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
