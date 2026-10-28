using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Nashi.Core;
using Nashi.Windows;
namespace Nashi.Linux;

internal static class ProfileChecks
{
    internal static async Task Run(Window owner,string image,string directory,Action<bool,string> check,Action<Window,string> capture)
    {
        var state=Path.Combine(directory,"profile-state");var preferences=new PreferencesStore(state);
        preferences.SetProfile("jpg",new(Quality:.37,PreserveMetadata:false));preferences.SetProfile("webp",new(Quality:.68));
        var defaults=new FormatDefaultsWindow(preferences);defaults.Show(owner);await Task.Delay(80);
        try{
            var format=defaults.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="ExportFormat");format.SelectedItem="jpg";
            var quality=defaults.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="quality");quality.Text="44";
            defaults.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Save defaults").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(new PreferencesStore(state).Profile("jpg").Quality==.44,"Settings format defaults persist without selecting an input file");
            capture(defaults,Path.Combine(directory,"export-defaults.png"));format.SelectedItem="gif";check(defaults.GetVisualDescendants().OfType<TextBox>().Any(t=>t.Name=="fps")&&!defaults.GetVisualDescendants().OfType<TextBox>().Any(t=>t.Name=="quality"),"Settings exposes format-specific GIF controls");
            defaults.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="fps").Text="NaN";defaults.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Save defaults").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(new PreferencesStore(state).Profile("gif").GifFrameRate==15,"Settings rejects nonfinite default values");
            format.SelectedItem="jpg";defaults.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Restore defaults").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(new PreferencesStore(state).Profile("jpg")==new OutputProfile(),"Settings restores original format defaults");
        }finally{defaults.Close();}
        preferences.SetProfile("jpg",new(Quality:.37,PreserveMetadata:false));
        var editor=new ToolEditor("convert","jpg",[image],new LinuxEngine(),preferences);var result=editor.ShowDialog<LinuxOptions?>(owner);await Task.Delay(80);
        TextBox Text(string name)=>editor.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name==name);
        void Click(string text)=>editor.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string==text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try{
            check(Text("Quality").Text=="37"&&!editor.GetVisualDescendants().OfType<CheckBox>().Single(c=>c.Name=="PreserveImageMetadata").IsChecked!.Value,"conversion editor applies persisted image quality and metadata defaults");
            Text("Quality").Text="52";Click("Reset");check(Text("Quality").Text=="37","Reset restores the selected format profile");
            Text("Quality").Text="61";Click("Save defaults for JPG");var reloaded=new PreferencesStore(state);
            check(reloaded.Profile("jpg").Quality==.61&&reloaded.Profile("webp").Quality==.68,"saving defaults persists only the selected format");
            Text("Quality").Text="0";Click("Save defaults for JPG");check(new PreferencesStore(state).Profile("jpg").Quality==.61,"invalid export defaults cannot overwrite the saved profile");
            Click("Reset");Click("Save new copy");check((await result)?.Quality==61,"conversion export returns the saved quality after Reset");
        }finally{if(editor.IsVisible)editor.Close((LinuxOptions?)null);}
        var text=Path.Combine(directory,"profile-document.txt");await File.WriteAllTextAsync(text,"Nashi profile document");preferences.SetProfile("pdf",new(Dpi:200,Layout:"preserve",EpubLayout:"fixed"));
        editor=new ToolEditor("convert","pdf",[text],new LinuxEngine(),preferences);result=editor.ShowDialog<LinuxOptions?>(owner);await Task.Delay(80);
        try{check(!editor.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="Document_Layout").IsVisible&&!Text("Document_Dpi").IsVisible,"document conversion hides format-inapplicable controls");Click("Save new copy");var options=await result;check(options?.Document is {Dpi:200,Layout:"preserve",EpubLayout:"fixed"},"document editor applies persisted DPI and layout defaults");}
        finally{if(editor.IsVisible)editor.Close((LinuxOptions?)null);}
        var video=Path.Combine(directory,"video.mp4");if(!File.Exists(video))return;
        preferences.SetProfile("mp4",new(Quality:.42,AudioBitrate:192,SampleDepth:"pcm24",MaxVideoHeight:120,GifFrameRate:8,GifWidth:640,PreserveMetadata:false));
        editor=new ToolEditor("convert","mp4",[video],new LinuxEngine(),preferences);result=editor.ShowDialog<LinuxOptions?>(owner);await Task.Delay(80);
        try{check(Text("Media_Quality").IsVisible&&!Text("Media_GifWidth").IsVisible,"MP4 conversion hides GIF-only controls");Click("Save new copy");var options=await result;check(options?.Media is {Quality:.42,AudioBitrate:192,SampleDepth:"pcm24",Height:120,GifFrameRate:8,GifWidth:640,PreserveMetadata:false},"media editor applies persisted quality, dimensions, codec and metadata defaults");}
        finally{if(editor.IsVisible)editor.Close((LinuxOptions?)null);}
        editor=new ToolEditor("convert","gif",[video],new LinuxEngine(),preferences);editor.Show(owner);await Task.Delay(80);
        try{check(Text("Media_GifWidth").IsVisible&&!Text("Media_Quality").IsVisible&&!editor.GetVisualDescendants().OfType<CheckBox>().Single(c=>c.Name=="Media_PreserveMetadata").IsVisible,"GIF conversion shows GIF controls and hides incompatible metadata and quality");capture(editor,Path.Combine(directory,"gif-profile-controls.png"));}
        finally{editor.Close();}
    }
}
