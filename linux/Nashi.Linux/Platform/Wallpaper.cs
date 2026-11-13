using Nashi.Core;
namespace Nashi.Linux;

/// Set as Wallpaper. KDE Plasma has plasma-apply-wallpaperimage; GNOME (and Budgie, Unity) read
/// org.gnome.desktop.background through gsettings. Nashi keeps its own copy of the picture, so the
/// wallpaper survives when the original is moved or deleted (screenshots often are).
public static class Wallpaper
{
    private static string? Plasma=>LinuxShell.FindInPath("plasma-apply-wallpaperimage");
    private static bool Gnome=>(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")??"").Split(':').Any(d=>d is "GNOME" or "Budgie" or "Unity"||d.EndsWith("GNOME",StringComparison.Ordinal))&&LinuxShell.FindInPath("gsettings") is not null;
    public static bool Supported=>Plasma is not null||Gnome;
    /// Formats every desktop shows as is; others (HEIC, AVIF, TIFF, RAW, …) are saved as PNG first.
    private static readonly string[] Plain=["jpg","jpeg","png","webp","bmp"];
    internal static string Folder=>Path.Combine(AppPaths.Data,"Wallpapers");

    /// Copies (or converts) the picture into Nashi's wallpaper folder under a new name, since desktops
    /// cache a wallpaper by its path. Only the last few are kept.
    public static string Prepare(string source)
    {
        Directory.CreateDirectory(Folder);
        var ext=FeatureCatalog.Extension(source);var plain=Plain.Contains(ext);
        var target=SafePaths.Unique(Folder,$"{Path.GetFileNameWithoutExtension(source)} {DateTime.Now:yyyyMMdd-HHmmss}.{(plain?ext:"png")}");
        if(plain)File.Copy(source,target,false);
        else{using var image=Engine.ImageEngine.Load(source);image.Write(target,ImageMagick.MagickFormat.Png);}
        foreach(var old in new DirectoryInfo(Folder).GetFiles().OrderByDescending(f=>f.LastWriteTimeUtc).Skip(5))try{old.Delete();}catch(IOException){}
        return target;
    }
    public static async Task SetAsync(string source)
    {
        var picture=await Task.Run(()=>Prepare(source));
        if(Plasma is {} plasma){await ProcessRunner.RunAsync(plasma,[picture]);return;}
        if(LinuxShell.FindInPath("gsettings") is {} gsettings)
        {
            var uri=new Uri(picture).AbsoluteUri;
            await ProcessRunner.RunAsync(gsettings,["set","org.gnome.desktop.background","picture-uri",uri]);
            try{await ProcessRunner.RunAsync(gsettings,["set","org.gnome.desktop.background","picture-uri-dark",uri]);}catch(IOException){}   // GNOME 42+ only
            return;
        }
        throw new InvalidOperationException("This desktop has no way to set the wallpaper from apps.");
    }
}
