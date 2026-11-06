using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Nashi.Core;
namespace Nashi.Linux;

/// Files on the Linux clipboard: file managers exchange text/uri-list, and GNOME-style
/// managers (Files, Nemo, Caja, Thunar) also read x-special/gnome-copied-files.
public static class ClipboardFiles
{
    public static List<string> ParseUris(string? text)
    {
        var paths=new List<string>();if(string.IsNullOrWhiteSpace(text))return paths;
        foreach(var raw in text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            if(raw.StartsWith('#')||raw is "copy" or "cut")continue;
            string? path=raw.StartsWith("file://",StringComparison.Ordinal)&&Uri.TryCreate(raw,UriKind.Absolute,out var uri)?uri.LocalPath:Path.IsPathFullyQualified(raw)?raw:null;
            if(path is not null&&(File.Exists(path)||Directory.Exists(path)))paths.Add(path);else return [];
        }
        return paths;
    }
    public static readonly DataFormat<byte[]> UriList=DataFormat.CreateBytesPlatformFormat("text/uri-list");
    public static readonly DataFormat<byte[]> GnomeFiles=DataFormat.CreateBytesPlatformFormat("x-special/gnome-copied-files");
    public static readonly DataFormat<byte[]> Png=DataFormat.CreateBytesPlatformFormat("image/png");
    public static async Task CopyAsync(TopLevel owner,IReadOnlyList<string> paths)
    {
        if(owner.Clipboard is not {} clipboard)throw new InvalidOperationException("The clipboard is unavailable.");
        var uris=paths.Select(p=>new Uri(Path.GetFullPath(p)).AbsoluteUri).ToArray();var transfer=new DataTransfer();
        var first=new DataTransferItem();transfer.Add(first);
        first.Set(UriList,System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n",uris)+"\r\n"));
        first.Set(GnomeFiles,System.Text.Encoding.UTF8.GetBytes("copy\n"+string.Join("\n",uris)));
        first.SetText(string.Join("\n",paths));
        var added=false;
        foreach(var path in paths)
        {
            IStorageItem? item=Directory.Exists(path)?await owner.StorageProvider.TryGetFolderFromPathAsync(path):await owner.StorageProvider.TryGetFileFromPathAsync(path);if(item is null)continue;
            if(!added){first.SetFile(item);added=true;}else transfer.Add(DataTransferItem.CreateFile(item));
        }
        await clipboard.SetDataAsync(transfer);
    }
    /// Pasted text and images become real files in the Library folder.
    public static string SaveImport(string libraryFolder,string stem,string extension,byte[] content)
    {
        var folder=Path.Combine(libraryFolder,"Clipboard");Directory.CreateDirectory(folder);
        var path=SafePaths.Unique(folder,$"{stem} {DateTime.Now:yyyy-MM-dd HH.mm.ss}.{extension}");File.WriteAllBytes(path,content);return path;
    }
}
