using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;

/// Small previews for any file: images are decoded directly, everything else asks
/// the Windows shell (video frames, PDF pages, documents). Videos the shell cannot
/// read fall back to one ffmpeg frame once media tools are installed.
internal static class Thumbnails
{
    /// Set when media tools are ready; enables the ffmpeg fallback for videos.
    internal static string? Ffmpeg{get;set;}
    private static readonly BlockingCollection<Action> shell=StartShell();
    private static readonly string frames=Path.Combine(Path.GetTempPath(),"Nashi-thumbnails");

    internal static async Task<BitmapSource?> LoadAsync(string path,int width)
    {
        if(!File.Exists(path))return null;
        if(FeatureCatalog.IsImage(path)&&await DecodeAsync(path,width) is {} image)return image;
        if(await ShellAsync(path,width) is {} preview)return preview;
        // A sound without cover art shows its waveform instead of a bare format label.
        if(FeatureCatalog.IsAudio(path)&&Ffmpeg is not null)return await Waveform.PeaksAsync(path,Ffmpeg) is {} peaks?Waveform.Picture(peaks,Math.Max(64,width),Math.Max(48,(int)(width*.62))):null;
        return FeatureCatalog.IsVideo(path)&&Ffmpeg is not null?await FrameAsync(path,width):null;
    }
    private static Task<BitmapSource?> DecodeAsync(string path,int width)=>Task.Run<BitmapSource?>(()=>
    {
        try
        {
            var image=new BitmapImage();image.BeginInit();image.UriSource=new Uri(path);image.DecodePixelWidth=width;image.CacheOption=BitmapCacheOption.OnLoad;image.CreateOptions=BitmapCreateOptions.IgnoreColorProfile;image.EndInit();image.Freeze();
            // BitmapImage ignores EXIF orientation; turn photos the way Explorer and the previewer show them.
            // WebP and PNG keep their stored pixels, as Windows does.
            if(FeatureCatalog.Extension(path) is "webp" or "png")return image;
            ushort orientation;using(var stream=File.OpenRead(path))orientation=ImageEngine.Orientation(BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation|BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.None).Frames[0]);
            if(orientation==1)return image;var upright=ImageEngine.Orient(image,orientation);upright.Freeze();return upright;
        }
        catch(Exception error)when(error is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or COMException){return null;}
    });
    private static Task<BitmapSource?> ShellAsync(string path,int width)
    {
        var done=new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.Add(()=>{try{done.SetResult(Shell(path,width));}catch(Exception error)when(error is COMException or ArgumentException or InvalidCastException or FileNotFoundException or UnauthorizedAccessException){done.SetResult(null);}});
        return done.Task;
    }
    /// Thumbnail only: a generic file icon would look like a preview but say nothing.
    private static BitmapSource? Shell(string path,int width)
    {
        var id=typeof(IShellItemImageFactory).GUID;SHCreateItemFromParsingName(Path.GetFullPath(path),IntPtr.Zero,ref id,out var factory);
        try
        {
            if(factory.GetImage(new(width,width),ThumbnailOnly|BiggerSizeOk,out var bitmap)!=0||bitmap==IntPtr.Zero)return null;
            try{var source=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());source.Freeze();return source;}
            finally{DeleteObject(bitmap);}
        }
        finally{Marshal.ReleaseComObject(factory);}
    }
    private static async Task<BitmapSource?> FrameAsync(string path,int width)
    {
        try
        {
            Directory.CreateDirectory(frames);var info=new FileInfo(path);
            var output=Path.Combine(frames,$"{(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(info.FullName):x8}-{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}-{width}.jpg");
            if(!File.Exists(output))
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
                // One frame a second in (or the first frame for very short clips).
                await ProcessRunner.RunAsync(Ffmpeg!,["-v","error","-nostdin","-ss","1","-i",info.FullName,"-frames:v","1","-vf",$"scale={width}:-2","-y",output],timeout.Token);
                if(!File.Exists(output))await ProcessRunner.RunAsync(Ffmpeg!,["-v","error","-nostdin","-i",info.FullName,"-frames:v","1","-vf",$"scale={width}:-2","-y",output],timeout.Token);
            }
            return File.Exists(output)?await DecodeAsync(output,width):null;
        }
        catch(Exception error)when(error is IOException or OperationCanceledException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception){return null;}
    }
    private static BlockingCollection<Action> StartShell()
    {
        // Shell thumbnail providers expect a single-threaded apartment.
        var queue=new BlockingCollection<Action>();var thread=new Thread(()=>{foreach(var job in queue.GetConsumingEnumerable())job();}){IsBackground=true,Name="Nashi thumbnails"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return queue;
    }
    private const int BiggerSizeOk=0x1,ThumbnailOnly=0x8;
    [StructLayout(LayoutKind.Sequential)]private readonly record struct NativeSize(int Width,int Height);
    [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory{[PreserveSig]int GetImage(NativeSize size,int flags,out IntPtr bitmap);}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]private static extern void SHCreateItemFromParsingName(string path,IntPtr context,ref Guid id,out IShellItemImageFactory item);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr handle);
}
