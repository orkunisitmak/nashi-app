using System.Windows.Controls;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class ToolEditor
{
    private Button? processedButton;private bool processingPreview,processedPlayback;
    internal string? ProcessedPreviewPath{get;private set;}
    internal bool ProcessedPlayback=>processedPlayback;
    internal string UIAuditPreviewStatus=>time.Text;
    private void PlayOriginalPreview()
    {
        if(processingPreview||media is null)return;processedPlayback=false;drawing.IsEnabled=true;drawing.Visibility=System.Windows.Visibility.Visible;duration=media.Duration;scrubber.Maximum=duration;player.Source=new Uri(originalPreviewPath??input);Seek(0);player.Play();playing=true;
    }
    private async Task PreviewProcessed()
    {
        if(processingPreview||previewTools?.Ready!=true){time.Text="Set up media tools to preview the processed result.";return;}
        if(!ReadSettings(false))return;processingPreview=true;if(processedButton is not null){processedButton.IsEnabled=false;processedButton.Content="Preparing…";}time.Text="Preparing processed preview…";
        try
        {
            var engine=new MediaEngine(previewTools.Executable("ffmpeg"),previewTools.Executable("ffprobe"));var settings=tool=="splitVideo"?MediaOptions with{JoinSplitClips=true}:MediaOptions;var rendered=(await engine.ToolAsync(tool,[input],PreviewDirectory(),settings,null,previewLifetime.Token))[0];
            var playable=(await engine.ConvertAsync([rendered],media?.Video is null&&tool!="audioToVideo"?"wav":"mp4",PreviewDirectory(),new(){PreserveMetadata=false},null,previewLifetime.Token))[0];
            var info=await engine.InspectAsync(playable,previewLifetime.Token);if(previewLifetime.IsCancellationRequested)return;
            ProcessedPreviewPath=playable;player.Source=new Uri(playable);duration=info.Duration;scrubber.Maximum=duration;processedPlayback=true;drawing.IsEnabled=false;drawing.Visibility=System.Windows.Visibility.Hidden;Seek(0);player.Play();playing=true;time.Text="Processed preview · export uses the original file.";
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(IsVisible)time.Text="Preview unavailable: "+error.Message;}
        finally{processingPreview=false;if(processedButton is not null){processedButton.IsEnabled=true;processedButton.Content=tool=="splitVideo"?"Play Result":tool=="audioToVideo"?"Preview Visualization":"Preview Processed";L10n.Content(processedButton);}}
    }
}
