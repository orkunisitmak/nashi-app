using System.Runtime.InteropServices;
using System.Windows;
using Nashi.Core;
namespace Nashi.Windows;

/// Nashi's sound effects. Every sound is synthesized (or decoded) once at startup into memory,
/// then mixed by a small engine on its own thread: one output stream that stays open while Nashi
/// is in use, so the first sound is never cut short and several sounds can overlap without
/// stopping each other.
internal static class Sounds
{
    private static Preferences Value=>WheelPresentation.Preferences;
    private static readonly Random random=new();
    private static readonly int[] last=[-1,-1,-1];
    private static float[][] pages=[],successes=[],failures=[];private static float[]? click;
    private static readonly Lazy<bool> ready=new(()=>{Load();return true;});
    /// Builds every sound and opens the output, off the UI thread; called once at startup.
    internal static void Start(){_=Task.Run(()=>{_=ready.Value;Mixer.Wake();});}
    /// Keeps the output warm while a Nashi window is in front.
    internal static void Wake()=>Mixer.Wake();
    internal static void Stop()=>Mixer.Shutdown();
    internal static void Page(){if(Value.PageSounds)Play(Pick(pages,0),.9);}
    internal static void Success()=>Play(Pick(successes,1),1);
    internal static void Failure()=>Play(Pick(failures,2),1);
    internal static void Click(){if(click is not null||ready.Value)Play(click,.65,always:true);}
    /// Settings › Listen: plays even with a sound switched off, at the chosen volume.
    internal static void Preview(string kind)
    {
        _=ready.Value;switch(kind){case "page":Play(Pick(pages,0),.9,true);break;case "failure":Play(Pick(failures,2),1,true);break;case "click":Play(click,.65,true);break;default:Play(Pick(successes,1),1,true);break;}
    }
    /// A different variation each time, never the same one twice in a row.
    private static float[]? Pick(float[][] set,int slot)
    {
        _=ready.Value;if(set.Length==0)return null;var n=random.Next(set.Length);if(set.Length>1&&n==last[slot])n=(n+1+random.Next(set.Length-1))%set.Length;last[slot]=n;return set[n];
    }
    private static void Play(float[]? samples,double gain,bool preview=false,bool always=false)
    {
        if(samples is null||Value.SoundVolume<=0||(!preview&&!always&&!Value.SoundEffects))return;
        Mixer.Play(samples,(float)(gain*Value.SoundVolume));
    }
    private static void Load()
    {
        pages=[Synthesize.Tap(),Synthesize.Glide(),Synthesize.Drop(),Synthesize.Step()];
        successes=[Synthesize.Arpeggio(),Synthesize.Ding(),Synthesize.Sparkle()];
        failures=[Synthesize.Fall(),Synthesize.Bump()];
        try{using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/WheelSelection.wav"))?.Stream;if(stream is not null)click=Wav.Read(stream);}
        catch(Exception error)when(error is IOException or InvalidDataException or InvalidOperationException){}
    }

    /// Reads a PCM WAV into the mixer's format (48 kHz stereo float), resampling if needed.
    private static class Wav
    {
        internal static float[]? Read(Stream stream)
        {
            using var reader=new BinaryReader(stream);string Id()=>System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));if(Id()!="RIFF")return null;reader.ReadInt32();if(Id()!="WAVE")return null;
            int channels=0,rate=0,bits=0;byte[]? data=null;
            while(stream.Position+8<=stream.Length)
            {
                var id=Id();var size=reader.ReadInt32();if(size<0)return null;var next=stream.Position+size+(size&1);
                if(id=="fmt "){var format=reader.ReadInt16();channels=reader.ReadInt16();rate=reader.ReadInt32();reader.ReadInt32();reader.ReadInt16();bits=reader.ReadInt16();if(format!=1)return null;}
                else if(id=="data")data=reader.ReadBytes(size);
                stream.Position=Math.Min(next,stream.Length);
            }
            if(data is null||bits!=16||channels is not (1 or 2)||rate<=0)return null;
            var frames=data.Length/(2*channels);var source=new float[frames*2];
            for(var n=0;n<frames;n++){var l=BitConverter.ToInt16(data,n*2*channels)/32768f;var r=channels==2?BitConverter.ToInt16(data,n*4+2)/32768f:l;source[n*2]=l;source[n*2+1]=r;}
            if(rate==Mixer.Rate)return source;
            var length=(int)((long)frames*Mixer.Rate/rate);var result=new float[length*2];
            for(var n=0;n<length;n++){var at=n*(double)rate/Mixer.Rate;var i=(int)at;var f=(float)(at-i);var j=Math.Min(i+1,frames-1);for(var c=0;c<2;c++)result[n*2+c]=source[i*2+c]*(1-f)+source[j*2+c]*f;}
            return result;
        }
    }

    /// Soft, tonal sounds: sine partials with gentle attacks and smooth decays, no noise.
    private static class Synthesize
    {
        private const double Rate=Mixer.Rate;
        /// Renders a mono shape to stereo, with a whisper of width so it does not sit dead centre.
        private static float[] Render(double seconds,Func<double,double> wave)
        {
            var frames=(int)(seconds*Rate);var result=new float[frames*2];var delay=(int)(Rate*.00035);var mono=new float[frames];
            for(var n=0;n<frames;n++){var fade=Math.Min(1,(frames-n)/(Rate*.012));mono[n]=(float)(Math.Clamp(wave(n/Rate),-1,1)*fade);}
            for(var n=0;n<frames;n++){result[n*2]=mono[n];result[n*2+1]=n>=delay?mono[n-delay]*.96f:0;}
            return result;
        }
        private static double Envelope(double t,double attack,double decay)=>t<0?0:(t<attack?Math.Sin(Math.PI/2*t/attack):1)*Math.Exp(-t/decay);
        /// A rounded mallet note: the fundamental, a soft octave and a faint fourth partial.
        private static double Mallet(double t,double hz,double decay)=>t<0?0:Envelope(t,.005,decay)*(Math.Sin(2*Math.PI*hz*t)+.18*Math.Sin(4*Math.PI*hz*t)*Math.Exp(-t/(decay*.5))+.05*Math.Sin(8*Math.PI*hz*t)*Math.Exp(-t/(decay*.25)));
        /// A clear bell: sine with a quiet, quickly fading shimmer above it.
        private static double Bell(double t,double hz,double decay)=>t<0?0:Envelope(t,.004,decay)*(Math.Sin(2*Math.PI*hz*t)+.12*Math.Sin(2*Math.PI*hz*2.01*t)*Math.Exp(-t/(decay*.4))+.04*Math.Sin(2*Math.PI*hz*3*t)*Math.Exp(-t/(decay*.2)));
        /// A pitch that slides smoothly from one frequency to another, phase-continuous.
        private static Func<double,double> Slide(double from,double to,double over,double decay,double attack=.012)
        {
            double phase=0,previous=0;
            return t=>{var p=Math.Clamp(t/over,0,1);var eased=1-Math.Pow(1-p,3);var hz=from+(to-from)*eased;phase+=2*Math.PI*hz*(t-previous);previous=t;return Envelope(t,attack,decay)*(Math.Sin(phase)+.08*Math.Sin(2*phase));};
        }
        // Page changes: four quiet variations.
        internal static float[] Tap()=>Render(.26,t=>.24*Mallet(t,784,.07));
        internal static float[] Glide(){var wave=Slide(520,780,.11,.06,.018);return Render(.24,t=>.2*wave(t));}
        internal static float[] Drop(){var wave=Slide(620,1040,.05,.045,.006);return Render(.22,t=>.2*wave(t));}
        internal static float[] Step()=>Render(.3,t=>.17*(Mallet(t,659.3,.06)+.8*Mallet(t-.055,880,.07)));
        // A job finished: three bright, gentle variations.
        internal static float[] Arpeggio()=>Render(.9,t=>.16*(Bell(t,1046.5,.22)+.9*Bell(t-.075,1318.5,.24)+.85*Bell(t-.15,1568,.32)));
        internal static float[] Ding()=>Render(.85,t=>.2*(Bell(t,1174.7,.3)+.55*Bell(t,1760,.22)));
        internal static float[] Sparkle()=>Render(.8,t=>.15*(Bell(t,1318.5,.2)+.9*Bell(t-.06,1975.5,.26)+.4*Bell(t-.06,987.8,.3)));
        // Something went wrong: low, rounded and short, never harsh.
        internal static float[] Fall()=>Render(.55,t=>.22*(Mallet(t,523.3,.12)+.95*Mallet(t-.12,392,.18)));
        internal static float[] Bump(){var wave=Slide(330,250,.18,.14,.008);return Render(.45,t=>.26*wave(t));}
    }

    /// One waveOut stream fed by a mixer thread. Voices are summed, so overlapping sounds all play.
    /// The stream stays open while Nashi is used and closes after a quiet spell, so it neither
    /// keeps the PC awake nor misses the start of the next sound (a reopened stream begins with silence).
    private static class Mixer
    {
        internal const int Rate=48000;
        private const int Frames=960,Buffers=4,Lead=3;
        private static readonly TimeSpan IdleClose=TimeSpan.FromSeconds(90);
        private sealed class Voice(float[] samples,float gain,int delay){public readonly float[] Samples=samples;public readonly float Gain=gain;public int Position=-delay*2;}
        private static readonly List<Voice> voices=[];private static readonly object gate=new();
        private static readonly AutoResetEvent signal=new(false),wake=new(false);
        private static Thread? thread;private static volatile bool quit;private static DateTime used=DateTime.UtcNow;
        private static IntPtr device;private static readonly IntPtr[] headers=new IntPtr[Buffers],data=new IntPtr[Buffers];private static DateTime opened;
        private static readonly int headerSize=Marshal.SizeOf<WaveHeader>(),flagsOffset=(int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags));
        private static readonly float[] mix=new float[Frames*2];private static readonly short[] pcm=new short[Frames*2];
        internal static void Play(float[] samples,float gain)
        {
            lock(gate){if(voices.Count>=16)voices.RemoveAt(0);voices.Add(new(samples,gain,device==IntPtr.Zero||DateTime.UtcNow-opened<TimeSpan.FromMilliseconds(250)?Frames*Lead/2:0));used=DateTime.UtcNow;}
            Ensure();wake.Set();
        }
        internal static void Wake(){lock(gate)used=DateTime.UtcNow;Ensure();wake.Set();}
        internal static void Shutdown(){quit=true;wake.Set();signal.Set();thread?.Join(500);}
        private static void Ensure()
        {
            lock(gate){if(thread is not null||quit)return;thread=new Thread(Run){IsBackground=true,Name="Nashi sound",Priority=ThreadPriority.AboveNormal};thread.Start();}
        }
        private static void Run()
        {
            while(!quit)
            {
                if(device==IntPtr.Zero&&!Open()){wake.WaitOne(2000);continue;}
                signal.WaitOne(30);
                for(var n=0;n<Buffers&&!quit;n++)
                {
                    if((Marshal.ReadInt32(headers[n],flagsOffset)&Done)==0)continue;
                    Fill();Marshal.Copy(pcm,0,data[n],pcm.Length);
                    if(waveOutWrite(device,headers[n],headerSize)!=0){Close();break;}
                }
                bool idle;lock(gate)idle=voices.Count==0&&DateTime.UtcNow-used>IdleClose;
                if(idle){Close();wake.WaitOne();}
            }
            Close();
        }
        private static bool Open()
        {
            var format=new WaveFormat{Tag=1,Channels=2,Rate=Rate,BytesPerSecond=Rate*4,BlockAlign=4,Bits=16};
            if(waveOutOpen(out device,WaveMapper,ref format,signal.SafeWaitHandle.DangerousGetHandle(),IntPtr.Zero,CallbackEvent)!=0){device=IntPtr.Zero;return false;}
            for(var n=0;n<Buffers;n++)
            {
                data[n]=Marshal.AllocHGlobal(Frames*4);headers[n]=Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new WaveHeader{Data=data[n],Length=Frames*4},headers[n],false);waveOutPrepareHeader(device,headers[n],headerSize);
                // Start every buffer with silence: the device wakes up on that, not on the sound.
                Array.Clear(pcm);Marshal.Copy(pcm,0,data[n],pcm.Length);waveOutWrite(device,headers[n],headerSize);
            }
            opened=DateTime.UtcNow;return true;
        }
        private static void Close()
        {
            if(device==IntPtr.Zero)return;waveOutReset(device);
            for(var n=0;n<Buffers;n++){if(headers[n]!=IntPtr.Zero){waveOutUnprepareHeader(device,headers[n],headerSize);Marshal.FreeHGlobal(headers[n]);headers[n]=IntPtr.Zero;}if(data[n]!=IntPtr.Zero){Marshal.FreeHGlobal(data[n]);data[n]=IntPtr.Zero;}}
            waveOutClose(device);device=IntPtr.Zero;
        }
        /// Sums the playing voices into the next buffer; a soft limiter keeps overlaps from clipping.
        private static void Fill()
        {
            Array.Clear(mix);
            lock(gate)
            {
                for(var v=voices.Count-1;v>=0;v--)
                {
                    var voice=voices[v];var samples=voice.Samples;
                    for(var i=0;i<mix.Length;i++){var at=voice.Position+i;if(at<0)continue;if(at>=samples.Length)break;mix[i]+=samples[at]*voice.Gain;}
                    voice.Position+=mix.Length;if(voice.Position>=samples.Length)voices.RemoveAt(v);
                }
            }
            for(var i=0;i<mix.Length;i++){var x=mix[i];var y=x>.6f||x<-.6f?Math.Sign(x)*(.6f+.4f*MathF.Tanh((MathF.Abs(x)-.6f)/.4f)):x;pcm[i]=(short)(Math.Clamp(y,-1f,1f)*32767);}
        }
        private const uint WaveMapper=0xFFFFFFFF,CallbackEvent=0x00050000;private const int Done=1;
        [StructLayout(LayoutKind.Sequential,Pack=2)]private struct WaveFormat{public short Tag,Channels;public int Rate,BytesPerSecond;public short BlockAlign,Bits,Extra;}
        [StructLayout(LayoutKind.Sequential)]private struct WaveHeader{public IntPtr Data;public int Length,Recorded;public IntPtr User;public int Flags,Loops;public IntPtr Next,Reserved;}
        [DllImport("winmm.dll")]private static extern int waveOutOpen(out IntPtr device,uint id,ref WaveFormat format,IntPtr callback,IntPtr instance,uint flags);
        [DllImport("winmm.dll")]private static extern int waveOutPrepareHeader(IntPtr device,IntPtr header,int size);
        [DllImport("winmm.dll")]private static extern int waveOutUnprepareHeader(IntPtr device,IntPtr header,int size);
        [DllImport("winmm.dll")]private static extern int waveOutWrite(IntPtr device,IntPtr header,int size);
        [DllImport("winmm.dll")]private static extern int waveOutReset(IntPtr device);
        [DllImport("winmm.dll")]private static extern int waveOutClose(IntPtr device);
    }
}
