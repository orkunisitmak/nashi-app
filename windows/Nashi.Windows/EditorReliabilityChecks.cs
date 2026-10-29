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
                photo.AcceptForTest();var turned=ImageEngine.Load(ImageEngine.Apply("editImage",[image],root,photo.ImageOptions)[0]);
                check(turned.PixelWidth==60&&turned.PixelHeight==100,$"Rotate Right distorted the photo to {turned.PixelWidth}×{turned.PixelHeight}");
                Children(photo).OfType<Button>().Single(b=>b.Content as string=="↶ Reset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));photo.FieldForTest("Degrees").Text="45";DesktopInput.Pump(1500);photo.AcceptForTest();
                var tilted=ImageEngine.Load(ImageEngine.Apply("editImage",[image],root,photo.ImageOptions)[0]);
                check(photo.IsVisible&&tilted.PixelWidth==114&&tilted.PixelHeight==114,$"A 45° photo rotation crashed the preview or lost the turned corners: {tilted.PixelWidth}×{tilted.PixelHeight}");
                var shown=(System.Windows.Media.Imaging.BitmapSource)((Image)Children(photo).OfType<Canvas>().First(c=>c.Children.Count>0&&c.Children[0] is Image).Children[0]).Source;
                check(shown.PixelWidth==114&&shown.PixelHeight==114,$"The 45° preview ({shown.PixelWidth}×{shown.PixelHeight}) differs from the exported photo");
            }
            finally{photo.Close();}
            var single=new ToolEditor(owner,"resize","Resize",image,preview:pixels);
            try
            {
                single.Show();DesktopInput.Pump();single.FieldForTest("Height").Text="30";single.AcceptForTest();var exact=ImageEngine.Load(ImageEngine.Apply("resize",[image],root,single.ImageOptions)[0]);
                check(exact.PixelWidth==100&&exact.PixelHeight==30,$"Resizing one image ignored the typed size: {exact.PixelWidth}×{exact.PixelHeight}");
            }
            finally{single.Close();}
            // Default ranges show milliseconds and can round just past ffprobe's duration.
            var rounded=new ToolEditor(owner,"trimAudio","Trim Audio",audio,new MediaInfo(4.9876,new FileInfo(audio).Length,[new MediaStream(0,"audio","pcm_s16le",null,null,false)]));
            try
            {
                rounded.Show();DesktopInput.Pump();Exception? failure=null;try{rounded.AcceptForTest();}catch(Exception error){failure=error;}
                check(failure is null&&rounded.MediaOptions.End<=4.9876,"The untouched trim range was rejected or ran past the media: End="+rounded.FieldForTest("End").Text+" "+failure?.Message);
                rounded.FieldForTest("Start").Text="4.9875";rounded.FieldForTest("End").Text="4.988";failure=null;try{rounded.AcceptForTest();}catch(ArgumentException error){failure=error;}
                check(failure is not null,"A range that is too short once End is clamped to the media was accepted");
                rounded.FieldForTest("Start").Text="1.1";rounded.FieldForTest("End").Text="1.15";failure=null;try{rounded.AcceptForTest();}catch(ArgumentException error){failure=error;}
                check(failure is null,"A 50 ms range was refused by floating-point rounding: "+failure?.Message);
            }
            finally{rounded.Close();}
            var bleep=new ToolEditor(owner,"redactAudio","Bleep Audio",audio,new MediaInfo(5,new FileInfo(audio).Length,[new MediaStream(0,"audio","pcm_s16le",null,null,false)]));
            try
            {
                bleep.Show();DesktopInput.Pump();bleep.FieldForTest("Start").Text="1";bleep.FieldForTest("End").Text="2";
                Children(bleep).OfType<Button>().Single(b=>b.Content as string=="Add selected range").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                bleep.FieldForTest("Start").Text="3";bleep.FieldForTest("End").Text="1";Exception? failure=null;try{bleep.AcceptForTest();}catch(Exception error){failure=error;}
                check(failure is null&&bleep.MediaOptions.AudioRedactions is [{Start:1,End:2}],"Bleep Audio checked the playback selection instead of the added ranges: "+failure?.Message);
            }
            finally{bleep.Close();}
            var tall=System.Windows.Media.Imaging.BitmapSource.Create(60,100,96,96,PixelFormats.Bgra32,null,new byte[60*100*4],60*4);var portrait=Path.Combine(root,"Reliability portrait.png");ImageEngine.Save(tall,portrait,"png");
            var batch=new ToolEditor(owner,"compress","Compress",image,preview:pixels,inputs:[image,portrait]);
            try
            {
                batch.Show();DesktopInput.Pump();batch.AcceptForTest();var kept=ImageEngine.Apply("compress",[image,portrait],root,batch.ImageOptions).Select(ImageEngine.Load).ToArray();
                check(kept[0].PixelWidth==100&&kept[0].PixelHeight==60&&kept[1].PixelWidth==60&&kept[1].PixelHeight==100,"Batch compression stretched images to the first image's size");
                Children(batch).OfType<ComboBox>().Single(c=>c.Items.Contains("50%")).SelectedItem="50%";batch.AcceptForTest();var halved=ImageEngine.Apply("compress",[image,portrait],root,batch.ImageOptions).Select(ImageEngine.Load).ToArray();
                check(halved[0].PixelWidth==50&&halved[0].PixelHeight==30&&halved[1].PixelWidth==30&&halved[1].PixelHeight==50,"A size preset was not applied to each image's own dimensions");
            }
            finally{batch.Close();}
            var widths=new ToolEditor(owner,"resize","Resize",image,preview:pixels,inputs:[image,portrait]);
            try
            {
                widths.Show();DesktopInput.Pump();widths.FieldForTest("Width").Text="50";widths.AcceptForTest();var narrowed=ImageEngine.Apply("resize",[image,portrait],root,widths.ImageOptions).Select(ImageEngine.Load).ToArray();
                check(narrowed[0].PixelWidth==50&&narrowed[0].PixelHeight==30&&narrowed[1].PixelWidth==50&&narrowed[1].PixelHeight==83,"A batch width change did not keep each image's proportions");
            }
            finally{widths.Close();}
            var flattened=Path.Combine(root,"Reliability transparent.jpg");ImageEngine.Save(pixels,flattened,"jpg");var corner=new byte[4];new System.Windows.Media.Imaging.FormatConvertedBitmap(ImageEngine.Load(flattened),PixelFormats.Bgra32,null,0).CopyPixels(new Int32Rect(0,0,1,1),corner,4,0);
            check(corner[0]>245&&corner[1]>245&&corner[2]>245,"Transparent pixels turned black in a JPEG export");
            var cover=new ToolEditor(owner,"redactImage","Redact Photo",image,preview:pixels);
            try
            {
                cover.Show();DesktopInput.Pump();var canvas=Children(cover).OfType<Canvas>().First(c=>c.Children.Count>0&&c.Children[0] is Image);var shapes=canvas.Children.Count;
                foreach(var routed in new[]{UIElement.MouseLeftButtonDownEvent,UIElement.MouseLeftButtonUpEvent})canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=routed});
                check(canvas.Children.Count==shapes,"A click without a drag left a redaction box that is never exported");
            }
            finally{cover.Close();}
            var video=new ToolEditor(owner,"redactVideo","Redact Video",audio,new MediaInfo(5,new FileInfo(audio).Length,[new MediaStream(0,"video","h264",100,60,false)]),pixels);
            try
            {
                video.Show();DesktopInput.Pump();Exception? failure=null;try{video.AcceptForTest();}catch(ArgumentException error){failure=error;}
                check(failure?.Message=="Drag an area first.","Redact Video accepted a save without any drawn area");
            }
            finally{video.Close();}
        }
        finally{editor.Close();owner.Close();}
    }
}
