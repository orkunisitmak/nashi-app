using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class ToolEditor
{
    private readonly CheckBox trimSilence=new(){Content="Remove silence at the beginning and end",Margin=new(4,12,4,12)},joinClips=new(){Content="Join retained clips into one video",Margin=new(4,12,4,12)};
    private readonly ComboBox visualization=new(){ItemsSource=new[]{"waveform","spectrum","bars","image"},SelectedIndex=0},redactMode=new(){ItemsSource=new[]{"solid","blur","pixelate"},SelectedIndex=0},bleepMode=new(){ItemsSource=new[]{"bleep","mute"},SelectedIndex=0},sampleDepth=new(){ItemsSource=new[]{"matchSource","pcm16","pcm24","pcm32","float32"},SelectedIndex=0};
    private readonly List<MediaRange> bleepRanges=[];private readonly List<MediaRegion> videoRegions=[];private readonly ListBox rangesList=new(){MaxHeight=140};private string? visualImage;private bool playingRange;private double rangePlaybackEnd;
    private void BuildMediaOptions()
    {
        if(media is null)return;
        if(tool is "trimAudio" or "trimVideo")options.Children.Add(trimSilence);
        if(tool=="normalizeAudio")Field("Loudness range",11);
        if(tool=="splitVideo"){TextField("Split points (seconds; separated by semicolons)","");TextField("Remove clips (numbers; separated by semicolons)","");options.Children.Add(joinClips);}
        if(tool=="audioToVideo")
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="Visualization"}));options.Children.Add(visualization);options.Children.Add(MainWindow.Button("Choose visualization image…",()=>{var picker=new OpenFileDialog{Filter="Images|*.png;*.jpg;*.bmp;*.webp"};if(picker.ShowDialog(this)==true){visualImage=picker.FileName;visualization.SelectedItem="image";}}));
        }
        if(tool=="redactAudio")
        {
            options.Children.Add(L10n.Label(new TextBlock{Text="Replace selected audio with"}));options.Children.Add(bleepMode);options.Children.Add(MainWindow.Button("Add selected range",()=>{try{var (a,b)=ReadPlaybackRange("Choose a valid range first.");bleepRanges.Add(new(a,b));ReloadRanges();}catch(ArgumentException error){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Check selected range"));}}));options.Children.Add(rangesList);options.Children.Add(MainWindow.Button("Remove selected range",()=>{if(rangesList.SelectedIndex is var i&&i>=0&&i<bleepRanges.Count){bleepRanges.RemoveAt(i);ReloadRanges();}}));
        }
        if(tool=="redactVideo"){options.Children.Add(L10n.Label(new TextBlock{Text="Cover style"}));options.Children.Add(redactMode);options.Children.Add(rangesList);options.Children.Add(MainWindow.Button("Remove selected area",()=>{var i=rangesList.SelectedIndex;if(i>=0&&i<videoRegions.Count){videoRegions.RemoveAt(i);var shape=drawing.Children.OfType<System.Windows.Shapes.Rectangle>().ElementAtOrDefault(i);if(shape is not null)drawing.Children.Remove(shape);ReloadRanges();}}));}
    }
    private void ReloadRanges(){rangesList.ItemsSource=null;rangesList.ItemsSource=tool=="redactAudio"?bleepRanges.Select((r,i)=>$"{i+1} · {r.Start:0.###}–{r.End:0.###} s").ToArray():videoRegions.Select((r,i)=>$"{i+1} · {r.Width:0}×{r.Height:0} · {r.Start:0.###}–{r.End:0.###} s").ToArray();}
    private (double Start,double End) ReadPlaybackRange(string message="Start must be before End, within the media duration.")
    {
        var a=Value("Start");var b=Value("End",duration);var end=RangeEnd();
        if(a<0||Math.Round(end-a,6)<MinRange||duration>0&&b>duration+RangeSlack)throw new ArgumentException(message);
        return(a,end);
    }
    private void PlayRange()
    {
        try{var (a,b)=ReadPlaybackRange();Seek(a);rangePlaybackEnd=b;playingRange=true;player.Play();playing=true;}
        catch(ArgumentException error){player.Pause();playing=false;playingRange=false;time.Text=error.Message;}
    }
    private MediaOptions MoreMediaSettings(MediaOptions original)
    {
        double[] Numbers(string name)=>fields.TryGetValue(name,out var field)&&!string.IsNullOrWhiteSpace(field.Text)?field.Text.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(s=>NumericInput.ParseFinite(s.Trim())).ToArray():[];
        var target=Value("Target size (KB; 0 = auto)");var reduction=Value("Reduction (%; 0 = auto)");var maxHeight=(int)Value("Maximum video height (0 = original)");
        return original with{AudioBitrate=(int)Value("Audio bitrate (kbps)",256),SampleDepth=sampleDepth.SelectedItem as string??"matchSource",GifFrameRate=(int)Value("GIF frames per second",15),GifWidth=(int)Value("GIF width",960),Height=tool=="convert"&&maxHeight>0?maxHeight:original.Height,PreserveMetadata=preserveMetadata.IsChecked==true,TargetBytes=target>0?(long)(target*1024):null,ReductionPercent=reduction>0?reduction:null,TrimSilence=trimSilence.IsChecked==true,LoudnessRange=Value("Loudness range",11),SplitPoints=Numbers("Split points (seconds; separated by semicolons)"),DiscardedClips=Numbers("Remove clips (numbers; separated by semicolons)").Select(n=>(int)n-1).ToArray(),JoinSplitClips=joinClips.IsChecked==true,Visualization=visualization.SelectedItem as string??"waveform",VisualizationImage=visualImage,RedactionMode=tool=="redactAudio"?bleepMode.SelectedItem as string??"bleep":redactMode.SelectedItem as string??"solid",AudioRedactions=tool=="redactAudio"&&bleepRanges.Count>0?bleepRanges.ToArray():original.AudioRedactions,Regions=tool=="redactVideo"&&videoRegions.Count>0?videoRegions.ToArray():original.Regions};
    }
}
