using System.Windows;
using System.Windows.Controls;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class SettingsWindow
{
    private void FormatDefaults()
    {
        Heading("Your go-to exports.","Defaults apply whenever you choose an output format.");var formats=FeatureCatalog.Conversions.Values.SelectMany(v=>v).Distinct().Order().ToArray();var select=new ComboBox{ItemsSource=formats,SelectedIndex=0};page.Children.Add(select);var panel=new StackPanel();page.Children.Add(panel);
        void Render()
        {
            panel.Children.Clear();var name=(string)select.SelectedItem;var profile=store.Profile(name);var fields=new Dictionary<string,TextBox>();var choices=new Dictionary<string,ComboBox>();
            void Number(string key,string title,double value){panel.Children.Add(L10n.Label(new TextBlock{Text=title,Margin=new(4,12,4,0)}));var input=new TextBox{Text=value.ToString("0.###",System.Globalization.CultureInfo.CurrentCulture)};fields[key]=input;panel.Children.Add(input);}
            void Choice(string key,string title,string[] values,string value){panel.Children.Add(L10n.Label(new TextBlock{Text=title,Margin=new(4,12,4,0)}));var input=new ComboBox{ItemsSource=values,SelectedItem=value};choices[key]=input;panel.Children.Add(input);}
            var audio=MediaEngine.AudioFormats.Contains(name);var video=MediaEngine.VideoFormats.Contains(name)&&name!="gif";var image=new[]{"png","jpg","heic","webp","avif","tiff","bmp"}.Contains(name);
            if(video||new[]{"jpg","heic","webp","avif"}.Contains(name))Number("quality","Quality (%)",profile.Quality*100);
            if(video||audio&&name is not ("wav" or "aiff" or "flac"))Number("bitrate","Audio bitrate (kbps)",profile.AudioBitrate);
            if(name is "wav" or "aiff")Choice("depth","Audio sample depth",["matchSource","pcm16","pcm24","pcm32","float32"],profile.SampleDepth);
            if(video)Number("height","Maximum video height (0 = original)",profile.MaxVideoHeight);
            if(name=="gif"){Number("fps","GIF frames per second",profile.GifFrameRate);Number("width","GIF width",profile.GifWidth);}
            if(name is "png" or "jpg" or "epub")Number("dpi","PDF rendering DPI",profile.Dpi);
            if(name is "docx" or "pages")Choice("layout","PDF layout",["editable","preserve"],profile.Layout);
            if(name=="epub")Choice("epub","EPUB layout",["reflowable","fixed"],profile.EpubLayout);
            var metadata=L10n.Content(new CheckBox{Content="Preserve metadata",IsChecked=profile.PreserveMetadata,Margin=new(4,12,4,12)});if(video||audio||image&&name!="bmp")panel.Children.Add(metadata);
            if(fields.Count+choices.Count==0&&metadata.Parent is null)panel.Children.Add(L10n.Label(new TextBlock{Text="This format uses its original document structure. No export defaults are needed.",TextWrapping=TextWrapping.Wrap,Margin=new(4,18,4,18)}));
            double Value(string key,double fallback)=>fields.TryGetValue(key,out var field)?NumericInput.ParseFinite(field.Text):fallback;
            string Picked(string key,string fallback)=>choices.TryGetValue(key,out var field)?(string)field.SelectedItem:fallback;
            panel.Children.Add(MainWindow.Button("Save defaults",()=>{try{store.SetProfile(name,profile with{Quality=Value("quality",profile.Quality*100)/100,AudioBitrate=checked((int)Value("bitrate",profile.AudioBitrate)),SampleDepth=Picked("depth",profile.SampleDepth),MaxVideoHeight=checked((int)Value("height",profile.MaxVideoHeight)),GifFrameRate=checked((int)Value("fps",profile.GifFrameRate)),GifWidth=checked((int)Value("width",profile.GifWidth)),Dpi=checked((int)Value("dpi",profile.Dpi)),Layout=Picked("layout",profile.Layout),EpubLayout=Picked("epub",profile.EpubLayout),PreserveMetadata=metadata.IsChecked==true});changed();}catch(Exception error)when(error is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Check export defaults"));}}));
            panel.Children.Add(MainWindow.Button("Restore defaults",()=>{try{store.SetProfile(name,new());Render();changed();}catch(Exception error)when(error is IOException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Defaults could not be restored"));}}));
        }
        select.SelectionChanged+=(_,_)=>Render();Render();
    }
}
