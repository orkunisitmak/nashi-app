using Avalonia.Controls;
using Nashi.Core;
using Nashi.Windows;
namespace Nashi.Linux;

public sealed partial class ToolEditor
{
    private void ConfigureProfile(PreferencesStore preferences,string format,string[] inputs)
    {
        format=format.ToLowerInvariant();var profile=preferences.Profile(format);
        var media=inputs.All(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p));
        var image=inputs.All(FeatureCatalog.IsImage);
        void Field(string name,object? value){
            if(textFields.TryGetValue(name,out var text)){text.Text=Display(value);text.Tag=text.Text;return;}
            var control=fields.Children.OfType<Control>().FirstOrDefault(c=>c.Name==name.Replace(".","_",StringComparison.Ordinal));
            if(control is ComboBox choice){choice.SelectedItem=value;choice.Tag=value;}
            if(control is CheckBox toggle){toggle.IsChecked=value as bool?;toggle.Tag=value;}
        }
        void Apply(){
            if(media){Field("Media.Quality",profile.Quality);Field("Media.AudioBitrate",profile.AudioBitrate);Field("Media.SampleDepth",profile.SampleDepth);Field("Media.GifFrameRate",profile.GifFrameRate);Field("Media.GifWidth",profile.GifWidth);Field("Media.Height",profile.MaxVideoHeight==0?null:profile.MaxVideoHeight);Field("Media.PreserveMetadata",profile.PreserveMetadata);}
            else if(image){Field("Quality",(int)Math.Round(profile.Quality*100));Field("PreserveImageMetadata",profile.PreserveMetadata);}
            else{Field("Document.Dpi",profile.Dpi);Field("Document.Layout",profile.Layout);Field("Document.EpubLayout",profile.EpubLayout);}
        }
        Apply();
        void Visible(string name,bool visible){
            var control=fields.Children.OfType<Control>().FirstOrDefault(c=>c.Name==name.Replace(".","_",StringComparison.Ordinal));if(control is null)return;
            control.IsVisible=visible;var index=fields.Children.IndexOf(control);
            if(control is TextBox or ComboBox&&index>0&&fields.Children[index-1] is TextBlock label)label.IsVisible=visible;
        }
        var video=media&&MediaEngine.VideoFormats.Contains(format);var audio=media&&MediaEngine.AudioFormats.Contains(format);
        if(media){
            Visible("Media.Quality",video&&format!="gif");Visible("Media.AudioBitrate",(audio||video)&&format is not ("wav" or "aiff" or "flac" or "gif"));
            Visible("Media.SampleDepth",audio&&format is "wav" or "aiff");Visible("Media.Width",false);Visible("Media.Height",video&&format!="gif");
            Visible("Media.GifFrameRate",format=="gif");Visible("Media.GifWidth",format=="gif");Visible("Media.PreserveMetadata",format!="gif");
        }else if(image){Visible("Quality",format is "jpg" or "webp" or "heic" or "avif");Visible("PreserveImageMetadata",format is not ("pdf" or "bmp" or "gif"));}
        else{
            Visible("Document.Dpi",format is "png" or "jpg" or "epub");Visible("Document.Layout",FeatureCatalog.Extension(inputs[0])=="pdf"&&format is "docx" or "pages");Visible("Document.EpubLayout",format=="epub");Visible("Document.Password",false);
        }
        fields.Children.Add(NashiTheme.Button("Save defaults for "+format.ToUpperInvariant(),()=>{
            try{
                var options=(LinuxOptions)Fill(new LinuxOptions());
                var updated=media?profile with{Quality=options.Media.Quality,AudioBitrate=options.Media.AudioBitrate,SampleDepth=options.Media.SampleDepth,GifFrameRate=options.Media.GifFrameRate,GifWidth=options.Media.GifWidth,MaxVideoHeight=options.Media.Height??0,PreserveMetadata=options.Media.PreserveMetadata}
                    :image?profile with{Quality=options.Quality/100d,PreserveMetadata=options.PreserveImageMetadata}
                    :profile with{Dpi=options.Document.Dpi,Layout=options.Document.Layout,EpubLayout=options.Document.EpubLayout};
                preferences.SetProfile(format,updated);profile=updated;Apply();error.Text="Defaults saved for "+format.ToUpperInvariant()+".";
            }catch(Exception exception){error.Text=exception.Message;}
        }));
    }
}
