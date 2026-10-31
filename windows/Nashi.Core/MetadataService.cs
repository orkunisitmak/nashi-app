using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Nashi.Core;
public sealed class MetadataService(string root,string? perlSearchPath=null)
{
    public static readonly string[] EditableTags=["EXIF:Artist","EXIF:Copyright","EXIF:ImageDescription","EXIF:Make","EXIF:Model","EXIF:Software","EXIF:DateTimeOriginal","EXIF:CreateDate","EXIF:ModifyDate","EXIF:UserComment","XMP:Title","XMP:Description","XMP:Creator","XMP:Rights","PDF:Title","PDF:Author","PDF:Subject","PDF:Keywords","QuickTime:Title","QuickTime:Artist","QuickTime:Comment","ID3:Title","ID3:Artist","ID3:Album","ID3:Year","ID3:Genre","ID3:Comment"];
    // Windows gets ExifTool with Strawberry Perl; Linux gets the plain Perl script and needs a system perl.
    private static readonly string FileName=OperatingSystem.IsWindows()?"exiftool.exe":"exiftool";
    // The exiftool script starts with "#!/usr/bin/env perl" and loads lib/Image/ExifTool.pm beside itself.
    private bool Ready(string directory)=>File.Exists(Path.Combine(directory,FileName))&&ToolInstallation.CanExecute(Path.Combine(directory,FileName))&&(OperatingSystem.IsWindows()||File.Exists(Path.Combine(directory,"lib","Image","ExifTool.pm"))&&PerlUsable());
    private bool PerlUsable()
    {
        foreach(var entry in (perlSearchPath??Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            if(!Path.IsPathFullyQualified(entry))continue;var perl=Path.Combine(entry,"perl");
            // perl is often a symlink (perl -> perl5.x), so usability is checked on its target like Toolchain does.
            try{var checkedPath=new FileInfo(perl).ResolveLinkTarget(true) is FileInfo target?target.FullName:perl;if(ToolInstallation.UsableFile(checkedPath)&&ToolInstallation.CanExecute(checkedPath))return true;}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return false;
    }
    public string? Executable=>Ready(Path.Combine(root,"metadata-13.59"))?Path.Combine(root,"metadata-13.59",FileName):null;
    public async Task InstallAsync(IProgress<OperationProgress>? progress,CancellationToken token)
    {
        if(Executable is not null)return;var windows=OperatingSystem.IsWindows();
        if(!windows&&!PerlUsable())throw new InvalidOperationException("ExifTool needs Perl. Install perl and try again.");
        var (url,sha256,script)=windows?("https://downloads.sourceforge.net/project/exiftool/exiftool-13.59_64.zip","44b512b25af500724ba579d0a53c8fc5851628b692dd5e5d94ae4a15c2cba9ec","exiftool(-k).exe"):("https://downloads.sourceforge.net/project/exiftool/Image-ExifTool-13.59.tar.gz","668ea3acececb7235fbd0f4900e72d5f12c9b07e5c778fd36cb1e9b5828fd65a","exiftool");
        Directory.CreateDirectory(root);var stage=Path.Combine(root,".metadata-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try
        {
            var zip=Path.Combine(stage,windows?"exiftool.zip":"exiftool.tar.gz");await VerifiedDownload.FetchAsync(new(url),zip,30_000_000,p=>progress?.Report(new(p*.8,"Downloading ExifTool…")),token);
            await using(var file=File.OpenRead(zip))if(Convert.ToHexString(await SHA256.HashDataAsync(file,token)).ToLowerInvariant()!=sha256)throw new InvalidDataException("Metadata tools checksum mismatch.");
            var unpack=ArchiveEngine.Extract(zip,stage,null,null,token)[0];var exe=Directory.GetFiles(unpack,script,SearchOption.AllDirectories).Single();var folder=Path.GetDirectoryName(exe)!;if(windows)File.Move(exe,Path.Combine(folder,"exiftool.exe"));ToolInstallation.MakeExecutable(Path.Combine(folder,FileName));token.ThrowIfCancellationRequested();ToolInstallation.Publish(folder,Path.Combine(root,"metadata-13.59"),Ready);progress?.Report(new(1,"Metadata tools ready"));
        }finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    public async Task<IReadOnlyDictionary<string,string>> ReadAsync(string input,CancellationToken token=default)
    {
        var result=await ProcessRunner.RunAsync(Executable??throw new InvalidOperationException("Set up metadata tools once."),["-json","-G","-s","-charset","filename=UTF8","--",Path.GetFullPath(input)],token);using var json=JsonDocument.Parse(result.Output);return json.RootElement[0].EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.ToString());
    }
    public async Task CopyImageTagsAsync(string source,string output,CancellationToken token)
    {
        // Pixels are already oriented and converted to sRGB. Do not reattach a
        // stale orientation, source ICC profile, preview or image dimensions.
        await ProcessRunner.RunAsync(Executable??throw new InvalidOperationException("Metadata tools are not ready."),["-overwrite_original","-charset","filename=UTF8","-tagsFromFile",Path.GetFullPath(source),"-all:all","--ICC_Profile","--Orientation","--ThumbnailImage","--PreviewImage","--JpgFromRaw","--ImageWidth","--ImageHeight","--ExifImageWidth","--ExifImageHeight","-Orientation=1","-ColorSpace=sRGB","--",Path.GetFullPath(output)],token);
    }
    public async Task<IReadOnlyList<string>> WriteAsync(IReadOnlyList<string> inputs,string destination,bool removeAll,IReadOnlyDictionary<string,string>? updates=null,IReadOnlyList<string>? removals=null,IProgress<OperationProgress>? progress=null,CancellationToken token=default)
    {
        if(updates?.Keys.Any(k=>!EditableTags.Contains(k))==true||removals?.Any(k=>!EditableTags.Contains(k))==true)throw new ArgumentException("Choose one of the editable metadata fields.");
        using var tx=new ExportTransaction(destination);var outputs=new List<string>();
        foreach(var input in inputs)
        {
            token.ThrowIfCancellationRequested();var output=SafePaths.Unique(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Metadata"+Path.GetExtension(input));
            if(FeatureCatalog.Extension(input)=="pdf"&&removeAll){var fresh=DocumentEngine.Pdf("removeMetadata",[input],tx.StagingDirectory,token:token)[0];File.Move(fresh,output);}else File.Copy(input,output);
            var args=new List<string>{"-overwrite_original","-P","-charset","filename=UTF8"};if(removeAll){args.Add("-all=");if(FeatureCatalog.IsImage(input)||FeatureCatalog.Extension(input)=="gif")args.AddRange(["-tagsfromfile","@","-Orientation","-ICC_Profile"]);}if(removals is not null)args.AddRange(removals.Select(k=>"-"+k+"="));if(updates is not null)args.AddRange(updates.Select(p=>"-"+p.Key+"="+p.Value));args.AddRange(["--",output]);
            await ProcessRunner.RunAsync(Executable??throw new InvalidOperationException("Set up metadata tools once."),args,token);outputs.Add(output);progress?.Report(new(outputs.Count/(double)inputs.Count*.98,"Saving metadata…"));
        }
        return tx.Publish(outputs,token);
    }
}
