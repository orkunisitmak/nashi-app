using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed partial class ImageSurface
{
    private string drawingTool="Marker";
    private readonly List<Point> strokePoints=[];
    private IPointer? gesturePointer;
    private readonly HashSet<Bitmap> ownedArtwork=[];
    private bool disposed;
    public string DrawingTool{get=>drawingTool;set{if(value==drawingTool)return;ReleasePointer();drawingTool=value;if(action=="annotateImage"){active=null;RefreshHandles();SceneChanged?.Invoke();}}}
    public string AnnotationFont{get;set;}="system";
    public event Action<string>? DrawingError;
    public event Action? SceneChanged;
    internal AnnotationMark[] Annotations=>canvas.Children.OfType<AnnotationMark>().ToArray();
    internal int SelectedAnnotation=>active is AnnotationMark mark?Array.IndexOf(Annotations,mark):-1;
    internal void SelectAnnotation(int index){ReleasePointer();active=index>=0&&index<Annotations.Length?Annotations[index]:null;RefreshHandles();SceneChanged?.Invoke();}
    private bool BeginAnnotation(Point point)
    {
        if(DrawingTool=="Select / Move"){
            var tolerance=HandleSize*1.5;active=Annotations.Reverse().FirstOrDefault(mark=>mark.SourceBounds.Inflate(tolerance).Contains(point));
            if(active is not null){beforeGesture=CaptureState();previous=MarkBounds(active);resizeHandle=HitHandle(previous,point,tolerance);moving=resizeHandle is null;}RefreshHandles();SceneChanged?.Invoke();return active is not null;
        }
        if(!Color.TryParse(Ink,out var color)){DrawingError?.Invoke("Choose a valid ink color, such as #FF5200.");return false;}
        if(!double.IsFinite(StrokeWidth)||StrokeWidth<1||StrokeWidth>40||!double.IsFinite(FontSize)||FontSize<8||FontSize>160){DrawingError?.Invoke("Use a stroke width from 1 to 40 and text size from 8 to 160.");return false;}
        Bitmap? artwork=null;
        if(DrawingTool=="Image"){
            if(InsertImage is null){DrawingError?.Invoke("Choose an image for the annotation first.");return false;}
            try{using var stream=new MemoryStream(PhotoCodec.Png(InsertImage));artwork=new Bitmap(stream);ownedArtwork.Add(artwork);}
            catch(Exception error)when(error is IOException or ArgumentException or ImageMagick.MagickException or NotSupportedException){DrawingError?.Invoke("Could not open annotation image: "+error.Message);return false;}
        }
        if(DrawingTool=="Magnifier")artwork=original.Source as Bitmap;
        DrawingError?.Invoke("");point=new(Math.Min(point.X,PixelWidth-1),Math.Min(point.Y,PixelHeight-1));origin=point;beforeGesture=CaptureState();
        var mark=new AnnotationMark{Kind=DrawingTool,Ink=new SolidColorBrush(color),StrokeWidth=StrokeWidth,TextSize=FontSize,Text=DrawingTool=="Number"?(markNumber++).ToString(System.Globalization.CultureInfo.InvariantCulture):AnnotationText,Font=AnnotationFont,Artwork=artwork,IsHitTestVisible=false};
        active=mark;mark.SetBounds(new(Math.Min(point.X,PixelWidth-1),Math.Min(point.Y,PixelHeight-1),1,1));strokePoints.Clear();strokePoints.Add(point);if(DrawingTool is "Marker" or "Freehand" or "Line" or "Arrow")mark.SetPath(strokePoints);canvas.Children.Add(mark);handles.Children.Clear();return true;
    }
    private void MoveAnnotation(Point point,KeyModifiers modifiers)
    {
        if(active is not AnnotationMark mark)return;
        if(DrawingTool=="Select / Move"){
            mark.SetBounds(resizeHandle is {} handle?CropGeometry.Resize(previous,handle,point,new(PixelWidth,PixelHeight),modifiers.HasFlag(KeyModifiers.Shift)?previous.Width/previous.Height:null):CropGeometry.Move(previous,origin,point,new(PixelWidth,PixelHeight)));RefreshHandles();return;
        }
        if(mark.Kind is "Marker" or "Freehand"){strokePoints.Add(point);mark.SetPath(strokePoints);}
        else if(mark.Kind is "Line" or "Arrow"){
            if(modifiers.HasFlag(KeyModifiers.Shift)){var angle=Math.Atan2(point.Y-origin.Y,point.X-origin.X);var length=Math.Sqrt(Math.Pow(point.X-origin.X,2)+Math.Pow(point.Y-origin.Y,2));angle=Math.Round(angle/(Math.PI/4))*Math.PI/4;point=new(Math.Clamp(origin.X+length*Math.Cos(angle),0,PixelWidth),Math.Clamp(origin.Y+length*Math.Sin(angle),0,PixelHeight));}
            mark.SetPath([origin,point]);
        }
        else{var rect=CropGeometry.Selection(origin,point,new(PixelWidth,PixelHeight),modifiers.HasFlag(KeyModifiers.Shift)?1:null);if(rect.Width>=1&&rect.Height>=1)mark.SetBounds(rect);}
    }
    private void PruneArtwork()
    {
        var referenced=Annotations.Select(m=>m.Artwork).Concat(undo.Concat(redo).SelectMany(state=>state.Marks).Select(m=>(m.Mark as AnnotationMark)?.Artwork)).OfType<Bitmap>().ToHashSet();
        foreach(var bitmap in ownedArtwork.Where(b=>!referenced.Contains(b)).ToArray()){ownedArtwork.Remove(bitmap);bitmap.Dispose();}
    }
    internal void DisposeArtwork()
    {
        if(disposed)return;disposed=true;ReleasePointer();foreach(var bitmap in ownedArtwork)bitmap.Dispose();ownedArtwork.Clear();(original.Source as Bitmap)?.Dispose();
    }
    private CropGeometry.Handle? HitHandle(Rect rect,Point point,double tolerance)
    {
        var nearest=Enum.GetValues<CropGeometry.Handle>().Select(handle=>{var f=CropGeometry.Fraction(handle);return(Handle:handle,Distance:Math.Sqrt(Math.Pow(rect.X+f.X*rect.Width-point.X,2)+Math.Pow(rect.Y+f.Y*rect.Height-point.Y,2)));}).OrderBy(h=>h.Distance).First();
        return nearest.Distance<=tolerance?nearest.Handle:null;
    }
    private void RefreshHandles()
    {
        handles.Children.Clear();if(CropBounds is {} crop)ShowCropHandles(crop);else if(action=="annotateImage"&&DrawingTool=="Select / Move"&&active is AnnotationMark mark)ShowCropHandles(mark.SourceBounds,true);else if(action=="redactImage"&&active is Rectangle region)ShowCropHandles(MarkBounds(region));
    }
}
