using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Nashi.Core;
namespace Nashi.Windows;
public sealed partial class SettingsWindow
{
    private static readonly string[] Families=["Images","Documents","Audio","Video"];
    private static string Family(string format)=>new[]{"png","jpg","heic","webp","avif","tiff","bmp","gif","ico","svg"}.Contains(format)?"Images":MediaEngine.AudioFormats.Contains(format)?"Audio":MediaEngine.VideoFormats.Contains(format)?"Video":"Documents";
    // Formats: a searchable, grouped list on the left; the chosen format's export defaults on the right, saved as they change.
    private void FormatDefaults()
    {
        formats.Children.Clear();formats.ColumnDefinitions.Clear();formats.ColumnDefinitions.Add(new(){Width=new(172)});formats.ColumnDefinitions.Add(new());
        // Everyday formats first in each family, as in the design; the rest follow alphabetically.
        string[] usual=["jpg","png","webp","heic","tiff","avif","gif","bmp","pdf","docx","epub","mp3","m4a","wav","mp4","mov"];
        var all=FeatureCatalog.Conversions.Values.SelectMany(v=>v).Distinct().OrderBy(f=>Array.IndexOf(Families,Family(f))).ThenBy(f=>Array.IndexOf(usual,f) is var i&&i>=0?i:usual.Length).ThenBy(f=>f).ToArray();
        var view=new ListCollectionView(all);view.GroupDescriptions.Add(new PropertyGroupDescription(null,new FamilyName()));
        var select=new ComboBox{ItemsSource=view,Style=Ui.Style("FormatList"),SelectedIndex=0};select.DropDownOpened+=(_,_)=>select.IsDropDownOpen=false;
        var heading=new FrameworkElementFactory(typeof(TextBlock));var text=new MultiBinding{Converter=(IMultiValueConverter)Application.Current.FindResource("ChoiceText")};text.Bindings.Add(new Binding("Name"));text.Bindings.Add(new Binding(nameof(LanguageState.Language)){Source=LanguageState.Shared});heading.SetBinding(TextBlock.TextProperty,text);heading.SetValue(TextBlock.FontSizeProperty,11d);heading.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);heading.SetValue(FrameworkElement.MarginProperty,new Thickness(8,12,8,4));heading.SetResourceReference(TextBlock.ForegroundProperty,"Faint");
        select.GroupStyle.Add(new GroupStyle{HeaderTemplate=new DataTemplate{VisualTree=heading}});
        var search=new TextBox{Style=Ui.Style("Field"),MinHeight=30,Margin=new(2,0,2,6)};Ui.SetIcon(search,"search",14);L10n.Bind(search,Ui.HintProperty,"Search formats");search.TextChanged+=(_,_)=>{var query=search.Text.Trim();view.Filter=query.Length==0?null:item=>((string)item).Contains(query,StringComparison.OrdinalIgnoreCase);};
        var list=new DockPanel{Margin=new(8,18,8,10)};DockPanel.SetDock(search,Dock.Top);list.Children.Add(search);list.Children.Add(select);
        var column=new Border{BorderThickness=new(0,0,1,0),Child=list};column.SetResourceReference(Border.BorderBrushProperty,"Line");formats.Children.Add(column);
        var panel=new StackPanel{Margin=new(22,18,28,24),MaxWidth=600};var right=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetColumn(right,1);formats.Children.Add(right);
        void Render()
        {
            panel.Children.Clear();target=panel;group=null;if(select.SelectedItem is not string name)return;var profile=store.Profile(name);var fields=new Dictionary<string,TextBox>();var choices=new Dictionary<string,ComboBox>();var label=name.ToUpperInvariant();
            var title=Ui.Text($"{label} Export",24,weight:FontWeights.Bold);title.Margin=new(4,0,4,4);panel.Children.Add(title);var sub=Ui.Text($"Used whenever Nashi saves a {label}.",12,"Muted");sub.Margin=new(4,0,4,14);panel.Children.Add(sub);
            var audio=MediaEngine.AudioFormats.Contains(name);var video=MediaEngine.VideoFormats.Contains(name)&&name!="gif";var image=new[]{"png","jpg","heic","webp","avif","tiff","bmp"}.Contains(name);var lossy=video||new[]{"jpg","heic","webp","avif"}.Contains(name);
            OutputProfile Current()=>profile with{AudioBitrate=checked((int)Value("bitrate",profile.AudioBitrate)),SampleDepth=Picked("depth",profile.SampleDepth),MaxVideoHeight=checked((int)Value("height",profile.MaxVideoHeight)),GifFrameRate=checked((int)Value("fps",profile.GifFrameRate)),GifWidth=checked((int)Value("width",profile.GifWidth)),Dpi=checked((int)Value("dpi",profile.Dpi)),Layout=Picked("layout",profile.Layout),EpubLayout=Picked("epub",profile.EpubLayout)};
            void Keep(OutputProfile next){try{store.SetProfile(name,next);profile=next;changed();}catch(Exception error)when(error is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Check export defaults"));}}
            void Number(string key,string title,double value){var input=new TextBox{Text=value.ToString("0.###",System.Globalization.CultureInfo.CurrentCulture),Width=90,TextAlignment=TextAlignment.Right};input.LostKeyboardFocus+=(_,_)=>{try{Keep(Current());}catch(Exception error)when(error is ArgumentException or FormatException or OverflowException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Check export defaults"));}};fields[key]=input;Row(title,input);}
            void Choice(string key,string title,string[] values,string value){var input=new ComboBox{ItemsSource=values,SelectedItem=value};input.SelectionChanged+=(_,_)=>{try{Keep(Current());}catch(Exception error)when(error is ArgumentException or FormatException or OverflowException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Check export defaults"));}};choices[key]=input;Row(title,input);}
            // Quality reads as three presets, like the design; a value set elsewhere simply highlights none.
            if(lossy){var presets=new[]{("smaller",.7),("balanced",.85),("higher",.95)};var quality=new ComboBox{ItemsSource=presets.Select(p=>p.Item1).ToArray(),Style=Ui.Style("SegmentedCombo"),SelectedItem=presets.FirstOrDefault(p=>Math.Abs(p.Item2-profile.Quality)<.005).Item1};quality.DropDownOpened+=(_,_)=>quality.IsDropDownOpen=false;quality.SelectionChanged+=(_,_)=>{if(quality.SelectedItem is string chosen)Keep(profile with{Quality=presets.First(p=>p.Item1==chosen).Item2});};Row("Quality",quality);}
            if(video||audio&&name is not ("wav" or "aiff" or "flac"))Number("bitrate","Audio bitrate (kbps)",profile.AudioBitrate);
            if(name is "wav" or "aiff")Choice("depth","Audio sample depth",["matchSource","pcm16","pcm24","pcm32","float32"],profile.SampleDepth);
            if(video)Number("height","Maximum video height (0 = original)",profile.MaxVideoHeight);
            if(name=="gif"){Number("fps","GIF frames per second",profile.GifFrameRate);Number("width","GIF width",profile.GifWidth);}
            if(name is "png" or "jpg" or "epub")Number("dpi","PDF rendering DPI",profile.Dpi);
            if(name is "docx" or "pages")Choice("layout","PDF layout",["editable","preserve"],profile.Layout);
            if(name=="epub")Choice("epub","EPUB layout",["reflowable","fixed"],profile.EpubLayout);
            var metadata=Switch("Preserve metadata",profile.PreserveMetadata,v=>Keep(profile with{PreserveMetadata=v}));if(video||audio||image&&name!="bmp")Row(metadata);
            if(image){Group("Compression");var strength=new ComboBox{ItemsSource=new[]{"balanced","strong"},Style=Ui.Style("SegmentedCombo"),SelectedItem=profile.Compression};strength.DropDownOpened+=(_,_)=>strength.IsDropDownOpen=false;strength.SelectionChanged+=(_,_)=>{if(strength.SelectedItem is string chosen)Keep(profile with{Compression=chosen});};Row("Quality",strength);}
            if(fields.Count+choices.Count==0&&metadata.Parent is null&&!lossy){var note=Ui.Text("This format uses its original document structure. No export defaults are needed.",13,"Muted");note.Margin=new(4,4,4,12);panel.Children.Add(note);}
            double Value(string key,double fallback)=>fields.TryGetValue(key,out var field)?NumericInput.ParseFinite(field.Text):fallback;
            string Picked(string key,string fallback)=>choices.TryGetValue(key,out var field)?(string)field.SelectedItem:fallback;
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
            buttons.Children.Add(Action("Restore Defaults",()=>{try{store.SetProfile(name,new());Render();changed();}catch(Exception error)when(error is IOException or UnauthorizedAccessException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Defaults could not be restored"));}}));
            var save=Action("Save defaults",()=>{try{Keep(Current() with{PreserveMetadata=metadata.IsChecked==true});}catch(Exception error)when(error is ArgumentException or FormatException or OverflowException){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("Check export defaults"));}});save.Margin=new(8,0,0,0);buttons.Children.Add(save);target=page;
        }
        select.SelectionChanged+=(_,_)=>Render();Render();
    }
    private sealed class FamilyName:IValueConverter
    {
        public object Convert(object value,Type type,object parameter,System.Globalization.CultureInfo culture)=>Family((string)value);
        public object ConvertBack(object value,Type type,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
}
