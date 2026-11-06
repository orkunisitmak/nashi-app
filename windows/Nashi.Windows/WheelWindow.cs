using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;
public sealed record ToolChoice(string Id,string Title,string Symbol,WindowPlacement? Placement=null);
public sealed class WheelWindow:Window
{
    private readonly List<Button> buttons=[];
    private IReadOnlyList<ToolChoice> choices;
    private readonly Action<ToolChoice> select;
    private WheelFace face=null!;
    private int selected=-1;
    internal int SelectedIndex=>selected;
    internal void HoverScreen(Point point)
    {
        var local=face.Canvas.PointFromScreen(point);var delta=local-new Point(WheelFace.Middle,WheelFace.Middle);var distance=delta.Length;face.Aim(local);
        if(distance<WheelFace.Inner||distance>WheelFace.Outer){Highlight(-1);return;}
        var angle=(Math.Atan2(delta.Y,delta.X)*180/Math.PI-WheelPresentation.StartAngle(choices.Count)+360)%360;
        var index=(int)Math.Floor((angle+180d/choices.Count)%360/(360d/choices.Count));
        Highlight(index);
    }
    internal bool Navigate(Key key)
    {
        if(key==Key.Escape){Close();return true;}
        if(buttons.Count==0)return false;
        if(key is Key.Right or Key.Down){Highlight((selected+1)%buttons.Count);buttons[selected].Focus();return true;}
        if(key is Key.Left or Key.Up){Highlight((selected-1+buttons.Count)%buttons.Count);buttons[selected].Focus();return true;}
        if(key is Key.Enter or Key.Space){var choice=choices[Math.Max(0,selected)];Close();select(choice);return true;}
        return false;
    }
    private void Highlight(int index){if(selected==index)return;selected=index;face.Hot(index);if(index>=0)Feedback.Hover(index);}
    private readonly bool externalDrag;private readonly Func<string,IReadOnlyList<ToolChoice>>? modes;private string mode="tools";private readonly Viewbox holder=new(){Stretch=Stretch.Uniform};
    /// Ctrl, Shift and Alt switch between Tools, Convert and File actions (as configured in Settings), like the wheel that opens while dragging.
    internal bool SwitchMode(string modifier,IReadOnlyCollection<string> held)
    {
        if(modes is null)return false;var preferences=WheelPresentation.Preferences;string? best=null;var size=0;
        foreach(var (kind,combo) in new[]{("tools",preferences.ToolsModifier),("convert",preferences.ConvertModifier),("actions",preferences.ActionsModifier)})
        {var set=combo.Split('+');if(set.Contains(modifier)&&set.All(m=>m==modifier||held.Contains(m))&&set.Length>size){best=kind;size=set.Length;}}
        if(best is null||best==mode)return false;var next=modes(best);if(next.Count==0)return false;mode=best;Populate(next);return true;
    }
    internal string Mode=>mode;
    private static string? ModifierName(Key key)=>key switch{Key.LeftCtrl or Key.RightCtrl=>"Ctrl",Key.LeftShift or Key.RightShift=>"Shift",Key.LeftAlt or Key.RightAlt=>"Alt",_=>null};
    private static List<string> HeldModifiers()
    {
        var held=new List<string>();if(Keyboard.IsKeyDown(Key.LeftCtrl)||Keyboard.IsKeyDown(Key.RightCtrl))held.Add("Ctrl");if(Keyboard.IsKeyDown(Key.LeftShift)||Keyboard.IsKeyDown(Key.RightShift))held.Add("Shift");if(Keyboard.IsKeyDown(Key.LeftAlt)||Keyboard.IsKeyDown(Key.RightAlt))held.Add("Alt");return held;
    }
    public WheelWindow(Window? owner,IReadOnlyList<ToolChoice> tools,Action<ToolChoice> select,bool externalDrag=false,Func<string,IReadOnlyList<ToolChoice>>? modes=null,string initialMode="tools")
    {
        mode=initialMode;choices=tools;this.select=select;this.externalDrag=externalDrag;this.modes=modes;if(owner is not null)Owner=owner;Title=externalDrag?"Nashi Window Management":"Nashi tools";Width=WheelPresentation.Diameter*WheelPresentation.Preferences.WheelScale;Height=Width;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=externalDrag?WindowStartupLocation.Manual:WindowStartupLocation.CenterOwner;
        if(externalDrag){ShowActivated=false;Topmost=true;Focusable=false;}
        Content=holder;
        // Glass: what is really under the wheel (Nashi or another app), frosted behind the disc.
        if(WheelPresentation.Clear)Loaded+=async(_,_)=>Lens.SetSupply(this,await Lens.UnderAsync(this));
        Populate(tools);
        PreviewKeyDown+=(_,e)=>{var key=e.Key==Key.System?e.SystemKey:e.Key;if(modes is not null&&ModifierName(key) is {} modifier){var held=HeldModifiers();if(!held.Contains(modifier))held.Add(modifier);if(SwitchMode(modifier,held)){e.Handled=true;return;}}e.Handled=Navigate(e.Key);};
        Loaded+=(_,_)=>{face.Enter();Feedback.Opened();if(!externalDrag&&buttons.Count>0)buttons[0].Focus();};bool closing=false;Closing+=(_,_)=>closing=true;Deactivated+=(_,_)=>{if(!externalDrag&&!closing&&IsVisible)Close();};
    }
    // Builds the wheel for one set of choices; a mode switch calls it again.
    private void Populate(IReadOnlyList<ToolChoice> tools)
    {
        choices=tools.Take(12).ToArray();selected=-1;buttons.Clear();
        var name=externalDrag?"Arrange window":choices.All(t=>t.Id.StartsWith("convert:"))?"Convert":choices.Any(t=>t.Id=="float")?"File actions":"Tools";
        face=new WheelFace(choices,name,externalDrag?"Release to place · Esc cancels":"Esc to close",buttons:true);var canvas=face.Canvas;
        KeyboardNavigation.SetTabNavigation(canvas,KeyboardNavigationMode.Cycle);holder.Child=canvas;
        for(var i=0;i<choices.Count;i++)
        {
            var choice=choices[i];var index=i;var button=(Button)face.Captions[i];buttons.Add(button);var petal=face.Petals[i];
            button.Click+=(_,_)=>{Close();select(choice);};button.MouseEnter+=(_,_)=>Highlight(index);button.GotKeyboardFocus+=(_,_)=>Highlight(index);
            petal.MouseLeftButtonUp+=(_,_)=>{Close();select(choice);};petal.MouseEnter+=(_,_)=>Highlight(index);
        }
        canvas.MouseMove+=(_,e)=>face.Aim(e.GetPosition(canvas));
        var center=new Button{Style=(Style)Application.Current.FindResource("WheelLabelButton"),Focusable=false,Width=WheelFace.HubSize,Height=WheelFace.HubSize,Background=Brushes.Transparent};Canvas.SetLeft(center,WheelFace.Middle-WheelFace.HubSize/2);Canvas.SetTop(center,WheelFace.Middle-WheelFace.HubSize/2);canvas.Children.Add(center);center.Click+=(_,_)=>Close();
        if(IsLoaded){face.Enter();Feedback.Opened();if(!externalDrag&&buttons.Count>0)buttons[0].Focus();}
    }
}
// The wheel's sound, made here in memory: a note per item on a pentatonic scale, so sweeping across plays a quiet arpeggio.
// Each note blooms softly and rings on over the ones before it, like a wind chime. For that every note gets its own waveOut
// stream and Windows mixes them (PlaySound holds one sound and cuts it for the next, which leaves only dry fragments).
internal static class Feedback
{
    internal static bool Enabled{get;set;}=true;
    private static readonly double[] Scale=[523.25,587.33,659.25,783.99,880.00,1046.5,1174.7,1318.5,1568.0,1760.0,2093.0,2349.3];
    private static readonly byte[]?[] notes=new byte[Scale.Length][];private static byte[]? sample;private static DateTime last,opened;
    private const double Level=.018,Ring=.62;
    // Opening and choosing are silent. Opening keeps the first highlight (the focus it opens with) quiet, and gets a stream ready for the first note.
    internal static void Opened(){opened=DateTime.UtcNow;if(Enabled)Sound(null);}
    internal static void Hover(int index)
    {
        var now=DateTime.UtcNow;if(!Enabled||(now-last).TotalMilliseconds<70||(now-opened).TotalMilliseconds<90)return;last=now;var n=(index%Scale.Length+Scale.Length)%Scale.Length;
        Sound(()=>notes[n]??=Wave(Ring,(Scale[n],0)));
    }
    // The "Try" in Settings: the hover notes, a short run up.
    public static void Play(){if(Enabled)Sound(()=>sample??=Wave(Ring+.24,(Scale[0],0),(Scale[2],.12),(Scale[4],.24)));}
    // The UI thread never waits on sound: opening a stream takes 6-35 ms, taking one back up to 10. With a note at most every 70 ms
    // ten streams outlast a note, so a ringing note is not cut; five seconds after the last note they all close.
    private sealed class Voice{internal IntPtr Stream;internal bool Prepared;internal readonly IntPtr Header=Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());}
    private static readonly Voice[] voices=Enumerable.Range(0,10).Select(_=>new Voice()).ToArray();private static int turn;
    private static readonly System.Threading.Timer idle=new(_=>Stop());
    private static void Sound(Func<byte[]>? wave)=>Task.Run(()=>
    {
        lock(voices)try
        {
            if(wave is null){if(voices.All(v=>v.Stream==IntPtr.Zero))Open(voices[0]);}
            else
            {
                var pcm=wave();var voice=voices.FirstOrDefault(v=>v.Stream!=IntPtr.Zero&&(!v.Prepared||(Marshal.PtrToStructure<WaveHeader>(v.Header).Flags&1)!=0))??voices.FirstOrDefault(v=>v.Stream==IntPtr.Zero)??voices[turn++%voices.Length];
                if(voice.Stream==IntPtr.Zero&&!Open(voice))return;Take(voice);
                Marshal.StructureToPtr(new WaveHeader{Data=Marshal.UnsafeAddrOfPinnedArrayElement(pcm,0),Length=(uint)pcm.Length},voice.Header,false);
                if(waveOutPrepareHeader(voice.Stream,voice.Header,Marshal.SizeOf<WaveHeader>())==0){voice.Prepared=true;waveOutWrite(voice.Stream,voice.Header,Marshal.SizeOf<WaveHeader>());}
            }
            idle.Change(5000,System.Threading.Timeout.Infinite);
        }catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
    });
    private static bool Open(Voice voice){var format=new WaveFormat{Tag=1,Channels=2,Rate=Rate,Bytes=Rate*4,Align=4,Bits=16};if(waveOutOpen(out voice.Stream,uint.MaxValue,ref format,IntPtr.Zero,IntPtr.Zero,0)==0)return true;voice.Stream=IntPtr.Zero;return false;}
    private static void Take(Voice voice){if(!voice.Prepared)return;waveOutReset(voice.Stream);waveOutUnprepareHeader(voice.Stream,voice.Header,Marshal.SizeOf<WaveHeader>());voice.Prepared=false;}
    internal static void Stop(){lock(voices)foreach(var voice in voices){if(voice.Stream==IntPtr.Zero)continue;Take(voice);waveOutClose(voice.Stream);voice.Stream=IntPtr.Zero;}}
    private const uint Rate=44100;
    // A stereo 16-bit wave in pinned memory: the notes given, each from its start, added together.
    private static byte[] Wave(double seconds,params (double Frequency,double Start)[] played)
    {
        var count=(int)(seconds*Rate);var data=GC.AllocateArray<byte>(count*4,pinned:true);var span=data.AsSpan();
        for(var i=0;i<count;i++)for(var side=0;side<2;side++)
        {
            var t=i/(double)Rate;double v=0;foreach(var (frequency,start) in played)v+=Note(frequency,t-start,side*2-1);
            BinaryPrimitives.WriteInt16LittleEndian(span[(i*4+side*2)..],(short)Math.Clamp(Math.Round(v*Level*32767),-32767,32767));
        }
        return data;
    }
    // (Against the note, its gain, how long it takes to fall by e.) The note, a soft octave under it, a fifth over it that fades first, a little air.
    private static readonly (double Ratio,double Gain,double Fall)[] Parts=[(1,1,.135),(.5,.3,.17),(1.5,.2,.075),(2,.1,.05)];
    // One note: a 10 ms swell, then each part falling at its own pace. The note itself is the same on both sides, so it stays in tune;
    // what is around it is tuned a hair apart per side, so it shimmers. Parts above 6 kHz are left out.
    private static double Note(double frequency,double t,int side)
    {
        if(t<0||t>=Ring)return 0;double v=0;
        foreach(var (ratio,gain,fall) in Parts)if(frequency*ratio<6000)v+=gain*Math.Sin(2*Math.PI*(frequency*ratio+(ratio==1?0:side*1.3))*t)*Math.Exp(-t/fall);
        var swell=Math.Sin(Math.Min(1,t/.01)*Math.PI/2);return v*swell*swell*Math.Min(1,(Ring-t)/.03);
    }
    [StructLayout(LayoutKind.Sequential)]private struct WaveFormat{public ushort Tag,Channels;public uint Rate,Bytes;public ushort Align,Bits,Extra;}
    [StructLayout(LayoutKind.Sequential)]private struct WaveHeader{public IntPtr Data;public uint Length,Recorded;public IntPtr User;public uint Flags,Loops;public IntPtr Next,Reserved;}
    [DllImport("winmm.dll")]private static extern int waveOutOpen(out IntPtr stream,uint device,ref WaveFormat format,IntPtr callback,IntPtr instance,uint flags);
    [DllImport("winmm.dll")]private static extern int waveOutPrepareHeader(IntPtr stream,IntPtr header,int size);
    [DllImport("winmm.dll")]private static extern int waveOutUnprepareHeader(IntPtr stream,IntPtr header,int size);
    [DllImport("winmm.dll")]private static extern int waveOutWrite(IntPtr stream,IntPtr header,int size);
    [DllImport("winmm.dll")]private static extern int waveOutReset(IntPtr stream);
    [DllImport("winmm.dll")]private static extern int waveOutClose(IntPtr stream);
}
