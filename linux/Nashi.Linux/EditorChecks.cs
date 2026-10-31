using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;
namespace Nashi.Linux;

/// The editors driven with real pointer and keyboard events on Avalonia's headless platform:
/// `Nashi --verify-editors <empty folder>`. Nothing is shown on screen.
internal static class EditorChecks
{
    private static int passed;
    public static int Run(string root)
    {
        try
        {
            root=Path.GetFullPath(root);Directory.CreateDirectory(root);
            if(Directory.EnumerateFileSystemEntries(root).Any())throw new InvalidOperationException("Choose an empty folder.");
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();
            Annotate(root);Organize(root);
            Console.WriteLine($"{{\"passed\":{passed},\"scope\":\"Linux editors with pointer and keyboard input\"}}");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine($"FAILED after {passed} checks: {error}");return 1;}
    }

    private static void Annotate(string root)
    {
        var photo=Path.Combine(root,"Photo.png");using(var image=new MagickImage(MagickColors.White,800,500))image.Write(photo,MagickFormat.Png);
        var dialog=Wait(AnnotateDialog.CreateAsync("Annotate Photo",photo));dialog.Show();Pump();
        var canvas=dialog.Canvas;
        Point At(double x,double y)=>canvas.TranslatePoint(canvas.ToControl(x,y),dialog)!.Value;
        void Drag(double x1,double y1,double x2,double y2,RawInputModifiers keys=RawInputModifiers.None,int steps=6)
        {
            dialog.MouseMove(At(x1,y1),keys);dialog.MouseDown(At(x1,y1),MouseButton.Left,keys);
            for(var i=1;i<=steps;i++)dialog.MouseMove(At(x1+(x2-x1)*i/steps,y1+(y2-y1)*i/steps),keys|RawInputModifiers.LeftMouseButton);
            dialog.MouseUp(At(x2,y2),MouseButton.Left,keys);Pump();
        }
        void Click(double x,double y){dialog.MouseDown(At(x,y),MouseButton.Left);dialog.MouseUp(At(x,y),MouseButton.Left);Pump();}
        void Key(Key key,RawInputModifiers keys=RawInputModifiers.None){dialog.KeyPress(key,keys,PhysicalKey.None,null);if(dialog.IsVisible)dialog.KeyRelease(key,keys,PhysicalKey.None,null);Pump();}
        static bool Near(double a,double b)=>Math.Abs(a-b)<=3;

        dialog.Choose("rectangle");Drag(100,100,300,200);
        var box=canvas.Marks.Single();Check(box.Kind=="rectangle"&&Near(box.X1,100)&&Near(box.Y1,100)&&Near(box.X2,300)&&Near(box.Y2,200),"Drag draws a rectangle in image pixels");
        Click(500,400);Check(canvas.Marks.Count==1,"A click with a shape tool leaves no empty shape");
        dialog.Choose("ellipse");Drag(400,100,500,140,RawInputModifiers.Shift);
        var circle=canvas.Marks[^1];Check(Near(Math.Abs(circle.X2-circle.X1),Math.Abs(circle.Y2-circle.Y1))&&Near(circle.X2,500),"Shift keeps an ellipse round");
        dialog.Choose("arrow");Drag(50,450,250,430,RawInputModifiers.Shift);
        var arrow=canvas.Marks[^1];Check(arrow.Kind=="arrow"&&Near(arrow.Y2,450),"Shift snaps an arrow to 45° steps");
        dialog.Choose("freehand");Drag(600,300,750,450,steps:12);
        var stroke=canvas.Marks[^1];Check(stroke.Kind=="freehand"&&stroke.Points!.Count>=10,"Freehand records the path");
        dialog.Choose("number");Click(650,80);Click(700,80);
        Check(canvas.Marks[^2].Text=="1"&&canvas.Marks[^1].Text=="2","Numbers count up");
        Key(Avalonia.Input.Key.Z,RawInputModifiers.Control);Check(canvas.Marks.Count==5&&canvas.Marks[^1].Text=="1","Ctrl+Z undoes the last mark");
        dialog.Choose("text");Click(60,300);Check(canvas.Marks[^1] is {Kind:"text",Text:"Note"}&&Near(canvas.Marks[^1].X1,60),"Text is placed by a click");
        dialog.Choose("select");Drag(100,150,150,190);
        var moved=canvas.Marks[0];Check(Near(moved.X1,150)&&Near(moved.Y1,140)&&Near(moved.X2,350),"Select / Move drags a mark");
        Key(Avalonia.Input.Key.Right,RawInputModifiers.Shift);Check(Near(canvas.Marks[0].X1,160),"Shift+→ nudges the selected mark");
        Key(Avalonia.Input.Key.Delete);Check(canvas.Marks.All(m=>m.Kind!="rectangle")&&canvas.Marks.Count==5,"Delete removes the selected mark");
        dialog.Choose("image");Drag(10,10,60,60);Check(canvas.Marks.Count==5,"The image tool waits for a chosen image");
        // The preview render runs off the UI thread; give it a moment and check it changed pixels.
        Wait(dialog.Flush());Pump();
        var frame=dialog.CaptureRenderedFrame()!;var shot=Path.Combine(root,"annotate-editor.png");frame.Save(shot);
        using(var image=new MagickImage(shot)){using var pixels=image.GetPixels();var p=canvas.TranslatePoint(canvas.ToControl(675,375),dialog)!.Value;var ink=pixels.GetPixel((int)p.X,(int)p.Y).ToColor()!;
            Check(dialog.Settled&&ink.R>200&&ink.G<150,$"The live preview shows the freehand ink (settled {dialog.Settled}, {ink}, at {p})");}
        Key(Avalonia.Input.Key.Enter);Check(dialog.Result is {Marks.Count:5},"Enter saves the marks");
        var output=Wait(Operations.RunAsync(new("annotateImage","Annotate Photo"),[photo],Path.Combine(root,"annotated"),new Progress<OperationProgress>(),CancellationToken.None,new(Annotate:dialog.Result))).Single();
        using(var result=new MagickImage(output)){using var pixels=result.GetPixels();var ink=pixels.GetPixel(675,375).ToColor()!;Check(result.Width==800&&ink.R>200&&ink.G<150,"The saved photo has the freehand ink");}
    }

    private static void Organize(string root)
    {
        var pages=Enumerable.Range(1,4).Select(n=>{var file=Path.Combine(root,$"P{n}.png");using var image=new MagickImage(new MagickColor((byte)(n*50),0,0),200,300);image.Write(file,MagickFormat.Png);return file;}).ToArray();
        var pdf=Operations.ImagesToPdf(pages,Path.Combine(root,"pdf"),null,CancellationToken.None).Single();
        var dialog=Wait(PdfOrganizeDialog.CreateAsync("Organize PDF",pdf));dialog.Show();Pump();
        Point Card(int index){var card=dialog.Gallery.Children[index];return card.TranslatePoint(new(card.Bounds.Width/2,card.Bounds.Height/2),dialog)!.Value;}
        void Click(int index,RawInputModifiers keys=RawInputModifiers.None){dialog.MouseDown(Card(index),MouseButton.Left,keys);dialog.MouseUp(Card(index),MouseButton.Left,keys);Pump();}
        void Key(Key key,RawInputModifiers keys=RawInputModifiers.None){dialog.KeyPress(key,keys,PhysicalKey.None,null);if(dialog.IsVisible)dialog.KeyRelease(key,keys,PhysicalKey.None,null);Pump();}
        Check(dialog.Gallery.Children.Count==4,"Organizer shows every page");
        Click(0);Key(Avalonia.Input.Key.Right,RawInputModifiers.Control);Check(dialog.Current.Pages.SequenceEqual(new[]{2,1,3,4}),"Ctrl+→ moves a page");
        Click(0);Click(3,RawInputModifiers.Control);Key(Avalonia.Input.Key.R);Check(dialog.Current.Rotations.SequenceEqual(new[]{90,0,0,90}),"Ctrl+click selects several pages to rotate");
        Click(1);Click(3,RawInputModifiers.Shift);Key(Avalonia.Input.Key.D,RawInputModifiers.Control);Check(dialog.Current.Pages.SequenceEqual(new[]{2,1,1,3,3,4,4}),"Shift+click selects a range; each copy follows its page, as on Windows");
        Key(Avalonia.Input.Key.Delete);Check(dialog.Current.Pages.SequenceEqual(new[]{2,1,3,4}),"Delete removes the selection");
        // Drag the first page onto the right half of the last card.
        var from=Card(0);var last=dialog.Gallery.Children[3];var to=last.TranslatePoint(new(last.Bounds.Width*.8,last.Bounds.Height/2),dialog)!.Value;
        dialog.MouseDown(from,MouseButton.Left);dialog.MouseMove(new(from.X+30,from.Y),RawInputModifiers.LeftMouseButton);dialog.MouseMove(to,RawInputModifiers.LeftMouseButton);dialog.MouseUp(to,MouseButton.Left);Pump();
        Check(dialog.Current.Pages.SequenceEqual(new[]{1,3,4,2}),"Dragging a page moves it");
        Key(Avalonia.Input.Key.A,RawInputModifiers.Control);Key(Avalonia.Input.Key.Delete);Check(dialog.Current.Pages.Length==4,"The last page cannot be removed");
        dialog.CaptureRenderedFrame()?.Save(Path.Combine(root,"organize-editor.png"));
        dialog.Close();Pump();
    }

    private static T Wait<T>(Task<T> task){while(!task.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}return task.GetAwaiter().GetResult();}
    private static void Wait(Task task)=>Wait(task.ContinueWith(t=>{t.GetAwaiter().GetResult();return 0;}));
    private static void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
    private static void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);passed++;}
}
