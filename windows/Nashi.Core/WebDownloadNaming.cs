namespace Nashi.Core;

public sealed partial class WebDownload
{
    private static bool InvalidNameCharacter(char value)=>char.IsControl(value)||"<>:\"/\\|?*".Contains(value);
    public static void ValidateFileName(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>160||name.Any(InvalidNameCharacter)||name!=name.Trim()||name.EndsWith('.'))
            throw new ArgumentException("Use 1–160 characters without folders or these characters: < > : \" / \\ | ? *");
        try{SafePaths.ArchiveEntry(Path.GetTempPath(),name);}
        catch(InvalidDataException){throw new ArgumentException("This file name is reserved. Choose another name.");}
    }
    public static string SuggestedFileName(string title)
    {
        var name=new string(title.Select(value=>InvalidNameCharacter(value)?'_':value).ToArray()).Trim().TrimEnd('.');
        if(name.Length==0)name="Web media";
        if(name.Length>159)name=name[..159].TrimEnd(' ','.');
        if(char.IsHighSurrogate(name[^1]))name=name[..^1];
        try{ValidateFileName(name);}
        catch(ArgumentException){name="_"+name[..Math.Min(name.Length,159)];}
        return name;
    }
    internal static IReadOnlyList<string> PublishDownloads(ExportTransaction transaction,IReadOnlyList<string> files,string? fileName,CancellationToken token)
    {
        if(fileName is null)return transaction.Publish(files,token);
        ValidateFileName(fileName);var renamed=new List<string>();var namedDirectory=Path.Combine(transaction.StagingDirectory,"named");Directory.CreateDirectory(namedDirectory);
        for(var i=0;i<files.Count;i++)
        {
            token.ThrowIfCancellationRequested();
            var extension=Path.GetExtension(files[i]);var stem=fileName.EndsWith(extension,StringComparison.OrdinalIgnoreCase)&&extension.Length>0&&fileName.Length>extension.Length?fileName[..^extension.Length]:fileName;
            ValidateFileName(stem);
            var target=SafePaths.Unique(namedDirectory,stem+(files.Count>1?$" {i+1:00}":"")+extension);
            File.Move(files[i],target);renamed.Add(target);
        }
        return transaction.Publish(renamed,token);
    }
}
