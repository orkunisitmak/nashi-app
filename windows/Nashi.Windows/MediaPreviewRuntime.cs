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
        if(processedPlayback||processingPreview){time.Text=L10n.Text("Processed preview unavailable for this Windows codec.");return;}
        if(triedProxy||previewTools?.Ready!=true){time.Text="This Windows codec cannot play directly. Set up media tools to create a local preview.";return;}triedProxy=true;time.Text="Preparing a local preview…";
        try
        {
            var output=Path.Combine(PreviewDirectory(),media?.Video is null?"preview.wav":"preview.mp4");var args=new List<string>{"-v","error","-nostdin","-i",input};if(media?.Video is not null)args.AddRange(["-vf","scale=w='min(1280,iw)':h=-2","-c:v","libx264","-preset","veryfast","-crf","25","-pix_fmt","yuv420p","-c:a","aac","-movflags","+faststart"]);else args.AddRange(["-vn","-c:a","pcm_s16le"]);args.Add(output);
            await ProcessRunner.RunAsync(previewTools.Executable("ffmpeg"),args,previewLifetime.Token);if(previewLifetime.IsCancellationRequested)return;originalPreviewPath=output;if(processedPlayback||processingPreview)return;player.Source=new Uri(output);player.Play();player.Pause();playing=false;Seek(0);time.Text="Preview ready · export uses your original file.";
        }catch(OperationCanceledException){}catch(Exception error){if(IsVisible)time.Text="Preview unavailable: "+error.Message;}
    }
    private async Task LoadTimelineArtwork()
    {
        if(previewTools is null||media is null)return;try
        {
            if(media.Video is not null)
            {
                var frames=new List<BitmapSource>();for(var n=0;n<10;n++){previewLifetime.Token.ThrowIfCancellationRequested();var frame=Path.Combine(PreviewDirectory(),$"frame-{n}.png");await ProcessRunner.RunAsync(previewTools.Executable("ffmpeg"),["-v","error","-ss",(Math.Max(0,duration-.001)*n/10).ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",input,"-frames:v","1","-vf","scale=160:-1",frame],previewLifetime.Token);frames.Add(ImageEngine.Load(frame));}if(!previewLifetime.IsCancellationRequested)timeline.SetArtwork(frames.ToArray());
            }
            else
            {
                var frame=Path.Combine(PreviewDirectory(),"waveform.png");await ProcessRunner.RunAsync(previewTools.Executable("ffmpeg"),["-v","error","-i",input,"-filter_complex","aformat=channel_layouts=mono,showwavespic=s=1200x140:colors=0xe5bc59","-frames:v","1",frame],previewLifetime.Token);if(!previewLifetime.IsCancellationRequested)timeline.SetArtwork([ImageEngine.Load(frame)]);
            }
        }catch(OperationCanceledException){}catch(Exception error){if(IsVisible)timeline.ToolTip="Timeline preview unavailable: "+error.Message;}
    }
    private void DeletePreviewFiles(){if(previewDirectory is null)return;var path=previewDirectory;_ = Task.Run(async()=>{for(var n=0;n<10;n++){try{if(Directory.Exists(path))Directory.Delete(path,true);return;}catch(IOException){await Task.Delay(200);}catch(UnauthorizedAccessException){return;}}});}
}
