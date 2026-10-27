using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;

/// The shape of a sound: ffmpeg decodes it once to a low-rate mono stream, and the loudest
/// moment of each slice becomes one bar. Shared by desk cards and the previewer.
internal static class Waveform
{
    internal const int Slices=640;
    private static readonly ConcurrentDictionary<string,Task<float[]?>> known=new(StringComparer.OrdinalIgnoreCase);
    /// Peaks (0–1) for the file, or null when media tools are missing or the file has no audio.
    internal static Task<float[]?> PeaksAsync(string path,string? ffmpeg)
    {
        if(ffmpeg is null||!File.Exists(path))return Task.FromResult<float[]?>(null);
        var info=new FileInfo(path);return known.GetOrAdd($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}",_=>Task.Run(()=>Decode(info.FullName,ffmpeg)));
    }
    private static async Task<float[]?> Decode(string path,string ffmpeg)
    {
        var raw=Path.Combine(Path.GetTempPath(),"Nashi-wave-"+Guid.NewGuid().ToString("N")+".raw");
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await ProcessRunner.RunAsync(ffmpeg,["-v","error","-nostdin","-i",path,"-vn","-ac","1","-ar","2000","-f","f32le","-y",raw],timeout.Token);
            var total=new FileInfo(raw).Length/4;if(total<2)return null;
            var peaks=new float[Slices];var buffer=new byte[4096*4];long n=0;int read;
            await using(var stream=File.OpenRead(raw))
                while((read=await stream.ReadAsync(buffer,timeout.Token))>0)
                    for(var i=0;i+3<read;i+=4,n++){var value=Math.Abs(BitConverter.ToSingle(buffer,i));var slice=(int)(n*Slices/total);if(slice<Slices&&value>peaks[slice]&&float.IsFinite(value))peaks[slice]=value;}
            // Normalise to the loudest moment; a gentle curve keeps quiet passages visible.
            var loudest=Math.Max(.02f,peaks.Max());for(var i=0;i<peaks.Length;i++)peaks[i]=MathF.Pow(Math.Min(1,peaks[i]/loudest),.75f);
            return peaks;
        }
        catch(Exception error)when(error is IOException or OperationCanceledException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception){return null;}
        finally{try{File.Delete(raw);}catch(IOException){}}
    }
    /// A card-sized picture of the waveform in the accent colour, safe to build off the UI thread.
    internal static BitmapSource Picture(float[] peaks,int width,int height)
    {
        var color=Theme.Dark?Color.FromRgb(0xC8,0xE6,0x4C):Color.FromRgb(0x5C,0x7F,0x0E);var stride=width*4;var pixels=new byte[stride*height];
        const int bar=3,gap=2;var bars=Math.Max(1,(width-12)/(bar+gap));var left=(width-bars*(bar+gap)+gap)/2;var middle=height/2.0;
        for(var b=0;b<bars;b++)
        {
            var peak=Slice(peaks,b,bars);var half=Math.Max(1,peak*height*.36);var top=(int)Math.Round(middle-half);var bottom=(int)Math.Round(middle+half);
            for(var y=Math.Max(0,top);y<Math.Min(height,bottom);y++)for(var x=left+b*(bar+gap);x<left+b*(bar+gap)+bar&&x<width;x++){var at=y*stride+x*4;pixels[at]=color.B;pixels[at+1]=color.G;pixels[at+2]=color.R;pixels[at+3]=255;}
        }
        var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,stride);image.Freeze();return image;
    }
    /// The loudest peak among the slices that fall under one bar.
    internal static float Slice(float[] peaks,int bar,int bars)
    {
        var from=bar*peaks.Length/bars;var to=Math.Max(from+1,(bar+1)*peaks.Length/bars);var peak=0f;for(var i=from;i<to&&i<peaks.Length;i++)peak=Math.Max(peak,peaks[i]);return peak;
    }
}

/// The previewer's waveform: bars already played take the accent, the rest stay quiet.
/// Click or drag along it to jump to that moment.
internal sealed class WaveformView:FrameworkElement
{
    private float[]? peaks;private double progress;
    internal float[]? Peaks{get=>peaks;set{peaks=value;InvalidateVisual();}}
    internal double Progress{get=>progress;set{var next=Math.Clamp(double.IsFinite(value)?value:0,0,1);if(Math.Abs(next-progress)<.0005)return;progress=next;InvalidateVisual();}}
    /// Asks to play from this fraction of the sound (0–1).
    internal event Action<double>? Seek;
    internal WaveformView(){Cursor=Cursors.Hand;Focusable=false;MinHeight=80;}
    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));if(peaks is null||ActualWidth<10)return;
        const double bar=3,gap=2;var bars=Math.Max(1,(int)((ActualWidth+gap)/(bar+gap)));var left=(ActualWidth-(bars*(bar+gap)-gap))/2;var middle=ActualHeight/2;
        var played=(Brush)FindResource("Gold");var rest=(Brush)FindResource("Muted");
        for(var b=0;b<bars;b++)
        {
            var half=Math.Max(1.5,Waveform.Slice(peaks,b,bars)*ActualHeight*.46);var x=left+b*(bar+gap);var on=(b+.5)/bars<=progress;
            if(!on)context.PushOpacity(.45);context.DrawRoundedRectangle(on?played:rest,null,new Rect(x,middle-half,bar,half*2),1.5,1.5);if(!on)context.Pop();
        }
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){CaptureMouse();Jump(e);e.Handled=true;}
    protected override void OnMouseMove(MouseEventArgs e){if(IsMouseCaptured&&e.LeftButton==MouseButtonState.Pressed)Jump(e);}
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){if(IsMouseCaptured)ReleaseMouseCapture();}
    private void Jump(MouseEventArgs e){if(ActualWidth<=0)return;var fraction=Math.Clamp(e.GetPosition(this).X/ActualWidth,0,1);Progress=fraction;Seek?.Invoke(fraction);}
}
