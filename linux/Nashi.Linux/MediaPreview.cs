using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Nashi.Core;
namespace Nashi.Linux;
internal sealed class MediaPreview:StackPanel
{
    private readonly MediaPlayer player;
    private readonly DispatcherTimer clock=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private readonly Slider scrubber=new(){Minimum=0,Maximum=1};
    private readonly Func<string,double?> get;
    private readonly Action<string,double> set;
    private bool updating;
    private double? rangeEnd;
    private readonly RangeTimeline timeline=new();
    private CancellationTokenSource? artworkRequest;
    private string currentPath;
    private readonly string originalPath;
    private double previewStart;
    private double? previewEnd;
    private bool Processed=>currentPath!=originalPath;
    private double RangeStart=>Processed?previewStart:get("Media.Start")??0;
    private double RangeEnd=>Processed?previewEnd??player.Duration:get("Media.End")??player.Duration;
    private void SetRange(bool start,double value){if(Processed){if(start)previewStart=value;else previewEnd=value;}else set(start?"Media.Start":"Media.End",value);}
    internal string? ArtworkSource{get;private set;}
    internal string? ArtworkError{get;private set;}
    private bool initialized;
    public MediaPreview(string path,Func<string,double?> get,Action<string,double> set)
    {
        originalPath=currentPath=Path.GetFullPath(path);this.get=get;this.set=set;Spacing=10;player=new(path){Height=280};Children.Add(player);
        var controls=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
        controls.Children.Add(NashiTheme.Button("−1s",()=>player.Seek(player.Position-1)));controls.Children.Add(NashiTheme.Button("Play / Pause",player.Toggle));controls.Children.Add(NashiTheme.Button("+1s",()=>player.Seek(player.Position+1)));Children.Add(controls);
        Children.Add(timeline);timeline.Changed+=(start,end)=>{SetRange(true,start);SetRange(false,end);player.Seek(start);};
        Children.Add(scrubber);var time=NashiTheme.Text("0:00 / 0:00",12,true);Children.Add(time);
        var range=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};range.Children.Add(NashiTheme.Button("Mark In",()=>SetRange(true,player.Position)));range.Children.Add(NashiTheme.Button("Mark Out",()=>SetRange(false,player.Position)));range.Children.Add(NashiTheme.Button("Play selected range",()=>
        {
            if(!initialized)return;var start=RangeStart;var end=RangeEnd;
            if(!double.IsFinite(start)||!double.IsFinite(end)||start<0||end<=start||end>player.Duration){time.Text="Choose a valid In / Out range.";return;}
            player.Pause();player.Seek(start);rangeEnd=end;player.Toggle();
        }));Children.Add(range);Children.Add(NashiTheme.Text("Space play/pause · ←/→ seek · I/O mark range",12,true));
        scrubber.ValueChanged+=(_,_)=>{if(!updating)player.Seek(scrubber.Value);};
        clock.Tick+=(_,_)=>
        {
            if(!player.Available){if(player.Error is {} message)time.Text="Install libmpv for embedded playback. "+message;return;}
            if(!initialized&&player.Duration>0&&player.LoadedPath==currentPath){initialized=true;if(Processed)previewEnd=player.Duration;else if(get("Media.End")is null)set("Media.End",player.Duration);_ = LoadArtwork(currentPath,player.Duration);}
            timeline.Set(player.Duration,RangeStart,RangeEnd);
            updating=true;scrubber.Maximum=Math.Max(RangeTimeline.MinimumSpan,player.Duration);scrubber.Value=Math.Clamp(player.Position,0,scrubber.Maximum);updating=false;
            time.Text=TimeSpan.FromSeconds(player.Position).ToString(@"m\:ss")+" / "+TimeSpan.FromSeconds(player.Duration).ToString(@"m\:ss");
            if(rangeEnd is {} end&&player.Position>=end){player.Pause();rangeEnd=null;}
        };
        AttachedToVisualTree+=(_,_)=>clock.Start();DetachedFromVisualTree+=(_,_)=>{clock.Stop();CancelArtwork();timeline.SetArtwork([]);ArtworkSource=null;};
    }
    internal Task ShutdownAsync(){clock.Stop();CancelArtwork();timeline.SetArtwork([]);ArtworkSource=null;return player.ShutdownAsync();}
    private void CancelArtwork(){artworkRequest?.Cancel();}
    internal void Reset(){Load(originalPath);player.Pause();}
    internal void Load(string file){CancelArtwork();currentPath=Path.GetFullPath(file);initialized=false;previewStart=0;previewEnd=null;rangeEnd=null;ArtworkSource=null;ArtworkError=null;timeline.SetArtwork([]);player.Load(file);}
    private async Task LoadArtwork(string path,double duration)
    {
        CancelArtwork();using var request=new CancellationTokenSource();artworkRequest=request;ArtworkError=null;
        if(SystemTools.Find("ffmpeg")is not {} ffmpeg){ArtworkError="Install FFmpeg for timeline artwork.";artworkRequest=null;return;}var work=Path.Combine(Path.GetTempPath(),"NashiTimeline-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);var images=new List<Bitmap>();
        try{
            var audio=FeatureCatalog.IsAudio(path);var target=Path.Combine(work,audio?"wave.png":"frame-%02d.png");
            var args=audio?new[]{"-v","error","-nostdin","-i",path,"-filter_complex","showwavespic=s=960x80:colors=0xE3B859","-frames:v","1",target}:new[]{"-v","error","-nostdin","-i",path,"-vf","fps=1/"+Math.Max(.1,duration/12).ToString(CultureInfo.InvariantCulture)+",scale=120:-1","-frames:v","12",target};
            await ProcessRunner.RunAsync(ffmpeg,args,request.Token);foreach(var file in Directory.GetFiles(work,"*.png").Order())images.Add(new Bitmap(file));request.Token.ThrowIfCancellationRequested();if(images.Count==0)throw new IOException("FFmpeg returned no timeline artwork.");timeline.SetArtwork(images.ToArray());images.Clear();ArtworkSource=path;
        }catch(OperationCanceledException){}catch(Exception error){if(!request.IsCancellationRequested)ArtworkError=error.Message;}finally{foreach(var image in images)image.Dispose();if(ReferenceEquals(artworkRequest,request))artworkRequest=null;Directory.Delete(work,true);}
    }
    internal bool Handle(KeyEventArgs e)
    {
        if(e.Source is TextBox)return false;
        switch(e.Key)
        {
            case Key.Space:player.Toggle();break;
            case Key.Left:player.Seek(player.Position-(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?1d/30:1));break;
            case Key.Right:player.Seek(player.Position+(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?1d/30:1));break;
            case Key.I:SetRange(true,player.Position);break;
            case Key.O:SetRange(false,player.Position);break;
            default:return false;
        }
        return true;
    }
}
