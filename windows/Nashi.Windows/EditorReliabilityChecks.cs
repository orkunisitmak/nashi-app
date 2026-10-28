using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nashi.Core;
namespace Nashi.Windows;

internal static class EditorReliabilityChecks
{
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Children(child))yield return nested;}
    }
    internal static void Run(string root,Action<bool,string> check)
    {
        var audio=Path.Combine(root,"Reliability.wav");
        using(var writer=new BinaryWriter(File.Create(audio)))
        {
            const int samples=44100*5;writer.Write("RIFF"u8);writer.Write(36+samples*2);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(44100);writer.Write(88200);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8);writer.Write(samples*2);writer.Write(new byte[samples*2]);
        }
        var owner=new Window{Width=600,Height=400};owner.Show();
        var editor=new ToolEditor(owner,"trimAudio","Trim Audio",audio,new MediaInfo(5,new FileInfo(audio).Length,[new MediaStream(0,"audio","pcm_s16le",null,null,false)]));
        try
        {
            editor.Show();DesktopInput.Pump();
            var play=Children(editor).OfType<Button>().Single(b=>b.Content as string=="Play selected range");
            foreach(var end in new[]{"invalid","NaN","Infinity","-1","0","6"})
            {
                editor.FieldForTest("End").Text=end;
                Exception? failure=null;try{play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}catch(Exception error){failure=error;}
                check(failure is null&&!editor.Playing,"Invalid range playback escaped the editor: "+end+" "+failure?.Message);
            }
            editor.FieldForTest("End").Text="2";play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(editor.Playing,"Valid range playback did not start after correcting invalid input");
            editor.FieldForTest("End").Text="editing";DesktopInput.Pump(250);
            check(editor.IsVisible,"Editing the range during playback closed the editor");
            editor.Timeline.Focus();DesktopInput.Key(editor,Key.Space);
            check(!editor.Playing,"Space did not pause after editing a playing range; active="+editor.IsActive+"; focus="+Keyboard.FocusedElement?.GetType().Name+"; timelineFocused="+editor.Timeline.IsKeyboardFocusWithin);
            editor.FieldForTest("Start").Text="invalid";editor.Timeline.Focus();DesktopInput.Key(editor,Key.O);
            check(editor.IsVisible&&!editor.Playing,"Mark Out crashed on an invalid Start field");
            editor.FieldForTest("Start").Text="0";editor.FieldForTest("End").Text="5";
            var scrub=Children(editor).OfType<Slider>().Single(s=>s.ToolTip as string=="Preview position");scrub.Focus();var value=scrub.Value;
            DesktopInput.Key(editor,Key.Right);
            check(scrub.Value>value&&scrub.Value-value<1&&Math.Abs(editor.PreviewSeconds-scrub.Value)<.05,"Focused scrubber did not seek using its own keyboard increment");
            var silence=Children(editor).OfType<CheckBox>().Single(c=>c.Content as string=="Remove silence at the beginning and end");silence.IsChecked=true;
            Children(editor).OfType<Button>().Single(b=>b.Content as string=="↶ Reset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(silence.IsChecked==false&&!editor.Playing&&editor.FieldForTest("Start").Text=="0"&&editor.FieldForTest("End").Text=="5","Reset retained media options or playback state");
            var preferences=new PreferencesStore(Path.Combine(root,"Reset profiles"));preferences.SetProfile("wav",new(Quality:.62,SampleDepth:"pcm24",PreserveMetadata:false));
            var convert=new ToolEditor(owner,"convert","Convert",audio,new MediaInfo(5,new FileInfo(audio).Length,[new MediaStream(0,"audio","pcm_s16le",null,null,false)]),outputFormats:["wav"]);
            try
            {
                convert.ConfigureProfiles(preferences,"wav");convert.Show();DesktopInput.Pump();convert.FieldForTest("Quality (%)").Text="20";
                Children(convert).OfType<ComboBox>().Single(c=>c.Items.Contains("pcm24")).SelectedItem="pcm16";
                Children(convert).OfType<Button>().Single(b=>b.Content as string=="↶ Reset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(convert.FieldForTest("Quality (%)").Text=="62"&&!convert.PreserveImageMetadata&&Children(convert).OfType<ComboBox>().Single(c=>c.Items.Contains("pcm24")).SelectedItem as string=="pcm24","Reset did not restore the selected format's saved profile");
            }
            finally{convert.Close();}
            var pixels=System.Windows.Media.Imaging.BitmapSource.Create(100,60,96,96,PixelFormats.Bgra32,null,new byte[100*60*4],100*4);pixels.Freeze();
            var image=Path.Combine(root,"Reliability.png");ImageEngine.Save(pixels,image,"png");var photo=new ToolEditor(owner,"editImage","Edit Photo",image,preview:pixels);
            try
            {
                photo.Show();DesktopInput.Pump();photo.FieldForTest("Degrees").Text="invalid";var rotate=Children(photo).OfType<Button>().Single(b=>b.Content as string=="Rotate Right");
                Exception? failure=null;try{rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}catch(Exception error){failure=error;}
                check(failure is null&&photo.FieldForTest("Width").Text=="100","Invalid photo rotation escaped the editor or partially changed dimensions");
                photo.FieldForTest("Degrees").Text="0";rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(photo.FieldForTest("Degrees").Text=="90"&&photo.FieldForTest("Width").Text=="60","Photo rotation did not recover after correcting invalid input");
            }
            finally{photo.Close();}
        }
        finally{editor.Close();owner.Close();}
    }
}
