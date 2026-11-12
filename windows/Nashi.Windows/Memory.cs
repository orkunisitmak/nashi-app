using System.Runtime;
using System.Windows;
using System.Windows.Threading;
namespace Nashi.Windows;

// Pictures keep their pixels outside the managed heap, where the collector cannot see how much is waiting:
// a closed editor or wheel could hold hundreds of megabytes until some later, unrelated collection.
// So when a window goes away (or the app goes back to the tray), collect once things have settled.
// A full collection pauses the app, so it waits until the app is idle and only runs when the process has
// grown enough since it last settled to be worth it (a closed toast or wheel alone is not).
internal static class Memory
{
    private const long Worth=24L<<20;
    private static readonly DispatcherTimer settle=new(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(1200)};
    // The baseline is read a moment after collecting: finalizers free the pictures' native memory only after the collection itself.
    private static readonly DispatcherTimer measure=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(500)};
    private static long settled;private static bool always;
    internal static void Watch()
    {
        settle.Tick+=(_,_)=>{settle.Stop();var forced=always;always=false;if(!forced&&Private()-settled<Worth)return;GCSettings.LargeObjectHeapCompactionMode=GCLargeObjectHeapCompactionMode.CompactOnce;GC.Collect(2,GCCollectionMode.Forced,true,true);settled=Private();measure.Stop();measure.Start();};
        measure.Tick+=(_,_)=>{measure.Stop();settled=Private();};
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.UnloadedEvent,new RoutedEventHandler((_,_)=>Release()));
        // Long sessions with nothing closing (a window moved and brought forward all day): once a minute,
        // if the app has grown by 150 MB since it last settled, it settles again.
        var check=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMinutes(1)};check.Tick+=(_,_)=>{if(Private()-settled>150L<<20)Release();};check.Start();settled=Private();
    }
    private static long Private(){using var self=System.Diagnostics.Process.GetCurrentProcess();return self.PrivateMemorySize64;}
    // anyway: settle even if little has grown (the app went back to the tray and will sit there).
    internal static void Release(bool anyway=false){always|=anyway;settle.Stop();settle.Start();}
}
