using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Nashi.Core;
namespace Nashi.Windows;

/// Registers one global hot key per window layout while window management and its keyboard
/// shortcuts are on. A hot key only reports its own combination; no other keystrokes are read.
internal sealed class WindowShortcutHost:IDisposable
{
    [DllImport("user32.dll")]private static extern bool RegisterHotKey(nint window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]private static extern bool UnregisterHotKey(nint window,int id);
    private const int FirstId=7710;
    private static WindowShortcutHost? current;private static bool recording;
    /// Layout shortcuts pause while Settings records a new combination, so it reaches the recorder.
    internal static bool Recording{get=>recording;set{recording=value;current?.Sync();}}
    private readonly HwndSource source;private readonly WindowManagement manager;private readonly Func<Preferences> preferences;private readonly Func<bool> enabled;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private readonly Dictionary<int,WindowPlacement> registered=[];private string? state;private DateTime retry;
    internal IReadOnlyCollection<WindowPlacement> Registered=>registered.Values;

    internal WindowShortcutHost(WindowManagement manager,Func<Preferences> preferences,Func<bool> enabled)
    {
        this.manager=manager;this.preferences=preferences;this.enabled=enabled;
        // A message-only window receives WM_HOTKEY independently of the main window's visibility.
        source=new HwndSource(new HwndSourceParameters("Nashi Window Shortcuts"){ParentWindow=new nint(-3),WindowStyle=0,Width=0,Height=0});source.AddHook(Hook);
        current=this;timer.Tick+=(_,_)=>Sync();Sync();timer.Start();
    }
    private void Sync()
    {
        var p=preferences();var wanted=new List<(WindowPlacement Placement,WindowShortcut Shortcut)>();
        if(enabled()&&p.WindowShortcutsEnabled&&!recording)foreach(var placement in Enum.GetValues<WindowPlacement>())if(p.Shortcut(placement) is {} shortcut)wanted.Add((placement,shortcut));
        var next=string.Join(';',wanted.Select(x=>x.Placement+"="+x.Shortcut.Modifiers+":"+x.Shortcut.Key));
        // A combination another app already owns is retried every few seconds rather than every tick.
        if(next==state&&(registered.Count==wanted.Count||DateTime.UtcNow<retry))return;
        state=next;Unregister();
        foreach(var (placement,shortcut) in wanted){var id=FirstId+(int)placement;if(RegisterHotKey(source.Handle,id,(uint)(shortcut.Modifiers|0x4000),(uint)shortcut.Key))registered[id]=placement;}
        retry=DateTime.UtcNow.AddSeconds(5);
    }
    private void Unregister(){foreach(var id in registered.Keys)_=UnregisterHotKey(source.Handle,id);registered.Clear();}
    private nint Hook(nint window,int message,nint w,nint l,ref bool handled)
    {
        if(message==0x0312&&registered.TryGetValue((int)w,out var placement)){manager.ArrangeForeground(placement);handled=true;}
        return 0;
    }
    public void Dispose(){timer.Stop();Unregister();if(ReferenceEquals(current,this))current=null;source.RemoveHook(Hook);source.Dispose();}
}
