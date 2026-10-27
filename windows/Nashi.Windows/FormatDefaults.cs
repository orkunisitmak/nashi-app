using System.Windows;
using System.Windows.Controls;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class SettingsWindow
{
    private void FormatDefaults()
    {
        Heading("Your go-to exports.","Pick a format and set how its new files come out. Files you already made stay as they are.");var formats=FeatureCatalog.Conversions.Values.SelectMany(v=>v).Distinct().Order().ToArray();var select=new ComboBox{ItemsSource=formats,SelectedIndex=0,MinWidth=190,Margin=new(0)};Row("Format",select,"Every new file in this format uses these settings.");var panel=new StackPanel();Add(panel);
        void Render()
        {
            panel.Children.Clear();var done=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new(16,0,0,0)};var name=(string)select.SelectedItem;var profile=store.Profile(name);var fields=new Dictionary<string,TextBox>();var choices=new Dictionary<string,ComboBox>();
            void Number(string key,string title,double value,string hint){var input=new TextBox{Text=value.ToString("0.###",System.Globalization.CultureInfo.CurrentCulture),Width=190};System.Windows.Automation.AutomationProperties.SetName(input,title);fields[key]=input;panel.Children.Add(FormRow(title,input,hint));}
            void Choice(string key,string title,string[] values,string value,string hint){var input=new ComboBox{ItemsSource=values,SelectedItem=value,MinWidth=190,Margin=new(0)};System.Windows.Automation.AutomationProperties.SetName(input,title);choices[key]=input;panel.Children.Add(FormRow(title,input,hint));}
            var audio=MediaEngine.AudioFormats.Contains(name);var video=MediaEngine.VideoFormats.Contains(name)&&name!="gif";var image=new[]{"png","jpg","heic","webp","avif","tiff","bmp"}.Contains(name);
            if(video||new[]{"jpg","heic","webp","avif"}.Contains(name))Number("quality","Quality (%)",profile.Quality*100,"Higher keeps more detail; lower makes smaller files.");
            if(video||audio&&name is not ("wav" or "aiff" or "flac"))Number("bitrate","Audio bitrate (kbps)",profile.AudioBitrate,"128 is fine for speech; 256 or more for music.");
            if(name is "wav" or "aiff")Choice("depth","Audio sample depth",["matchSource","pcm16","pcm24","pcm32","float32"],profile.SampleDepth,"Match source keeps the original depth.");
            if(video)Number("height","Maximum video height (0 = original)",profile.MaxVideoHeight,"Larger videos shrink to this height. 0 keeps the original size.");
            if(name=="gif"){Number("fps","GIF frames per second",profile.GifFrameRate,"More frames look smoother but weigh more.");Number("width","GIF width",profile.GifWidth,"Wider GIFs are sharper and bigger.");}
            if(name is "png" or "jpg" or "epub")Number("dpi","PDF rendering DPI",profile.Dpi,"How sharp PDF pages turn out.");
            if(name is "docx" or "pages")Choice("layout","PDF layout",["editable","preserve"],profile.Layout,"Editable lets you change the text; preserve keeps the page look.");
            if(name=="epub")Choice("epub","EPUB layout",["reflowable","fixed"],profile.EpubLayout,"Reflowable fits any screen; fixed keeps the pages as they are.");
            var metadata=new CheckBox{IsChecked=profile.PreserveMetadata,Style=(Style)FindResource("Switch")};System.Windows.Automation.AutomationProperties.SetName(metadata,"Preserve metadata");var metadataRow=FormRow("Preserve metadata",metadata,"Keeps the date, camera and location details.");if(video||audio||image&&name!="bmp")panel.Children.Add(metadataRow);
            if(fields.Count+choices.Count==0&&metadataRow.Parent is null)panel.Children.Add(L10n.Label(new TextBlock{Text="This format uses its original document structure. No export defaults are needed.",TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,10),Foreground=Theme.Brush("Muted")}));var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,10,0,6)};
            double Value(string key,double fallback)=>fields.TryGetValue(key,out var field)?NumericInput.ParseFinite(field.Text):fallback;
            string Picked(string key,string fallback)=>choices.TryGetValue(key,out var field)?(string)field.SelectedItem:fallback;
            buttons.Children.Add(MainWindow.Primary("Save defaults",()=>{try{store.SetProfile(name,profile with{Quality=Value("quality",profile.Quality*100)/100,AudioBitrate=checked((int)Value("bitrate",profile.AudioBitrate)),SampleDepth=Picked("depth",profile.SampleDepth),MaxVideoHeight=checked((int)Value("height",profile.MaxVideoHeight)),GifFrameRate=checked((int)Value("fps",profile.GifFrameRate)),GifWidth=checked((int)Value("width",profile.GifWidth)),Dpi=checked((int)Value("dpi",profile.Dpi)),Layout=Picked("layout",profile.Layout),EpubLayout=Picked("epub",profile.EpubLayout),PreserveMetadata=metadata.IsChecked==true});changed();Confirm(L10n.Format("Saved for every new {0}.",name.ToUpperInvariant()));}catch(Exception error)when(error is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Check export defaults"));}}));
            buttons.Children.Add(MainWindow.Button("Restore defaults",()=>{try{store.SetProfile(name,new());Render();changed();Confirm(L10n.Text("Back to Nashi’s defaults."));}catch(Exception error)when(error is IOException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Defaults could not be restored"));}}));
            ((FrameworkElement)buttons.Children[1]).Margin=new(10,0,0,0);buttons.Children.Add(done);panel.Children.Add(buttons);
            void Confirm(string text){done.Children.Clear();var mark=Icons.Create("check-circle",16,"Gold");mark.Margin=new(0,0,6,0);done.Children.Add(mark);var note=new TextBlock{Text=text,FontSize=12,VerticalAlignment=VerticalAlignment.Center};note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");done.Children.Add(note);Motion.Pop(done);}
        }
        select.SelectionChanged+=(_,_)=>Render();Render();
    }
}
