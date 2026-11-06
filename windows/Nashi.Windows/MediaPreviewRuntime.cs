using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class ToolEditor
{
    private readonly CancellationTokenSource previewLifetime=new();private Toolchain? previewTools;private string? previewDirectory,originalPreviewPath;private bool triedProxy;
    internal void ConfigureMediaRuntime(Toolchain tools){previewTools=tools;if(media is not null&&tools.Ready)Loaded+=async(_,_)=>await LoadTimelineArtwork();}
    private string PreviewDirectory()=>previewDirectory??=Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),"Nashi-preview-"+Guid.NewGuid().ToString("N"))).FullName;
    internal async Task CreatePreviewProxy()
    {
        if(processedPlayback||processingPreview){ShowPreviewNotice(L10n.Text("Processed preview unavailable for this Windows codec."));return;}
        if(triedProxy||previewTools?.Ready!=true){ShowPreviewNotice(L10n.Text("This Windows codec cannot play directly. Set up media tools to create a local preview."));return;}triedProxy=true;ShowPreviewNotice(L10n.Text("Preparing a preview this computer can play…"));
        try
        {
            var output=Path.Combine(PreviewDirectory(),media?.Video is null?"preview.wav":"preview.mp4");var args=new List<string>{"-v","error","-nostdin","-i",input};if(media?.Video is not null)args.AddRange(["-vf","scale=w='min(960,iw)':h=-2","-c:v","libx264","-preset","ultrafast","-crf","28","-pix_fmt","yuv420p","-c:a","aac","-movflags","+faststart"]);else args.AddRange(["-vn","-c:a","pcm_s16le"]);args.Add(output);
            await ProcessRunner.RunAsync(previewTools.Executable("ffmpeg"),args,previewLifetime.Token);if(previewLifetime.IsCancellationRequested)return;originalPreviewPath=output;if(processedPlayback||processingPreview)return;player.Source=new Uri(output);player.Play();player.Pause();playing=false;Seek(0);ShowPreviewNotice(null);time.Text=L10n.Text("Preview ready · export uses your original file.");
        }catch(OperationCanceledException){}catch(Exception error){if(IsVisible)ShowPreviewNotice(L10n.Text("Preview unavailable")+" · "+error.Message);}
    }
    // What the preview is doing, said in the middle of the stage (null hides it).
    private Border? previewNotice;
    private void ShowPreviewNotice(string? text)
    {
        if(text is null){if(previewNotice is not null)previewNotice.Visibility=Visibility.Collapsed;return;}
        if(previewNotice is null)
        {
            var label=new TextBlock{FontSize=13,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,MaxWidth=380};label.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
            previewNotice=new RoundBorder{Radius=16,Padding=new(18,12,18,12),Child=label,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};previewNotice.SetResourceReference(Border.BackgroundProperty,"GlassStrong");previewNotice.SetResourceReference(EffectProperty,"PanelShadow");stage.Children.Add(previewNotice);
        }
        previewNotice.Margin=new(0,0,optionsPanel is {IsVisible:true} beside&&(media is not null||tool=="splitPDF")?beside.ActualWidth+30:0,0);
        ((TextBlock)previewNotice.Child).Text=text;previewNotice.Visibility=Visibility.Visible;
    }
    // The timeline's film: frames at their own shape across the video (a few at a time), or the sound's waveform.
    private async Task LoadTimelineArtwork()
    {
        if(previewTools is null||media is null)return;var ffmpeg=previewTools.Executable("ffmpeg");try
        {
            if(media.Video is not null)
            {
                const int count=14;var frames=new BitmapSource?[count];using var gate=new SemaphoreSlim(3);
                await Task.WhenAll(Enumerable.Range(0,count).Select(async n=>
                {
                    await gate.WaitAsync(previewLifetime.Token);try
                    {
                        var frame=Path.Combine(PreviewDirectory(),$"film-{n}.png");var at=Math.Max(0,duration-.05)*(n+.5)/count;
                        await ProcessRunner.RunAsync(ffmpeg,["-v","error","-ss",at.ToString(CultureInfo.InvariantCulture),"-i",input,"-frames:v","1","-vf","scale=-2:96",frame],previewLifetime.Token);if(File.Exists(frame))frames[n]=ImageEngine.Load(frame);
                    }
                    finally{gate.Release();}
                }));
                if(!previewLifetime.IsCancellationRequested&&frames.All(f=>f is not null))timeline.SetArtwork(frames!);
            }
            else
            {
                // A tall, full waveform for the canvas; ink on light, light on dark.
                var frame=Path.Combine(PreviewDirectory(),"waveform.png");var size=SoundStage?"1800x420":"1200x140";var color=Theme.Dark?"0xd9d7d2":"0x34363b";
                await ProcessRunner.RunAsync(ffmpeg,["-v","error","-i",input,"-filter_complex",$"aformat=channel_layouts=mono,showwavespic=s={size}:scale=sqrt:colors={color}","-frames:v","1",frame],previewLifetime.Token);if(!previewLifetime.IsCancellationRequested)timeline.SetArtwork([ImageEngine.Load(frame)]);
            }
        }catch(OperationCanceledException){}catch(Exception error){if(IsVisible)timeline.ToolTip=L10n.Text("Timeline preview unavailable")+": "+error.Message;}
    }
    // One frame of the video at a time, small, for the chosen frames' pictures.
    private async Task<BitmapSource?> FrameAt(double seconds,int height)
    {
        if(previewTools?.Ready!=true)return null;try
        {
            var frame=Path.Combine(PreviewDirectory(),$"frame-{Guid.NewGuid():N}.png");
            await ProcessRunner.RunAsync(previewTools.Executable("ffmpeg"),["-v","error","-ss",seconds.ToString(CultureInfo.InvariantCulture),"-i",input,"-frames:v","1","-vf",$"scale=-2:{height}",frame],previewLifetime.Token);return File.Exists(frame)?ImageEngine.Load(frame):null;
        }catch(Exception error)when(error is OperationCanceledException or IOException or InvalidOperationException or ArgumentException or NotSupportedException){return null;}
    }
    // How loud the sound is now (integrated LUFS), measured the way the export measures it.
    private async Task MeasureLoudness()
    {
        if(previewTools?.Ready!=true){loudnessKnown?.Invoke();return;}try
        {
            var result=await ProcessRunner.RunAsync(previewTools.Executable("ffmpeg"),["-hide_banner","-nostdin","-v","info","-nostats","-i",input,"-map","0:a:0","-af","loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json","-f","null",OperatingSystem.IsWindows()?"NUL":"/dev/null"],previewLifetime.Token);
            var text=result.Error;var start=text.LastIndexOf('{');var end=text.LastIndexOf('}');
            if(start>=0&&end>start){using var json=System.Text.Json.JsonDocument.Parse(text[start..(end+1)]);if(json.RootElement.TryGetProperty("input_i",out var value)&&double.TryParse(value.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var lufs)&&double.IsFinite(lufs))loudness=lufs;}
        }
        catch(OperationCanceledException){return;}catch(Exception error)when(error is IOException or InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception){}
        if(IsLoaded)loudnessKnown?.Invoke();
    }
    // Audio to Video: a still of the video it will make, in its frame, made from the middle of the sound.
    private int visualGeneration;
    private async Task DrawVisualization()
    {
        var generation=++visualGeneration;double width,height;try{width=Value("Width",1280);height=Value("Height",720);}catch(ArgumentException){return;}if(width<16||height<16)return;
        visualFrame.Width=width;visualFrame.Height=height;var style=visualization.SelectedItem as string??"waveform";
        if(style=="image"){visualPreview.Source=visualImage is not null&&File.Exists(visualImage)?ImageEngine.Load(visualImage):null;return;}
        if(previewTools?.Ready!=true||media is null)return;
        try
        {
            await Task.Delay(120,previewLifetime.Token);if(generation!=visualGeneration)return;
            int w=(int)Math.Round(width/2/2)*2,h=(int)Math.Round(height/2/2)*2;var mid=Math.Max(0,duration/2);var frame=Path.Combine(PreviewDirectory(),$"visual-{generation}.png");
            var (from,length,filter)=style switch{"spectrum"=>(Math.Max(0,mid-10),20d,$"showspectrumpic=s={w}x{h}:legend=0:color=intensity"),"bars"=>(mid,.5,$"showfreqs=s={w}x{h}:mode=bar:ascale=log:fscale=log:rate=30"),_=>(mid,.5,$"showwaves=s={w}x{h}:mode=cline:rate=30:colors=0xe4bd59")};
            await ProcessRunner.RunAsync(previewTools.Executable("ffmpeg"),["-v","error","-ss",from.ToString(CultureInfo.InvariantCulture),"-t",length.ToString(CultureInfo.InvariantCulture),"-i",input,"-filter_complex",$"[0:a]{filter},format=rgb24","-update","1",frame],previewLifetime.Token);
            if(generation==visualGeneration&&File.Exists(frame))visualPreview.Source=ImageEngine.Load(frame);
        }
        catch(OperationCanceledException){}catch(Exception error)when(error is IOException or InvalidOperationException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception){if(IsVisible)ShowPreviewNotice(L10n.Text("Preview unavailable")+" · "+error.Message);}
    }
    // Audio to Video, moving: the video it will make, small and fast, made in the background after each choice. Once
    // ready it takes the still's place and plays with the sound, from where the playhead is (playing on if it was).
    private CancellationTokenSource? visualRender;private bool visualLive;
    private async Task RenderVisualization()
    {
        visualRender?.Cancel();visualLive=false;if(previewTools?.Ready!=true||media is null)return;var style=visualization.SelectedItem as string??"waveform";
        // A picture does not move: the still is the preview, and the sound plays under it.
        if(style=="image"){barHint.Text="";return;}
        double width,height;try{width=Value("Width",1280);height=Value("Height",720);}catch(ArgumentException){return;}if(width<16||height<16)return;
        var cancel=CancellationTokenSource.CreateLinkedTokenSource(previewLifetime.Token);visualRender=cancel;
        var scale=Math.Min(1,640/Math.Max(width,height));int w=Math.Max(16,(int)Math.Round(width*scale/2)*2),h=Math.Max(16,(int)Math.Round(height*scale/2)*2);var fps=duration>300?15:30;
        var visual=style switch{"spectrum"=>$"showspectrum=s={w}x{h}:slide=scroll:color=intensity:fps={fps}","bars"=>$"showfreqs=s={w}x{h}:mode=bar:ascale=log:fscale=log:rate={fps}",_=>$"showwaves=s={w}x{h}:mode=cline:rate={fps}:colors=0xe4bd59"};
        var output=Path.Combine(PreviewDirectory(),$"moving-{Guid.NewGuid():N}.mp4");
        // Every frame a key frame (-g 1): the picture changes wholly from frame to frame anyway, so the file is hardly larger,
        // and wherever playing starts or the playhead lands there is a whole frame to show. With key frames 8 s apart the
        // player froze the picture, or showed broken blocks, until the next one.
        string[] args=["-hide_banner","-nostdin","-v","error","-nostats","-progress","pipe:1","-i",input,"-filter_complex",$"[0:a]asplit[v][a];[v]{visual},format=yuv420p[video]","-map","[video]","-map","[a]","-c:v","libx264","-preset","ultrafast","-crf","30","-g","1","-c:a","aac","-b:a","128k","-movflags","+faststart","-t",duration.ToString(CultureInfo.InvariantCulture),output];
        var done=false;void Say(double share){if(!cancel.IsCancellationRequested&&!done)barHint.Text=string.Format(L10n.Text("Preparing the moving preview… {0}"),Math.Round(share*100).ToString(CultureInfo.CurrentCulture)+"%");}
        try
        {
            await Task.Delay(350,cancel.Token);Say(0);
            await ProcessRunner.RunAsync(previewTools.Executable("ffmpeg"),args,cancel.Token,line=>{if(line.StartsWith("out_time_us=",StringComparison.Ordinal)&&long.TryParse(line.AsSpan(12),out var us)&&duration>0)Dispatcher.BeginInvoke(()=>Say(Math.Clamp(us/1e6/duration,0,1)));});
            done=true;if(cancel.IsCancellationRequested||!File.Exists(output))return;
            var at=player.Position;player.ScrubbingEnabled=true;
            // Playing or not is read when the new source is ready, so a press of Play while it opens is kept.
            void Opened(object? sender,RoutedEventArgs args){player.MediaOpened-=Opened;if(cancel.IsCancellationRequested)return;player.Position=at;player.Play();if(!playing)player.Pause();visualLive=true;barHint.Text=L10n.Text("Play to see it move with the sound.");}
            // A paused player opens a new source only when told to play or pause: told now, its picture is there at once.
            player.MediaOpened+=Opened;player.Source=new Uri(output);if(!playing)player.Pause();
        }
        catch(OperationCanceledException){}
        catch(Exception error)when(error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception){if(IsVisible)barHint.Text=L10n.Text("Preview unavailable")+": "+error.Message.Split((char)10)[0];}
    }
    private void DeletePreviewFiles(){if(previewDirectory is null)return;var path=previewDirectory;_ = Task.Run(async()=>{for(var n=0;n<10;n++){try{if(Directory.Exists(path))Directory.Delete(path,true);return;}catch(IOException){await Task.Delay(200);}catch(UnauthorizedAccessException){return;}}});}
}
