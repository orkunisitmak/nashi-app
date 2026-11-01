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
            Annotate(root);Organize(root);Metadata();Web();Redact(root);Float(root);BubbleWheel(root);
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
        var frame=dialog.CaptureRenderedFrame()!;var shot=Path.Combine(root,"annotate-editor.png");frame.Save(shot,new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
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
        dialog.CaptureRenderedFrame()?.Save(Path.Combine(root,"organize-editor.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        dialog.Close();Pump();
    }

    private static void Metadata()
    {
        var dialog=new MetadataDialog(new Dictionary<string,string>{["SourceFile"]="/tmp/Song.mp3",["title"]="Eski",["artist"]="Biri",["Stream 1 · codec_name"]="mp3"},true);dialog.Show();Pump();
        Check(dialog.Fields.ContainsKey("title")&&dialog.Fields["title"].Text=="Eski"&&!dialog.Fields.ContainsKey("Stream 1 · codec_name"),"Metadata editor lists editable media fields");
        var title=dialog.Fields["title"];title.Focus();title.SelectAll();Pump();dialog.KeyTextInput("Yeni Şarkı");Pump();
        Check(dialog.Current is {RemoveAll:false} edit&&edit.Updates.Count==1&&edit.Updates["title"]=="Yeni Şarkı","Typing changes only that field");
        dialog.RemoveAllBox.IsChecked=true;Pump();Check(!title.IsEnabled&&dialog.Current is {RemoveAll:true,Updates.Count:0},"Remove all disables the fields");
        dialog.KeyPress(Avalonia.Input.Key.Escape,RawInputModifiers.None,PhysicalKey.None,null);Pump();Check(!dialog.IsVisible,"Escape closes the editor");
    }
    private static void Web()
    {
        var window=new Window{Content=new WebPanel()};window.Show();Pump();var panel=(WebPanel)window.Content!;
        string[] Kinds()=>panel.Kind.ItemsSource!.Cast<string>().ToArray();
        panel.Link.Text="https://youtu.be/jNQXAC9IVRw";Pump();
        Check(Kinds().SequenceEqual(new[]{"video","audio","thumbnail"})&&panel.Kind.SelectedItem as string=="video"&&panel.Quality.IsEffectivelyVisible,"YouTube link offers video, audio and thumbnail");
        panel.Kind.SelectedIndex=1;Pump();Check(!panel.Quality.IsEffectivelyVisible,"Quality is only for video");
        panel.Link.Text="https://www.instagram.com/reel/ABC123/";Pump();Check(Kinds().SequenceEqual(new[]{"video","audio"})&&panel.Kind.SelectedItem as string=="audio","A Reel keeps the chosen kind");
        panel.Link.Text="https://www.instagram.com/someone/p/ABC123/";Pump();Check(Kinds().SequenceEqual(new[]{"photos"}),"An Instagram post offers its photos");
        panel.Link.Text="not a link";Pump();Wait(panel.FindAsync());Check(panel.Summary.Text=="Paste one complete public media link.","A broken link is explained");
        window.Close();Pump();
    }
    private static void Redact(string root)
    {
        if(Operations.Executable("tesseract") is not null)
        {
            var sign=Path.Combine(root,"Sign.png");using(var image=new MagickImage(MagickColors.White,900,300)){new ImageMagick.Drawing.Drawables().FillColor(MagickColors.Black).FontPointSize(90).Text(40,180,"SECRET 4242").Draw(image);image.Write(sign,MagickFormat.Png);}
            var dialog=Wait(RedactDialog.CreateAsync("Redact Photo",sign));dialog.Show();Pump();
            var find=Descendants(dialog).OfType<Button>().Single(b=>b.Content as string is "Find text" or "Metni bul");
            find.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));for(var i=0;i<400&&dialog.Canvas.Areas.Count==0;i++){Thread.Sleep(25);Pump();}
            Check(dialog.Canvas.Areas.Count>=2&&dialog.Canvas.Areas.All(a=>a.Y is > 50 and < 210),"Find text puts a box over each word");
            dialog.KeyPress(Avalonia.Input.Key.Enter,RawInputModifiers.None,PhysicalKey.None,null);Pump();Check(dialog.Result is {Areas.Length:>=2},"Enter keeps the found boxes");
        }
        var frame=new MagickImage(MagickColors.Gray,320,240).ToByteArray(MagickFormat.Png);
        var video=Wait(RedactDialog.CreateAsync("Redact Video","clip.mp4",frame,10));video.Show();Pump();
        var fields=Descendants(video).OfType<TextBox>().Where(t=>t.TemplatedParent is null).ToArray();Check(fields.Length==2,"Video redaction shows From and To");
        video.Canvas.Areas.Add(new Rect(10,10,50,50));fields[0].Text="0:08";fields[1].Text="0:03";
        video.KeyPress(Avalonia.Input.Key.Enter,RawInputModifiers.None,PhysicalKey.None,null);Pump();Check(video.IsVisible&&video.Result is null,"A start after the end is refused");
        fields[0].Text="0:02";fields[1].Text="";video.KeyPress(Avalonia.Input.Key.Enter,RawInputModifiers.None,PhysicalKey.None,null);Pump();
        Check(video.Result is not null&&video.Range==(2,null),"An empty end means until the end of the clip");
    }
    private static void Float(string root)
    {
        var a=Path.Combine(root,"a.txt");var b=Path.Combine(root,"b.txt");File.WriteAllText(a,"a");File.WriteAllText(b,"b");
        var shelf=new FloatShelf([a,b,a,Path.Combine(root,"missing.txt")]);shelf.Show();Pump();
        Check(shelf.Items.Count==2&&shelf.Topmost,"Float holds each existing file once and stays on top");
        shelf.Add([b,Path.Combine(root,"Photo.png")]);Check(shelf.Items.Count==3,"Adding to Float skips files it already has");
        shelf.List.SelectedItems!.Add(a);Pump();shelf.KeyPress(Avalonia.Input.Key.Delete,RawInputModifiers.None,PhysicalKey.None,null);Pump();
        Check(shelf.Items.Count==2&&!shelf.Items.Contains(a)&&File.Exists(a),"Delete removes from Float, not from disk");
        shelf.Toggle();Pump();Check(shelf.Height<60,"Float collapses to a strip");shelf.Toggle();Pump();Check(shelf.Height>300,"Float expands again");
        shelf.Close();Pump();
    }
    private static void BubbleWheel(string root)
    {
        var photo=Path.Combine(root,"Photo.png");Choice? ran=null;string[]? with=null;
        var bubble=new Bubble(MainWindow.BubbleChoices,(files,choice)=>{ran=choice;with=files.ToArray();return Task.FromResult("Saved");});bubble.Show();Pump();
        Check(bubble.Width==Bubble.Small&&bubble.Topmost,"The bubble is a small pear on top");
        bubble.Expand([photo]);Pump();
        Check(bubble.Width==Bubble.Large&&bubble.Ring=="tools"&&bubble.Segments.Any(b=>(b.Tag as Choice)?.Id=="annotateImage"),"Dropping an image opens the Tools ring");
        bubble.Collapse();bubble.Expand([photo],KeyModifiers.Shift);Pump();Check(bubble.Ring=="convert"&&bubble.Segments.Any(b=>(b.Tag as Choice)?.Format=="webp"),"Shift opens the Convert ring");
        bubble.Collapse();bubble.Expand([photo],KeyModifiers.Alt);Pump();Check(bubble.Ring=="actions"&&bubble.Segments.Count()==6,"Alt opens the File ring");
        Check(bubble.Segments.Count()==6&&bubble.Segments.All(p=>p is Avalonia.Controls.Shapes.Path),"Each choice is a petal of the ring");
        var many=Enumerable.Range(1,10).Select(n=>new Choice("t"+n,"Change Video Speed "+n)).ToArray();
        var crowded=new Bubble((_,_)=>many,(_,_)=>Task.FromResult(""));crowded.Show();crowded.Expand([photo]);Pump();
        // The middle of each petal is inside that petal and in no other, so ten choices still have their own place.
        var c=Bubble.Large/2;bool Own(Choice x,int i){var a=-Math.PI/2-Math.PI/10+(i+.5)*2*Math.PI/10;var p=new Point(c+169*Math.Cos(a),c+169*Math.Sin(a));return crowded.PetalOf(x).FillContains(p)&&many.Where(o=>o!=x).All(o=>!crowded.PetalOf(o).FillContains(p));}
        Check(crowded.Segments.Count()==10&&many.Select((x,i)=>Own(x,i)).All(v=>v),"Ten choices get ten separate petals");crowded.Close();
        bubble.KeyPress(Avalonia.Input.Key.Tab,RawInputModifiers.None,PhysicalKey.None,null);Pump();Check(bubble.Ring=="tools","Tab moves to the next ring");
        var crop=(Choice)bubble.Segments.First(b=>(b.Tag as Choice)?.Id=="cropImage").Tag!;Wait(bubble.Pick(crop));Pump();
        Check(ran?.Id=="cropImage"&&with is [var only]&&only==photo&&bubble.Width==Bubble.Small,"A wheel button runs that tool on the dropped file and closes the wheel");
        bubble.Expand([photo,Path.Combine(root,"a.txt")]);Pump();Check(bubble.Ring=="actions","Mixed files open on the ring that has choices");
        bubble.KeyPress(Avalonia.Input.Key.Escape,RawInputModifiers.None,PhysicalKey.None,null);Pump();Check(bubble.Width==Bubble.Small&&bubble.Held.Count==0,"Escape closes the wheel");
        bubble.CaptureRenderedFrame()?.Save(Path.Combine(root,"bubble-small.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());bubble.Expand([photo]);Pump();bubble.CaptureRenderedFrame()?.Save(Path.Combine(root,"bubble-wheel.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        bubble.Close();Pump();
    }
    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual root){foreach(var child in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(root)){yield return child;foreach(var d in Descendants(child))yield return d;}}
    private static T Wait<T>(Task<T> task){while(!task.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}return task.GetAwaiter().GetResult();}
    private static void Wait(Task task)=>Wait(task.ContinueWith(t=>{t.GetAwaiter().GetResult();return 0;}));
    private static void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
    private static void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);passed++;}
}
