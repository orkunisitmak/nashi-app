using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
namespace Nashi.Windows;

internal readonly record struct FileArtworkStamp(bool Exists,bool Directory,long Length,long Modified)
{
    internal static FileArtworkStamp Read(string path)
    {
        try
        {
            var file=new FileInfo(path);file.Refresh();
            if(file.Exists)return new(true,false,file.Length,file.LastWriteTimeUtc.Ticks);
            var directory=new DirectoryInfo(path);directory.Refresh();
            return directory.Exists?new(true,true,0,directory.LastWriteTimeUtc.Ticks):default;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){return default;}
    }
}

/// The shell lookup and file metadata both stay off the dispatcher; duplicate rows share a flight.
internal sealed class FileArtworkCache
{
    private sealed record Entry(FileArtworkStamp Stamp,ImageSource? Image,long Access);
    private readonly object gate=new();
    private readonly Dictionary<string,Entry> cache=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,Task<ImageSource?>> flights=new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string,FileArtworkStamp> stamp;
    private readonly Func<string,FileArtworkStamp,Task<ImageSource?>> load;
    private readonly int capacity;private long clock;
    internal FileArtworkCache(Func<string,FileArtworkStamp,Task<ImageSource?>> load,int capacity=128,Func<string,FileArtworkStamp>? stamp=null)
    {this.load=load;this.capacity=Math.Max(1,capacity);this.stamp=stamp??FileArtworkStamp.Read;}
    internal Task<ImageSource?> GetAsync(string path)
    {
        try{path=Path.GetFullPath(path);}catch(Exception error)when(error is ArgumentException or NotSupportedException or PathTooLongException){return Task.FromResult<ImageSource?>(null);}
        lock(gate)
        {
            if(flights.TryGetValue(path,out var pending))return pending;
            var task=Task.Run(()=>LoadAsync(path));flights[path]=task;return task;
        }
    }
    private async Task<ImageSource?> LoadAsync(string path)
    {
        try
        {
            var current=stamp(path);
            lock(gate)if(cache.TryGetValue(path,out var entry)&&entry.Stamp==current){cache[path]=entry with{Access=++clock};return entry.Image;}
            var image=await load(path,current).ConfigureAwait(false);
            // Every consumer receives immutable artwork, safe to use on another dispatcher.
            if(image is not null&&!image.IsFrozen){if(!image.CanFreeze)return null;image.Freeze();}
            lock(gate)
            {
                if(image is not null)cache[path]=new(current,image,++clock);else cache.Remove(path);
                while(cache.Count>capacity)cache.Remove(cache.MinBy(pair=>pair.Value.Access).Key);
            }
            return image;
        }
        finally{lock(gate)flights.Remove(path);}
    }
    internal int CachedCount{get{lock(gate)return cache.Count;}}
}

internal static class FileArtwork
{
    private static readonly FileArtworkCache cache=new((path,stamp)=>ShellWorker.Run(()=>NativeIcon(path,stamp)));
    internal static readonly DependencyProperty PathProperty=DependencyProperty.RegisterAttached("Path",typeof(string),typeof(FileArtwork),new PropertyMetadata(null,PathChanged));
    private static readonly DependencyPropertyKey IsLoadingKey=DependencyProperty.RegisterAttachedReadOnly("IsLoading",typeof(bool),typeof(FileArtwork),new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsReadyKey=DependencyProperty.RegisterAttachedReadOnly("IsReady",typeof(bool),typeof(FileArtwork),new PropertyMetadata(true));
    internal static readonly DependencyProperty IsLoadingProperty=IsLoadingKey.DependencyProperty;
    internal static readonly DependencyProperty IsReadyProperty=IsReadyKey.DependencyProperty;
    internal static bool GetIsLoading(DependencyObject image)=>(bool)image.GetValue(IsLoadingProperty);
    internal static bool GetIsReady(DependencyObject image)=>(bool)image.GetValue(IsReadyProperty);
    private static readonly ImageSource placeholder=Placeholder();
    private static ImageSource Placeholder()
    {
        var outline=Geometry.Parse("M5,2 L17,2 25,10 25,30 5,30 Z M17,2 L17,10 25,10 M9,16 L21,16 M9,21 L21,21 M9,26 L17,26");
        var image=new DrawingImage(new GeometryDrawing(Brushes.Transparent,new Pen(Brushes.Gray,1.5),outline));image.Freeze();return image;
    }
    private static readonly DependencyProperty BindingProperty=DependencyProperty.RegisterAttached("Binding",typeof(ArtworkBinding),typeof(FileArtwork));
    internal static void Bind(Image image,string path)=>image.SetValue(PathProperty,path);
    internal static void BindForTest(Image image,string path,FileArtworkCache isolatedCache)
    {image.SetValue(BindingProperty,new ArtworkBinding(image,isolatedCache));Bind(image,path);}
    private static void PathChanged(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not Image image)return;
        if(image.GetValue(BindingProperty) is not ArtworkBinding binding){binding=new(image);image.SetValue(BindingProperty,binding);}
        binding.Refresh();
    }
    private sealed class ArtworkBinding
    {
        private readonly WeakReference<Image> target;private readonly FileArtworkCache source;private CancellationTokenSource? request;private Window? owner;private long generation;
        internal ArtworkBinding(Image image,FileArtworkCache? source=null)
        {target=new(image);this.source=source??cache;image.Loaded+=Loaded;image.Unloaded+=Unloaded;}
        private void Loaded(object sender,RoutedEventArgs args)
        {
            if(owner is not null)owner.Activated-=Activated;if(sender is Image image){owner=Window.GetWindow(image);if(owner is not null)owner.Activated+=Activated;}Refresh();
        }
        private void Unloaded(object sender,RoutedEventArgs args)
        {generation++;request?.Cancel();request?.Dispose();request=null;if(owner is not null)owner.Activated-=Activated;owner=null;if(target.TryGetTarget(out var image)){image.SetValue(IsLoadingKey,false);image.SetValue(IsReadyKey,true);}}
        // Activation only revalidates the stamp; keep the current icon until a changed one arrives.
        private void Activated(object? sender,EventArgs args)=>Refresh(keepImage:true);
        internal void Refresh(bool keepImage=false)
        {
            request?.Cancel();request?.Dispose();request=new();
            if(!target.TryGetTarget(out var image))return;
            var path=image.GetValue(PathProperty) as string;if(!keepImage||image.Source is null)image.Source=placeholder;var current=++generation;
            image.SetValue(IsLoadingKey,!string.IsNullOrEmpty(path));image.SetValue(IsReadyKey,string.IsNullOrEmpty(path));
            if(!string.IsNullOrEmpty(path))_ = ApplyAsync(path,request.Token,current);
        }
        private async Task ApplyAsync(string path,CancellationToken cancellation,long version)
        {
            try
            {
                var artwork=await source.GetAsync(path).WaitAsync(cancellation).ConfigureAwait(false);
                if(cancellation.IsCancellationRequested||!target.TryGetTarget(out var image))return;
                await image.Dispatcher.InvokeAsync(()=>
                {
                    if(version==generation&&!cancellation.IsCancellationRequested&&target.TryGetTarget(out var current)&&Equals(current.GetValue(PathProperty),path))current.Source=artwork??placeholder;
                });
            }
            catch(OperationCanceledException){}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or COMException or InvalidOperationException or ArgumentException){/* Keep the placeholder; never break a row for an icon. */}
            finally
            {
                if(target.TryGetTarget(out var image)&&!image.Dispatcher.HasShutdownStarted)await image.Dispatcher.InvokeAsync(()=>
                {
                    if(version==generation&&target.TryGetTarget(out var current)){current.SetValue(IsLoadingKey,false);current.SetValue(IsReadyKey,true);}
                });
            }
        }
    }
    private static ImageSource? NativeIcon(string path,FileArtworkStamp stamp)
    {
        var flags=0x100u|0x1u; // SHGFI_ICON | SHGFI_SMALLICON
        if(!stamp.Exists)flags|=0x10; // name/extension association for missing entries
        _ = SHGetFileInfo(path,stamp.Directory?0x10u:0x80u,out var info,(uint)Marshal.SizeOf<Info>(),flags);
        if(info.Icon==IntPtr.Zero)return null;
        try{var image=Imaging.CreateBitmapSourceFromHIcon(info.Icon,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());image.Freeze();return image;}finally{DestroyIcon(info.Icon);}
    }
    /// Shell extensions can require COM/STA. A single background worker also bounds concurrent shell work.
    private static class ShellWorker
    {
        private static readonly BlockingCollection<Action> jobs=new();
        static ShellWorker()
        {
            var thread=new Thread(()=>{var initialized=OleInitialize(IntPtr.Zero)>=0;try{foreach(var job in jobs.GetConsumingEnumerable())job();}finally{if(initialized)OleUninitialize();}}){IsBackground=true,Name="Nashi file artwork"};
            thread.SetApartmentState(ApartmentState.STA);thread.Start();
        }
        internal static Task<T> Run<T>(Func<T> job)
        {
            var completion=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.Add(()=>{try{completion.SetResult(job());}catch(Exception error){completion.SetException(error);}});return completion.Task;
        }
        [DllImport("ole32.dll")]private static extern int OleInitialize(IntPtr reserved);
        [DllImport("ole32.dll")]private static extern void OleUninitialize();
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct Info{public IntPtr Icon;public int Index;public uint Attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string Type;}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr SHGetFileInfo(string path,uint attributes,out Info info,uint size,uint flags);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr icon);
}
