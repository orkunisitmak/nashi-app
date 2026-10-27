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
    private readonly CancellationTokenSource lifetime=new();
    private bool initialized;
    public MediaPreview(string path,Func<string,double?> get,Action<string,double> set)
    {
        this.get=get;this.set=set;Spacing=10;player=new(path){Height=280};Children.Add(player);
        var controls=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
        controls.Children.Add(NashiTheme.Button("−1s",()=>player.Seek(player.Position-1)));controls.Children.Add(NashiTheme.Button("Play / Pause",player.Toggle));controls.Children.Add(NashiTheme.Button("+1s",()=>player.Seek(player.Position+1)));Children.Add(controls);
        Children.Add(timeline);timeline.Changed+=(start,end)=>{set("Media.Start",start);set("Media.End",end);player.Seek(start);};
        Children.Add(scrubber);var time=NashiTheme.Text("0:00 / 0:00",12,true);Children.Add(time);
        var range=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};range.Children.Add(NashiTheme.Button("Mark In",()=>set("Media.Start",player.Position)));range.Children.Add(NashiTheme.Button("Mark Out",()=>set("Media.End",player.Position)));range.Children.Add(NashiTheme.Button("Play selected range",()=>
        {
            var start=get("Media.Start")??0;var end=get("Media.End")??player.Duration;
            if(!double.IsFinite(start)||!double.IsFinite(end)||start<0||end<=start||end>player.Duration){time.Text="Choose a valid In / Out range.";return;}
            player.Pause();player.Seek(start);rangeEnd=end;player.Toggle();
        }));Children.Add(range);Children.Add(NashiTheme.Text("Space play/pause · ←/→ seek · I/O mark range",12,true));
        scrubber.ValueChanged+=(_,_)=>{if(!updating)player.Seek(scrubber.Value);};
        clock.Tick+=(_,_)=>
        {
            if(!player.Available){if(player.Error is {} message)time.Text="Install libmpv for embedded playback. "+message;return;}
            if(!initialized&&player.Duration>0){initialized=true;if(get("Media.End")is null)set("Media.End",player.Duration);_ = LoadArtwork(path,player.Duration);}
            timeline.Set(Math.Max(1,player.Duration),get("Media.Start")??0,get("Media.End")??player.Duration);
            updating=true;scrubber.Maximum=Math.Max(1,player.Duration);scrubber.Value=Math.Clamp(player.Position,0,scrubber.Maximum);updating=false;
            time.Text=TimeSpan.FromSeconds(player.Position).ToString(@"m\:ss")+" / "+TimeSpan.FromSeconds(player.Duration).ToString(@"m\:ss");
            if(rangeEnd is {} end&&player.Position>=end){player.Pause();rangeEnd=null;}
        };
        AttachedToVisualTree+=(_,_)=>clock.Start();DetachedFromVisualTree+=(_,_)=>{clock.Stop();lifetime.Cancel();};
    }
    internal void Load(string file){initialized=false;player.Load(file);}
    private async Task LoadArtwork(string path,double duration)
    {
        if(SystemTools.Find("ffmpeg")is not {} ffmpeg)return;var work=Path.Combine(Path.GetTempPath(),"NashiTimeline-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        try{
            var audio=FeatureCatalog.IsAudio(path);var target=Path.Combine(work,audio?"wave.png":"frame-%02d.png");
            var args=audio?new[]{"-v","error","-nostdin","-i",path,"-filter_complex","showwavespic=s=960x80:colors=0xE3B859","-frames:v","1",target}:new[]{"-v","error","-nostdin","-i",path,"-vf","fps=1/"+Math.Max(.1,duration/12).ToString(CultureInfo.InvariantCulture)+",scale=120:-1","-frames:v","12",target};
            await ProcessRunner.RunAsync(ffmpeg,args,lifetime.Token);var images=Directory.GetFiles(work,"*.png").Order().Select(p=>new Bitmap(p)).ToArray();if(lifetime.IsCancellationRequested){foreach(var image in images)image.Dispose();return;}timeline.SetArtwork(images);
        }catch(OperationCanceledException){}catch(Exception){}finally{Directory.Delete(work,true);}
    }
    internal bool Handle(KeyEventArgs e)
    {
        if(e.Source is TextBox)return false;
        switch(e.Key)
        {
            case Key.Space:player.Toggle();break;
            case Key.Left:player.Seek(player.Position-(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?1d/30:1));break;
            case Key.Right:player.Seek(player.Position+(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?1d/30:1));break;
            case Key.I:set("Media.Start",player.Position);break;
            case Key.O:set("Media.End",player.Position);break;
            default:return false;
        }
        return true;
    }
}
