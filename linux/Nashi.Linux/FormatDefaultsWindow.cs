using System.Globalization;
using Avalonia.Controls;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed class FormatDefaultsWindow:Window
{
    internal FormatDefaultsWindow(PreferencesStore store)
    {
        Title="Nashi · Export defaults";Width=540;Height=640;Background=NashiTheme.Paper;Foreground=NashiTheme.Ink;
        var page=new StackPanel{Margin=new(24),Spacing=12};Content=new ScrollViewer{Content=page};
        page.Children.Add(NashiTheme.Text("Your go-to exports.",24));page.Children.Add(NashiTheme.Text("Defaults apply whenever you choose an output format.",13,true));
        var formats=FeatureCatalog.Conversions.Values.SelectMany(v=>v).Distinct().Order().ToArray();
        var select=new ComboBox{Name="ExportFormat",ItemsSource=formats,SelectedIndex=0};page.Children.Add(select);var panel=new StackPanel{Spacing=10};page.Children.Add(panel);
        void Render(){
            panel.Children.Clear();var name=(string)select.SelectedItem!;var profile=store.Profile(name);var fields=new Dictionary<string,TextBox>();var choices=new Dictionary<string,ComboBox>();
            void Number(string key,string title,double value){panel.Children.Add(NashiTheme.Text(title,13));var input=new TextBox{Name=key,Text=value.ToString("0.###",CultureInfo.InvariantCulture)};fields[key]=input;panel.Children.Add(input);}
            void Choice(string key,string title,string[] values,string value){panel.Children.Add(NashiTheme.Text(title,13));var input=new ComboBox{Name=key,ItemsSource=values,SelectedItem=value};choices[key]=input;panel.Children.Add(input);}
            var audio=MediaEngine.AudioFormats.Contains(name);var video=MediaEngine.VideoFormats.Contains(name)&&name!="gif";var image=new[]{"png","jpg","heic","webp","avif","tiff","bmp"}.Contains(name);
            if(video||new[]{"jpg","heic","webp","avif"}.Contains(name))Number("quality","Quality (%)",profile.Quality*100);
            if(video||audio&&name is not ("wav" or "aiff" or "flac"))Number("bitrate","Audio bitrate (kbps)",profile.AudioBitrate);
            if(name is "wav" or "aiff")Choice("depth","Audio sample depth",["matchSource","pcm16","pcm24","pcm32","float32"],profile.SampleDepth);
            if(video)Number("height","Maximum video height (0 = original)",profile.MaxVideoHeight);
            if(name=="gif"){Number("fps","GIF frames per second",profile.GifFrameRate);Number("width","GIF width",profile.GifWidth);}
            if(name is "png" or "jpg" or "epub")Number("dpi","PDF rendering DPI",profile.Dpi);
            if(name is "docx" or "pages")Choice("layout","PDF layout",["editable","preserve"],profile.Layout);
            if(name=="epub")Choice("epub","EPUB layout",["reflowable","fixed"],profile.EpubLayout);
            var metadata=new CheckBox{Name="ProfileMetadata",Content="Preserve metadata",IsChecked=profile.PreserveMetadata};if(video||audio||image&&name!="bmp")panel.Children.Add(metadata);
            if(fields.Count+choices.Count==0&&metadata.Parent is null)panel.Children.Add(NashiTheme.Text("This format uses its original document structure. No export defaults are needed.",13,true));
            var error=NashiTheme.Text("",13);panel.Children.Add(error);
            double Value(string key,double fallback){if(!fields.TryGetValue(key,out var field))return fallback;var number=double.Parse(field.Text??"",CultureInfo.InvariantCulture);if(!double.IsFinite(number))throw new ArgumentException("Choose a finite number.");return number;}
            string Picked(string key,string fallback)=>choices.TryGetValue(key,out var field)?(string)field.SelectedItem!:fallback;
            panel.Children.Add(NashiTheme.Button("Save defaults",()=>{
                try{store.SetProfile(name,profile with{Quality=Value("quality",profile.Quality*100)/100,AudioBitrate=checked((int)Value("bitrate",profile.AudioBitrate)),SampleDepth=Picked("depth",profile.SampleDepth),MaxVideoHeight=checked((int)Value("height",profile.MaxVideoHeight)),GifFrameRate=checked((int)Value("fps",profile.GifFrameRate)),GifWidth=checked((int)Value("width",profile.GifWidth)),Dpi=checked((int)Value("dpi",profile.Dpi)),Layout=Picked("layout",profile.Layout),EpubLayout=Picked("epub",profile.EpubLayout),PreserveMetadata=metadata.IsChecked==true});profile=store.Profile(name);error.Text="Defaults saved.";}
                catch(Exception exception){error.Text=exception.Message;}
            },true));
            panel.Children.Add(NashiTheme.Button("Restore defaults",()=>{try{store.SetProfile(name,new());Render();}catch(Exception exception){error.Text=exception.Message;}}));
        }
        select.SelectionChanged+=(_,_)=>Render();Render();
    }
}
