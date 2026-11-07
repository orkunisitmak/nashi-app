using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace Nashi.Windows;

// The media editors' frame. The picture (or, for a sound, its waveform) fills the left, the editor's decision sits on
// the right, the timeline runs across the bottom; nothing lies over the picture. The playhead lives in the timeline,
// and what an editor does is previewed live where it can be: speed, silence, balance and volume, covered areas and bleeps.
public sealed partial class ToolEditor
{
    private Border? optionsPanel;private Button? playButton;private bool showingPause,covering;private Border? mediaCapsule;
    private readonly Grid mediaBar=new();private readonly StackPanel barActions=new(){Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
    // The result, said in the panel under the choice that makes it: a line, and a quieter second one.
    private readonly TextBlock outcome=new(){FontSize=13.5,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new(0),Style=null},outcomeDetail=new(){FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new(0,2,0,0),Style=null};
    private Border? resultCard;
    private readonly TextBlock barHint=new(){FontSize=11.5,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,3,0,0)};
    // A sound with nothing to show: its waveform is the canvas (Audio to Video shows the picture it will make).
    private bool SoundStage=>media is {Video:null}&&tool!="audioToVideo";
    internal static string Stamp(double seconds){var time=TimeSpan.FromSeconds(Math.Max(0,seconds));return time.TotalHours>=1?time.ToString(@"h\:mm\:ss\.ff",CultureInfo.CurrentCulture):time.ToString(@"m\:ss\.ff",CultureInfo.CurrentCulture);}
    private void BuildMediaBar(DockPanel previewArea)
    {
        // The picture sits between the title and the timeline, beside the panel, never under them.
        Grid.SetRow(stage,1);Grid.SetRowSpan(stage,1);previewArea.Margin=new(18,4,18,6);
        if(optionsPanel is {} panel){panel.VerticalAlignment=VerticalAlignment.Top;panel.Margin=new(0,4,18,6);panel.MaxHeight=double.PositiveInfinity;panel.SizeChanged+=(_,_)=>FitBeside(previewArea);panel.IsVisibleChanged+=(_,_)=>FitBeside(previewArea);}
        var transport=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        transport.Children.Add(DockIcon("back","Back 1 second",()=>Seek(player.Position.TotalSeconds-1)));playButton=DockIcon("play","Play / pause",TogglePlayback);transport.Children.Add(playButton);transport.Children.Add(DockIcon("fwd","Forward 1 second",()=>Seek(player.Position.TotalSeconds+1)));
        L10n.Bind(playButton,ToolTipProperty,"Space play/pause · ←/→ 1s · Shift+←/→ 1 frame (video), 0.1s (audio) · I/O set range · Enter save");
        // The scrubber stays for the keyboard (arrows move it when it has focus); the timeline draws the playhead and takes the pointer.
        var face=new FrameworkElementFactory(typeof(Border));face.SetValue(Border.BackgroundProperty,Brushes.Transparent);scrubber.Template=new ControlTemplate(typeof(System.Windows.Controls.Slider)){VisualTree=face};
        scrubber.IsHitTestVisible=false;scrubber.Margin=new(12,0,12,0);scrubber.SmallChange=media?.Video is {} video?1/Math.Max(1,video.FrameRate):.1;scrubber.LargeChange=1;
        // Moving through the film while it plays pauses it until the pointer lets go, and the picture follows at a pace the
        // decoder can keep (a seek at most every 60 ms, the last one always), so neither the sound nor the picture stutters.
        timeline.Seeked+=seconds=>{if(processedPlayback)return;SeekSoon(seconds);};seekLater.Tick+=(_,_)=>FlushSeek();
        timeline.GrabStarted+=()=>{if(playing&&!processedPlayback){resumeAfterGrab=true;player.Pause();playing=false;}};
        timeline.GrabEnded+=()=>{FlushSeek();if(resumeAfterGrab){resumeAfterGrab=false;player.Play();playing=true;}};
        timeline.VerticalAlignment=VerticalAlignment.Stretch;timeline.Height=double.NaN;
        var lane=new Grid{Height=SoundStage?232:62,VerticalAlignment=VerticalAlignment.Center};lane.Children.Add(timeline);lane.Children.Add(scrubber);
        rangeText.FontSize=12.5;rangeText.FontWeight=FontWeights.SemiBold;rangeText.SetResourceReference(TextBlock.ForegroundProperty,"Ink");selectionText.FontSize=12;selectionText.Margin=new(8,0,0,0);selectionText.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        time.FontSize=12;time.FontWeight=FontWeights.SemiBold;time.TextWrapping=TextWrapping.NoWrap;time.TextTrimming=TextTrimming.CharacterEllipsis;time.MaxWidth=260;time.Margin=new(0);time.Text=Position();barHint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var readout=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(10,0,10,0),MinWidth=150};
        if(tool is "trimVideo" or "trimAudio"){var line=new StackPanel{Orientation=Orientation.Horizontal};line.Children.Add(rangeText);line.Children.Add(selectionText);readout.Children.Add(line);time.SetResourceReference(TextBlock.ForegroundProperty,"Muted");time.FontWeight=FontWeights.Normal;time.Margin=new(0,3,0,0);readout.Children.Add(time);}
        else{time.SetResourceReference(TextBlock.ForegroundProperty,"Ink");readout.Children.Add(time);readout.Children.Add(barHint);}
        var row=new Grid();foreach(var width in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})row.ColumnDefinitions.Add(new(){Width=width});
        void Put(UIElement element,int column){Grid.SetColumn(element,column);row.Children.Add(element);}
        Put(transport,0);Put(DockDivider(),1);Put(readout,3);Put(barActions,4);
        if(SoundStage)
        {
            // The waveform is the canvas: large, with the time under it.
            timeline.Ruler=true;var sound=new Grid{VerticalAlignment=VerticalAlignment.Center};sound.Children.Add(player);sound.Children.Add(lane);previewArea.Children.Add(sound);
        }
        else{Put(lane,2);lane.Margin=new(6,0,6,0);}
        // Audio to Video shows a still of the video it will make, in its frame (the made video plays in the player under it).
        if(tool=="audioToVideo"){var holder=new Grid();holder.Children.Add(player);visualFrame.Children.Add(visualPreview);visualFit.Child=visualFrame;holder.Children.Add(visualFit);previewArea.Children.Add(holder);}
        mediaBar.Children.Add(row);
    }
    private readonly Grid visualFrame=new(){Background=Brushes.Black};private readonly Viewbox visualFit=new(){Stretch=Stretch.Uniform};
    private Border MediaBarCapsule()
    {
        mediaCapsule=Ui.GlassCapsule(mediaBar,"GlassDock",26,"dock");mediaCapsule.Padding=new(8);mediaCapsule.Margin=new(18,0,18,0);mediaCapsule.HorizontalAlignment=HorizontalAlignment.Stretch;
        if(mediaCapsule.Child is Grid host&&host.Children.OfType<Border>().LastOrDefault() is {} face)face.Padding=new(10,6,14,6);
        return mediaCapsule;
    }
    // The picture keeps clear of the panel beside it.
    private void FitBeside(DockPanel previewArea)
    {
        var width=optionsPanel is {IsVisible:true} panel?panel.ActualWidth+30:0;previewArea.Margin=new(18,4,18+width,6);
    }
    private Border ResultCard()
    {
        var label=L10n.Label(new TextBlock{Text="Result",FontSize=11,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,3),Style=null});label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        outcome.SetResourceReference(TextBlock.ForegroundProperty,"Ink");outcomeDetail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");var stack=new StackPanel();stack.Children.Add(label);stack.Children.Add(outcome);stack.Children.Add(outcomeDetail);
        resultCard=new RoundBorder{Radius=14,Padding=new(14,10,14,11),Margin=new(0,14,0,0),Child=stack};resultCard.SetResourceReference(Border.BackgroundProperty,"Sel");Outcome(outcome.Text,outcomeDetail.Text);return resultCard;
    }
    private void Outcome(string text,string detail="")
    {
        outcome.Text=text;outcomeDetail.Text=detail;outcomeDetail.Visibility=detail.Length>0?Visibility.Visible:Visibility.Collapsed;if(resultCard is not null)resultCard.Visibility=text.Length>0?Visibility.Visible:Visibility.Collapsed;
    }
    private bool resumeAfterGrab;private double? pendingSeek;private DateTime lastSeek;private readonly System.Windows.Threading.DispatcherTimer seekLater=new(){Interval=TimeSpan.FromMilliseconds(60)};
    private void SeekSoon(double seconds)
    {
        pendingSeek=seconds;timeline.Position=Math.Clamp(seconds,0,duration);
        if((DateTime.UtcNow-lastSeek).TotalMilliseconds>=60)FlushSeek();else if(!seekLater.IsEnabled)seekLater.Start();
    }
    private void FlushSeek(){seekLater.Stop();if(pendingSeek is not {} at)return;pendingSeek=null;lastSeek=DateTime.UtcNow;Seek(at);}
    // The frames to save: the ones added, or, with none added, the frame on screen.
    private double[] ChosenFrames()
    {
        var times=fields["Times (seconds)"].Text.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(s=>NumericInput.ParseFinite(s)).ToArray();
        return times.Length>0?times:[Math.Round(Math.Clamp(player.Position.TotalSeconds,0,Math.Max(0,duration-.05)),2)];
    }
    // Plays one part and stops at its end.
    private void PlayPart(double start,double end){Seek(start);rangePlaybackEnd=end;playingRange=true;player.Play();playing=true;}
    // Each tick: the playhead, the play button's face, and what the editor previews live.
    private void MediaTick(bool open)
    {
        if(media is null)return;
        if(playButton is not null&&showingPause!=playing){showingPause=playing;playButton.Content=Icons.Make(playing?"pause":"play",17);}
        if(!open)return;var now=player.Position.TotalSeconds;
        if(!timeline.IsMouseCaptured)timeline.Position=processedPlayback?double.NaN:now;
        // The still stays as the cover until the moving preview is played or moved through (the very start is often silent, so black).
        if(tool=="audioToVideo")visualFit.Visibility=visualLive&&(playing||now>.01)?Visibility.Collapsed:Visibility.Visible;
        if(tool=="videoSnapshots"&&fields["Times (seconds)"].Text.Trim().Length==0)Outcome(L10n.Text("1 frame · PNG"),string.Format(L10n.Text("The frame on screen, at {0}"),Stamp(now)));
        CoverSound(now);CoverPicture(now);
    }
    // Bleep Audio, live: inside a covered part the sound goes quiet, and a bleep plays over it.
    // A tone lasts at most Tone.Longest seconds; a longer covered part starts it again.
    private double toneUntil;
    private void CoverSound(double now)
    {
        if(tool!="redactAudio")return;var inside=playing&&!processedPlayback?bleepRanges.FirstOrDefault(r=>now>=r.Start&&now<r.End):null;
        if(inside is not null&&(!covering||now>=toneUntil)){if(!covering){covering=true;player.IsMuted=true;}if(bleepMode.SelectedItem as string!="mute"){Tone.Play(inside.End-now);toneUntil=now+Tone.Longest;}}
        else if(inside is null&&covering){covering=false;player.IsMuted=false;Tone.Stop();}
    }
    // Redact Video, live: an area shows its cover while the playhead is inside its time, and only an outline otherwise.
    private int chosenArea=-1;private double lastCover=double.NaN;private string lastStyle="";
    private void CoverPicture(double now)
    {
        if(tool!="redactVideo"||bitmap is null)return;var style=redactMode.SelectedItem as string??"solid";var moved=Math.Abs(now-lastCover)>.02||style!=lastStyle;lastCover=now;lastStyle=style;
        var boxes=drawing.Children.OfType<Rectangle>().ToArray();var unit=Math.Max(1,drawing.Width/600);
        for(var n=0;n<videoRegions.Count&&n<boxes.Length;n++)
        {
            var region=videoRegions[n];var box=boxes[n];var on=now>=region.Start-.001&&now<=(region.End??duration)+.001;
            box.Stroke=n==chosenArea?Theme.Brush("Coral"):Brushes.White;box.StrokeThickness=(n==chosenArea?3:1.5)*unit;box.StrokeDashArray=on?null:new DoubleCollection{4,3};
            if(!on){box.Fill=Brushes.Transparent;continue;}
            if(style=="solid"){box.Fill=Brushes.Black;continue;}
            if(moved||box.Fill is not ImageBrush){box.Fill=Cover(region,style=="blur");RenderOptions.SetBitmapScalingMode(box,style=="blur"?BitmapScalingMode.HighQuality:BitmapScalingMode.NearestNeighbor);}
        }
    }
    // The video under an area as the export covers it: in 16-pixel blocks, or blurred (drawn small and soft, then
    // stretched; the outline stays sharp). Drawn again whenever the playhead moves.
    private Brush Cover(Nashi.Core.MediaRegion region,bool blur)
    {
        var scale=blur?.25:1/16d;var columns=(int)Math.Max(1,Math.Round(region.Width*scale));var rows=(int)Math.Max(1,Math.Round(region.Height*scale));var bleed=blur?6:0;var visual=new DrawingVisual();
        if(blur)visual.Effect=new BlurEffect{Radius=15,KernelType=KernelType.Gaussian};
        using(var dc=visual.RenderOpen())dc.DrawRectangle(new VisualBrush(player){Viewbox=new Rect(region.X-bleed/scale,region.Y-bleed/scale,region.Width+2*bleed/scale,region.Height+2*bleed/scale),ViewboxUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill},null,new Rect(-bleed,-bleed,columns+2*bleed,rows+2*bleed));
        var pixels=new RenderTargetBitmap(columns,rows,96,96,PixelFormats.Pbgra32);pixels.Render(visual);pixels.Freeze();return new ImageBrush(pixels){Stretch=Stretch.Fill};
    }
}

// The bleep heard in a covered part while previewing: a 1 kHz tone, as the export writes it.
internal static class Tone
{
    [DllImport("winmm.dll")]private static extern bool PlaySound(IntPtr sound,IntPtr module,uint flags);
    private static byte[]? playing;internal const double Longest=30;
    internal static void Play(double seconds)
    {
        const int rate=22050;var samples=(int)(Math.Clamp(seconds,.05,Longest)*rate);var wave=GC.AllocateArray<byte>(44+samples*2,pinned:true);
        using(var writer=new BinaryWriter(new MemoryStream(wave)))
        {
            writer.Write("RIFF"u8);writer.Write(36+samples*2);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8);writer.Write(samples*2);
            for(var n=0;n<samples;n++){var fade=Math.Min(1,Math.Min(n,samples-n)/(rate*.004));writer.Write((short)(Math.Sin(2*Math.PI*1000*n/rate)*.18*fade*short.MaxValue));}
        }
        // The tone playing now stays alive until the new one has replaced it.
        var previous=playing;playing=wave;try{PlaySound(Marshal.UnsafeAddrOfPinnedArrayElement(wave,0),IntPtr.Zero,0x7);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}GC.KeepAlive(previous);
    }
    internal static void Stop(){try{PlaySound(IntPtr.Zero,IntPtr.Zero,0);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}playing=null;}
}
