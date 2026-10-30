using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Nashi.Core;
public sealed class MetadataService(string root)
{
    public static readonly string[] EditableTags=["EXIF:Artist","EXIF:Copyright","EXIF:ImageDescription","EXIF:Make","EXIF:Model","EXIF:Software","EXIF:DateTimeOriginal","EXIF:CreateDate","EXIF:ModifyDate","EXIF:UserComment","XMP:Title","XMP:Description","XMP:Creator","XMP:Rights","PDF:Title","PDF:Author","PDF:Subject","PDF:Keywords","QuickTime:Title","QuickTime:Artist","QuickTime:Comment","ID3:Title","ID3:Artist","ID3:Album","ID3:Year","ID3:Genre","ID3:Comment"];
    public string? Executable=>File.Exists(Path.Combine(root,"metadata-13.59","exiftool.exe"))?Path.Combine(root,"metadata-13.59","exiftool.exe"):null;
    public async Task InstallAsync(IProgress<OperationProgress>? progress,CancellationToken token)
    {
        if(Executable is not null)return;Directory.CreateDirectory(root);var stage=Path.Combine(root,".metadata-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try
        {
            var zip=Path.Combine(stage,"exiftool.zip");await VerifiedDownload.FetchAsync(new("https://downloads.sourceforge.net/project/exiftool/exiftool-13.59_64.zip"),zip,30_000_000,p=>progress?.Report(new(p*.8,"Downloading ExifTool…")),token);
            await using(var file=File.OpenRead(zip))if(Convert.ToHexString(await SHA256.HashDataAsync(file,token)).ToLowerInvariant()!="44b512b25af500724ba579d0a53c8fc5851628b692dd5e5d94ae4a15c2cba9ec")throw new InvalidDataException("Metadata tools checksum mismatch.");
            var unpack=ArchiveEngine.Extract(zip,stage,null,null,token)[0];var exe=Directory.GetFiles(unpack,"exiftool(-k).exe",SearchOption.AllDirectories).Single();var folder=Path.GetDirectoryName(exe)!;File.Move(exe,Path.Combine(folder,"exiftool.exe"));token.ThrowIfCancellationRequested();Directory.Move(folder,Path.Combine(root,"metadata-13.59"));progress?.Report(new(1,"Metadata tools ready"));
        }finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    public async Task<IReadOnlyDictionary<string,string>> ReadAsync(string input,CancellationToken token=default)
    {
        var result=await Run(Executable??throw new InvalidOperationException("Set up metadata tools once."),["-json","-G","-s","-charset","filename=UTF8","--",Path.GetFullPath(input)],token);using var json=JsonDocument.Parse(result.Output);return json.RootElement[0].EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.ToString());
    }
    public async Task CopyImageTagsAsync(string source,string output,CancellationToken token)
    {
        // Pixels are already oriented and converted to sRGB. Do not reattach a
        // stale orientation, source ICC profile, preview or image dimensions.
        await Run(Executable??throw new InvalidOperationException("Metadata tools are not ready."),["-overwrite_original","-charset","filename=UTF8","-tagsFromFile",Path.GetFullPath(source),"-all:all","--ICC_Profile","--Orientation","--ThumbnailImage","--PreviewImage","--JpgFromRaw","--ImageWidth","--ImageHeight","--ExifImageWidth","--ExifImageHeight","-Orientation=1","-ColorSpace=sRGB","--",Path.GetFullPath(output)],token);
    }
    public async Task<IReadOnlyList<string>> WriteAsync(IReadOnlyList<string> inputs,string destination,bool removeAll,IReadOnlyDictionary<string,string>? updates=null,IReadOnlyList<string>? removals=null,IProgress<OperationProgress>? progress=null,CancellationToken token=default)
    {
        if(updates?.Keys.Any(k=>!EditableTags.Contains(k))==true||removals?.Any(k=>!EditableTags.Contains(k))==true)throw new ArgumentException("Choose one of the editable metadata fields.");
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();
        foreach(var input in inputs)
        {
            token.ThrowIfCancellationRequested();var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Metadata"+Path.GetExtension(input));
            if(FeatureCatalog.Extension(input)=="pdf"&&removeAll){var fresh=DocumentEngine.Pdf("removeMetadata",[input],tx.StagingDirectory,token:token)[0];File.Move(fresh,output);}else File.Copy(input,output);
            var args=new List<string>{"-overwrite_original","-P","-ec","-charset","filename=UTF8"};if(removeAll){args.Add("-all=");if(FeatureCatalog.IsImage(input)||FeatureCatalog.Extension(input)=="gif")args.AddRange(["-tagsfromfile","@","-Orientation","-ICC_Profile"]);}if(removals is not null)args.AddRange(removals.Select(k=>"-"+k+"="));if(updates is not null)args.AddRange(updates.Select(p=>"-"+p.Key+"= "+EscapeValue(p.Value)));args.AddRange(["--",Path.GetFullPath(output)]);
            await Run(Executable??throw new InvalidOperationException("Set up metadata tools once."),args,token);outputs.Add(output);progress?.Report(new(outputs.Count/(double)inputs.Count*.98,"Saving metadata…"));
        }
        return tx.Publish(outputs,token);
    }
    // ExifTool decodes its command line with the ANSI code page and then treats the bytes as UTF-8, so
    // non-ASCII paths fail to open and non-ASCII tag values are garbled. Arguments read by "-@ -" from
    // standard input arrive as UTF-8 instead. Argfile lines cannot hold newlines, so written values are
    // C-escaped for -ec, and ExifTool drops the single space that follows "=" (keeping leading spaces).
    private static string EscapeValue(string value)=>value.Replace("\\","\\\\").Replace("\r","\\r").Replace("\n","\\n");
    private static Task<ProcessResult> Run(string executable,IReadOnlyList<string> arguments,CancellationToken token)
    {
        if(arguments.Any(a=>a.Length==0||char.IsWhiteSpace(a[0])||a[0]=='#'||a.Contains('\r')||a.Contains('\n')))throw new ArgumentException("Argument cannot be passed through an ExifTool argfile.");
        return ProcessRunner.RunAsync(executable,["-@","-"],token,standardInput:string.Join('\n',arguments)+"\n");
    }
}
