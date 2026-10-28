using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ImageMagick;
using ImageMagick.Drawing;
using Nashi.Core;
using Nashi.Windows;
namespace Nashi.Linux;

internal static class RedactionUiChecks
{
    internal static async Task Run(Window owner,string directory,Action<bool,string> check,Action<Window,string> capture)
    {
        var fixture=Path.Combine(directory,"redaction-ui-pattern.png");
        using(var pattern=new MagickImage(MagickColors.White,320,240)){var draw=new Drawables().StrokeColor(MagickColors.Transparent);for(var y=0;y<240;y+=4)for(var x=0;x<320;x+=4)draw.FillColor(((x+y)/4)%2==0?MagickColors.Black:MagickColors.White).Rectangle(x,y,x+3,y+3);draw.Draw(pattern);pattern.Write(fixture);}
        var editor=new ToolEditor("redactImage",null,[fixture],new LinuxEngine());var resultTask=editor.ShowDialog<LinuxOptions?>(owner);await Task.Delay(150);
        try{
            var surface=editor.GetVisualDescendants().OfType<ImageSurface>().Single();
            void Gesture(Point start,params Point[] moves)=>AnnotationChecks.PointerGesture(editor,surface,start,moves);
            void Click(string text)=>editor.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string==text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            bool Near(Rect a,Rect b)=>Math.Abs(a.X-b.X)<.001&&Math.Abs(a.Y-b.Y)<.001&&Math.Abs(a.Width-b.Width)<.001&&Math.Abs(a.Height-b.Height)<.001;
            byte[] originalPixels;using(var baseline=new MagickImage(surface.Png()))originalPixels=baseline.ToByteArray(MagickFormat.Rgba);
            Click("Save new copy");check(editor.IsVisible&&editor.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("at least one redaction area",StringComparison.Ordinal)==true),"redaction cannot silently save an unselected full image");
            Gesture(new Point(20,20),new Point(100,80));Gesture(new Point(180,120),new Point(260,180));check(surface.Regions.Length==2,"redaction canvas creates multiple independent areas");
            surface.SelectRegion(0);Gesture(new Point(60,50),new Point(80,60));check(Near(surface.RegionBounds[0],new(40,30,80,60))&&Near(surface.RegionBounds[1],new(180,120,80,60)),"redaction interior drag moves one area");
            Gesture(new Point(120,90),new Point(140,100));check(Near(surface.RegionBounds[0],new(40,30,100,70)),"redaction corner handle resizes selected area");
            Click("Undo");check(Near(surface.RegionBounds[0],new(40,30,80,60)),"redaction undo restores area geometry");Click("Redo");check(Near(surface.RegionBounds[0],new(40,30,100,70)),"redaction redo restores area geometry");
            var style=editor.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="RedactionStyle");
            var areas=editor.GetVisualDescendants().OfType<ListBox>().Single(l=>l.Name=="RedactionAreas");areas.SelectedIndex=1;surface.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Delete});check(surface.Regions.Length==1,"redaction list selection and Delete remove the chosen area");style.SelectedItem="blur";Click("Undo");check(surface.Regions.Length==2,"redaction undo restores a removed area");
            areas.SelectedIndex=0;editor.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="Width").Text="90";check(Near(surface.RegionBounds[0],new(40,30,90,70)),"redaction numeric fields update selected area");
            check(surface.AddRegions(surface.Regions)==0&&surface.Regions.Length==2,"adding detected duplicate regions preserves the area list");
            var added=surface.AddRegions([new(270,20,20,30),new(270,100,20,30)]);check(added==2&&surface.Regions.Length==4,"detected region batch appends without replacing manual areas");Click("Undo");check(surface.Regions.Length==2,"one undo removes an entire detection batch");
            style.SelectedItem="blur";await Wait(()=>!editor.PhotoPreviewBusy,"blur preview");
            using(var rendered=new MagickImage(surface.Png()))using(var pixels=rendered.GetPixels()){var a=pixels.GetPixel(62,50).ToColor()!;var b=pixels.GetPixel(200,140).ToColor()!;check(a.R>20&&a.R<235&&b.R>20&&b.R<235,"live blur preview processes every area");}
            var original=editor.GetVisualDescendants().OfType<CheckBox>().Single(c=>c.Name=="OriginalView");original.IsChecked=true;await Task.Delay(30);using(var rendered=new MagickImage(surface.Png()))check(rendered.ToByteArray(MagickFormat.Rgba).SequenceEqual(originalPixels),"Show original hides processed pixels and area overlays");
            original.IsChecked=false;await Wait(()=>!editor.PhotoPreviewBusy,"restored blur preview");
            Click("Clear");await Wait(()=>!editor.PhotoPreviewBusy,"cleared redaction preview");check(surface.Regions.Length==0&&areas.ItemCount==0,"clear removes redaction areas from canvas and list");Click("Undo");check(surface.Regions.Length==2,"cleared areas can be restored");
            style.SelectedItem="pixelate";await Wait(()=>!editor.PhotoPreviewBusy,"pixelate preview");capture(editor,Path.Combine(directory,"redaction-editing.png"));
            var widthField=editor.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="Width");widthField.Text="-1";Click("Save new copy");check(editor.IsVisible&&!resultTask.IsCompleted,"invalid numeric area cannot silently export stale geometry");widthField.Text="90";
            Click("Save new copy");var options=await resultTask;check(options is not null&&options.Regions.Length==2&&options.RedactionStyle=="pixelate","redaction dialog returns the complete selected area set and style");
            var exported=await new LinuxEngine().RunAsync("redactImage",[fixture],Path.Combine(directory,"redaction-ui-export"),options!);using(var image=new MagickImage(exported.Single()))using(var pixels=image.GetPixels()){var a=pixels.GetPixel(62,50).ToColor()!;var b=pixels.GetPixel(200,140).ToColor()!;check(a.R>20&&a.R<235&&b.R>20&&b.R<235,"saved dialog options process both exported areas");}
        }finally{if(editor.IsVisible)editor.Close((LinuxOptions?)null);}
        if(SystemTools.Find("tesseract") is not null)await CheckDetection(owner,directory,check,capture);
    }
    private static async Task CheckDetection(Window owner,string directory,Action<bool,string> check,Action<Window,string> capture)
    {
        var fixture=Path.Combine(directory,"redaction-ui-text.png");using(var image=new MagickImage(MagickColors.White,800,240)){new Drawables().Font(await FontFiles.ResolveAsync("DejaVu Sans")).FontPointSize(72).FillColor(MagickColors.Black).Text(40,130,"PRIVATE DATA").Draw(image);image.Write(fixture);}
        var editor=new ToolEditor("redactImage",null,[fixture],new LinuxEngine());editor.Show(owner);await Task.Delay(100);
        try{
            var detect=editor.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Detect text");var cancel=editor.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Cancel detection");var languages=editor.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="OcrLanguage");
            await Wait(()=>detect.IsEnabled,"OCR language discovery");check(languages.ItemsSource?.Cast<ToolEditor.OcrLanguage>().Any()==true,"redaction UI discovers installed OCR languages");
            var english=languages.ItemsSource?.Cast<ToolEditor.OcrLanguage>().FirstOrDefault(l=>l.Code=="eng");if(english is null)return;languages.SelectedItem=english;
            var surface=editor.GetVisualDescendants().OfType<ImageSurface>().Single();detect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(!detect.IsEnabled&&cancel.IsEnabled,"text detection exposes active and cancel states");await Wait(()=>detect.IsEnabled,"text region detection");
            check(surface.Regions.Length>=2&&surface.RegionBounds.All(r=>r.Left>=0&&r.Top>=0&&r.Right<=800&&r.Bottom<=240),"OCR button creates source-coordinate editable areas");
            var count=surface.Regions.Length;detect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>detect.IsEnabled,"duplicate text detection");check(surface.Regions.Length==count,"repeated OCR detection does not duplicate existing areas");
            surface.Undo();check(surface.Regions.Length==0,"repeated detection adds no spurious undo entry");surface.Redo();check(surface.Regions.Length==count,"OCR batch can be redone");await Wait(()=>!editor.PhotoPreviewBusy,"detected region preview");capture(editor,Path.Combine(directory,"redaction-detected-text.png"));
            surface.ClearMarks();detect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>detect.IsEnabled,"cancelled text detection");check(surface.Regions.Length==0,"cancelled OCR does not append stale areas");
            detect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));editor.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Clear").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>detect.IsEnabled,"cleared text detection");check(surface.Regions.Length==0,"Clear cancels pending detection and prevents late areas");
            detect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));editor.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Reset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>detect.IsEnabled,"reset text detection");check(surface.Regions.Length==0,"Reset cancels pending detection and prevents late areas");
        }finally{editor.Close();}
    }
    private static async Task Wait(Func<bool> ready,string label)
    {
        var timeout=DateTime.UtcNow.AddSeconds(20);while(!ready()){if(DateTime.UtcNow>timeout)throw new Exception("Timed out waiting for "+label);await Task.Delay(30);}await Task.Delay(30);
    }
}
