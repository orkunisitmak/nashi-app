using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageMagick;
using Nashi.Windows;
namespace Nashi.Linux;

internal static class AnnotationChecks
{
    internal static async Task Run(Window owner,string fixture,string directory,Action<bool,string> check,Action<Window,string> capture)
    {
        var originalHash=SHA256.HashData(await File.ReadAllBytesAsync(fixture));
        var editor=new ToolEditor("annotateImage",null,[fixture],new LinuxEngine());editor.Show(owner);await Task.Delay(150);
        try{
            var surface=editor.GetVisualDescendants().OfType<ImageSurface>().Single();
            var tools=editor.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.ItemsSource?.Cast<object>().Contains("Select / Move")==true);
            void Tool(string kind){tools.SelectedItem=kind;}
            void Field(string name,string text){editor.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name==name).Text=text;}
            void Gesture(Point start,params Point[] moves)=>PointerGesture(editor,surface,start,moves);
            bool Near(Rect a,Rect b)=>Math.Abs(a.X-b.X)<.001&&Math.Abs(a.Y-b.Y)<.001&&Math.Abs(a.Width-b.Width)<.001&&Math.Abs(a.Height-b.Height)<.001;
            static IMagickColor<byte> Pixel(byte[] bytes,int x,int y){using var image=new MagickImage(bytes);using var pixels=image.GetPixels();return pixels.GetPixel(x,y).ToColor()!;}
            bool Red(byte[] bytes,int x,int y){var c=Pixel(bytes,x,y);return c.R>200&&c.G<60&&c.B<60;}
            Field("Ink","#FF0000");Field("StrokeWidth","4");Field("FontSize","20");Field("Text","Nashi");
            var font=editor.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.ItemsSource?.Cast<object>().Contains("serif")==true);font.SelectedItem="serif";
            Tool("Rectangle");Gesture(new Point(20,20),new Point(100,80));await Task.Delay(80);
            check(surface.Annotations.Length==1&&Near(surface.Annotations[0].SourceBounds,new(20,20,80,60)),"annotation rectangle uses source coordinates");
            check(Red(surface.Png(),20,40),"annotation rectangle renders intended pixels");
            var first=surface.Annotations[0];Tool("Select / Move");Gesture(new Point(50,50),new Point(70,60));await Task.Delay(80);
            check(Near(first.SourceBounds,new(40,30,80,60)),"annotation interior drag moves only its own rectangle");
            check(Red(surface.Png(),40,50)&&!Red(surface.Png(),20,40),"annotation move updates exported pixels");
            Gesture(new Point(120,90),new Point(160,130));await Task.Delay(80);check(Near(first.SourceBounds,new(40,30,120,100)),"annotation southeast handle resize");
            surface.Undo();check(Near(first.SourceBounds,new(40,30,80,60)),"undo restores annotation resize");surface.Redo();check(Near(first.SourceBounds,new(40,30,120,100)),"redo restores annotation resize");
            surface.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Z,KeyModifiers=KeyModifiers.Control});check(Near(first.SourceBounds,new(40,30,80,60)),"Ctrl+Z routes to annotation undo");
            surface.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Z,KeyModifiers=KeyModifiers.Control|KeyModifiers.Shift});check(Near(first.SourceBounds,new(40,30,120,100)),"Ctrl+Shift+Z routes to annotation redo");
            surface.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Delete});check(surface.Annotations.Length==0,"Delete routes to selected annotation removal");surface.Undo();check(surface.Annotations.Single()==first,"undo restores deleted annotation");
            surface.ClearMarks();check(surface.Annotations.Length==0,"clear annotation layer");surface.Undo();check(surface.Annotations.Single()==first,"undo restores cleared layer");
            Tool("Rectangle");await Task.Delay(50);var unselected=surface.Png();Tool("Select / Move");surface.SelectAnnotation(0);await Task.Delay(50);
            check(SHA256.HashData(unselected).SequenceEqual(SHA256.HashData(surface.Png())),"selection handles are excluded from annotation PNG");
            Tool("Rectangle");Gesture(new Point(60,40),new Point(140,100));check(surface.Annotations.Length==2,"drawing over an existing annotation creates another mark");
            var second=surface.Annotations[1];Tool("Select / Move");Gesture(new Point(85,65),new Point(105,75));check(Near(second.SourceBounds,new(80,50,80,60))&&Near(first.SourceBounds,new(40,30,120,100)),"overlapping annotation selection chooses the top mark");
            surface.Reset();Tool("Line");Gesture(new Point(20,120),new Point(120,150));Tool("Select / Move");Gesture(new Point(70,135),new Point(90,145));
            check(Near(surface.Annotations.Single().SourceBounds,new(40,130,100,30)),"line can be independently selected and moved");
            Gesture(new Point(140,160),new Point(240,190));check(Near(surface.Annotations.Single().SourceBounds,new(40,130,200,60))&&Red(surface.Png(),220,184),"line resize scales local points and exported pixels");
            surface.Reset();Tool("Freehand");Gesture(new Point(20,20),new Point(60,60),new Point(100,20));Tool("Select / Move");Gesture(new Point(60,40),new Point(80,60));Gesture(new Point(120,80),new Point(200,120));await Task.Delay(80);
            check(Near(surface.Annotations.Single().SourceBounds,new(40,40,160,80))&&Red(surface.Png(),120,120),"freehand resize retains all path points");
            var overlay=Path.Combine(directory,"annotation-overlay.png");using(var green=new MagickImage(MagickColors.Lime,80,80))green.Write(overlay);surface.InsertImage=overlay;
            foreach(var kind in new[]{"Marker","Rectangle","Arrow","Line","Ellipse","Text","Number","Callout","Magnifier","Image","Freehand"}){
                surface.Reset();Tool(kind);Gesture(new Point(20,20),new Point(60,60),new Point(100,80));
                var mark=surface.Annotations.Single();check(mark.Kind==kind&&mark.Font=="serif"&&mark.StrokeWidth==4&&mark.TextSize==20,"annotation style captured: "+kind);
                Tool("Select / Move");Gesture(new Point(100,80),new Point(140,110));await Task.Delay(60);check(Near(mark.SourceBounds,new(20,20,120,90)),"annotation handle resizes "+kind);
                var png=surface.Png();using(var image=new MagickImage(png)){check(image.Width==320&&image.Height==240,"annotation export size: "+kind);}
                if(kind=="Number")check(Red(png,45,65),"number annotation has a filled badge");
                if(kind=="Callout")check(Red(png,45,40),"callout annotation has a filled body");
                if(kind=="Image"){var color=Pixel(png,80,60);check(color.G>200&&color.R<30,"inserted image survives resize/export");}
                await File.WriteAllBytesAsync(Path.Combine(directory,"annotation-"+kind+".png"),png);
            }
            surface.Reset();Tool("Rectangle");Field("Ink","bad-color");Gesture(new Point(20,20),new Point(100,80));check(surface.Annotations.Length==0,"invalid annotation color is rejected without creating a mark");
            Field("Ink","#FF0000");Field("StrokeWidth","NaN");Gesture(new Point(20,20),new Point(100,80));check(surface.Annotations.Length==0,"nonfinite annotation stroke is rejected");Field("StrokeWidth","4");
            Tool("Image");surface.InsertImage=null;Gesture(new Point(20,20),new Point(100,80));check(surface.Annotations.Length==0,"image annotation requires chosen artwork");surface.InsertImage=overlay;
            surface.Reset();var kinds=new[]{"Rectangle","Ellipse","Arrow","Line","Text","Number","Callout","Magnifier","Image","Marker","Freehand"};Field("FontSize","14");
            for(var index=0;index<kinds.Length;index++){Tool(kinds[index]);var x=12+index%4*76;var y=12+index/4*72;Gesture(new Point(x,y),new Point(x+56,y+50));}
            Tool("Select / Move");surface.SelectAnnotation(0);await Task.Delay(100);capture(editor,Path.Combine(directory,"annotation-editing.png"));
            var annotated=surface.Png();var exported=await new LinuxEngine().RunAsync("annotateImage",[fixture],Path.Combine(directory,"annotation-export"),new(){AnnotatedPng=annotated});
            using(var rendered=new MagickImage(annotated))using(var saved=new MagickImage(exported.Single()))check(rendered.ToByteArray(MagickFormat.Rgba).SequenceEqual(saved.ToByteArray(MagickFormat.Rgba)),"annotation engine publishes the rendered pixels");
            check(SHA256.HashData(await File.ReadAllBytesAsync(fixture)).SequenceEqual(originalHash),"annotation export preserves the source file");
            var list=editor.GetVisualDescendants().OfType<ListBox>().Single(l=>l.Name=="Annotations");check(list.ItemCount==kinds.Length,"annotation list exposes every independently editable mark");
            list.SelectedIndex=5;check(surface.SelectedAnnotation==5,"annotation list selects an individual mark");surface.DeleteSelected();check(surface.Annotations.Length==kinds.Length-1&&surface.Annotations.All(m=>m.Kind!="Number"),"list selection deletes only the requested mark");surface.Undo();check(surface.Annotations[5].Kind=="Number","undo restores annotation ordering");
        }finally{editor.Close();}
        await CheckMarkerAndMagnifier(owner,directory,check);
    }
    private static async Task CheckMarkerAndMagnifier(Window owner,string directory,Action<bool,string> check)
    {
        var fixture=Path.Combine(directory,"blue-annotation-source.png");using(var blue=new MagickImage(MagickColors.Blue,320,240))blue.Write(fixture);
        var editor=new ToolEditor("annotateImage",null,[fixture],new LinuxEngine());editor.Show(owner);await Task.Delay(100);
        try{
            var surface=editor.GetVisualDescendants().OfType<ImageSurface>().Single();var tools=editor.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.ItemsSource?.Cast<object>().Contains("Select / Move")==true);
            editor.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="Ink").Text="#FF0000";
            tools.SelectedItem="Marker";PointerGesture(editor,surface,new(20,100),[new(160,100)]);await Task.Delay(80);
            using(var rendered=new MagickImage(surface.Png()))using(var pixels=rendered.GetPixels()){var color=pixels.GetPixel(80,100).ToColor()!;check(color.R<5&&color.G<5&&color.B>150&&color.B<180,"marker multiplies its ink with source pixels at original opacity");}
            surface.Reset();tools.SelectedItem="Number";PointerGesture(editor,surface,new(0,0),[new(320,240)]);
            tools.SelectedItem="Magnifier";PointerGesture(editor,surface,new(40,40),[new(200,160)]);await Task.Delay(80);
            using(var rendered=new MagickImage(surface.Png()))using(var pixels=rendered.GetPixels()){
                var inside=pixels.GetPixel(120,100).ToColor()!;var corner=pixels.GetPixel(50,50).ToColor()!;
                check(inside.B>240&&inside.R<5&&inside.G<5,"magnifier samples the original image instead of other annotations");
                check(corner.R>240&&corner.B<5,"magnifier clips to an ellipse and retains lower marks outside it");
            }
            await File.WriteAllBytesAsync(Path.Combine(directory,"annotation-magnifier-layering.png"),surface.Png());
        }finally{editor.Close();}
    }
    private static void PointerGesture(Window editor,ImageSurface surface,Point start,IReadOnlyList<Point> moves)
    {
        var drawing=surface.GetVisualDescendants().OfType<Canvas>().First(c=>c.Children.OfType<Image>().Any());var pointer=new Pointer(73,PointerType.Mouse,true);Point Root(Point point)=>drawing.TranslatePoint(point,editor)!.Value;
        var down=new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed);
        drawing.RaiseEvent(new PointerPressedEventArgs(drawing,pointer,editor,Root(start),1,down,KeyModifiers.None,1));
        foreach(var point in moves)drawing.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,drawing,pointer,editor,Root(point),2,down,KeyModifiers.None));
        drawing.RaiseEvent(new PointerReleasedEventArgs(drawing,pointer,editor,Root(moves.LastOrDefault(start)),3,new PointerPointProperties(),KeyModifiers.None,MouseButton.Left));
    }
}
