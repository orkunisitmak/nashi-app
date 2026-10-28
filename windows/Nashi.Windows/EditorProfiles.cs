using System.Windows.Controls;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class ToolEditor
{
    private readonly CheckBox preserveMetadata=new(){Content="Preserve metadata",IsChecked=true,Margin=new(4,12,4,12)};
    private OutputProfile exportProfile=new();
    internal void ConfigureProfiles(PreferencesStore preferences,string? requestedFormat=null)
    {
        if(tool!="convert")return;
        if(!fields.ContainsKey("Quality (%)"))Field("Quality (%)",85);
        if(media is not null){Field("Audio bitrate (kbps)",256);Field("Maximum video height (0 = original)",0);Field("GIF frames per second",15);Field("GIF width",960);options.Children.Add(L10n.Label(new TextBlock{Text="Audio sample depth"}));options.Children.Add(sampleDepth);}
        options.Children.Add(preserveMetadata);
        void Apply(){if(format.SelectedItem is not string value)return;
            void Show(string name,bool visible){if(!fields.TryGetValue(name,out var box))return;var index=options.Children.IndexOf(box);box.Visibility=visible?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;if(index>0&&options.Children[index-1] is TextBlock label)label.Visibility=box.Visibility;}
            void Combo(ComboBox box,bool visible){box.Visibility=visible?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;var i=options.Children.IndexOf(box);if(i>0&&options.Children[i-1] is TextBlock label)label.Visibility=box.Visibility;}
            var video=media is not null&&MediaEngine.VideoFormats.Contains(value);var audio=media is not null&&MediaEngine.AudioFormats.Contains(value);
            Show("Quality (%)",video&&value!="gif"||bitmap is not null&&new[]{"jpg","webp","heic","avif"}.Contains(value));Show("Audio bitrate (kbps)",(audio||video)&&value is not ("wav" or "aiff" or "flac" or "gif"));Show("Maximum video height (0 = original)",video&&value!="gif");Show("GIF frames per second",video&&value=="gif");Show("GIF width",video&&value=="gif");Show("DPI",value is "png" or "jpg" or "epub");Combo(sampleDepth,audio&&value is "wav" or "aiff");
            preserveMetadata.Visibility=media is not null&&value!="gif"||bitmap is not null&&value is not ("pdf" or "bmp" or "gif")?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;Combo(documentLayout,FeatureCatalog.Extension(input)=="pdf"&&value is "docx" or "pages");Combo(epubLayout,value=="epub");
exportProfile=preferences.Profile(value);fields["Quality (%)"].Text=Number(exportProfile.Quality*100);preserveMetadata.IsChecked=exportProfile.PreserveMetadata;
            if(fields.TryGetValue("Audio bitrate (kbps)",out var bitrate))bitrate.Text=Number(exportProfile.AudioBitrate);if(fields.TryGetValue("Maximum video height (0 = original)",out var height))height.Text=Number(exportProfile.MaxVideoHeight);if(fields.TryGetValue("GIF frames per second",out var fps))fps.Text=Number(exportProfile.GifFrameRate);if(fields.TryGetValue("GIF width",out var width))width.Text=Number(exportProfile.GifWidth);sampleDepth.SelectedItem=exportProfile.SampleDepth;
            if(fields.TryGetValue("DPI",out var dpi))dpi.Text=Number(exportProfile.Dpi);documentLayout.SelectedItem=exportProfile.Layout;epubLayout.SelectedItem=exportProfile.EpubLayout;
        }
        applyExportProfile=Apply;format.SelectionChanged+=(_,_)=>Apply();if(requestedFormat is not null&&format.Items.Contains(requestedFormat))format.SelectedItem=requestedFormat;Apply();
    }
    internal bool PreserveImageMetadata=>preserveMetadata.IsChecked==true;
}
