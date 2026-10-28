using Avalonia;
using Avalonia.Controls;
namespace Nashi.Linux;

internal sealed partial class ImageSurface
{
    private sealed record MarkState(Control Mark,Rect Bounds,Point[] Points);
    private sealed record SurfaceState(MarkState[] Marks,Control? Active,int NextNumber);
    private readonly List<SurfaceState> undo=[],redo=[];
    private SurfaceState? beforeGesture;
    private SurfaceState CaptureState()=>new(canvas.Children.Where(c=>c!=original).Select(c=>new MarkState(c,MarkBounds(c),c is AnnotationMark mark?mark.Points.ToArray():[])).ToArray(),active,markNumber);
    private static Rect MarkBounds(Control mark)=>mark is AnnotationMark annotation?annotation.SourceBounds:new(Canvas.GetLeft(mark),Canvas.GetTop(mark),double.IsFinite(mark.Width)?mark.Width:Math.Max(1,mark.Bounds.Width),double.IsFinite(mark.Height)?mark.Height:Math.Max(1,mark.Bounds.Height));
    private void Remember(SurfaceState state){undo.Add(state);if(undo.Count>100)undo.RemoveAt(0);redo.Clear();PruneArtwork();}
    private void CommitGesture()
    {
        if(beforeGesture is not {} before)return;beforeGesture=null;var after=CaptureState();
        var same=before.Marks.Length==after.Marks.Length&&before.NextNumber==after.NextNumber&&before.Marks.Zip(after.Marks).All(pair=>pair.First.Mark==pair.Second.Mark&&CloseBounds(pair.First.Bounds,pair.Second.Bounds)&&pair.First.Points.SequenceEqual(pair.Second.Points));
        if(!same)Remember(before);AnnotationsChanged?.Invoke();
    }
    private static bool CloseBounds(Rect a,Rect b)=>Math.Abs(a.X-b.X)<1e-7&&Math.Abs(a.Y-b.Y)<1e-7&&Math.Abs(a.Width-b.Width)<1e-7&&Math.Abs(a.Height-b.Height)<1e-7;
    private void Restore(SurfaceState state)
    {
        while(canvas.Children.Count>1)canvas.Children.RemoveAt(1);
        foreach(var item in state.Marks){
            if(item.Mark is AnnotationMark mark){mark.Points=item.Points.ToArray();mark.SetBounds(item.Bounds);}
            else{Canvas.SetLeft(item.Mark,item.Bounds.X);Canvas.SetTop(item.Mark,item.Bounds.Y);item.Mark.Width=item.Bounds.Width;item.Mark.Height=item.Bounds.Height;}
            canvas.Children.Add(item.Mark);
        }
        active=state.Active;markNumber=state.NextNumber;RefreshHandles();if(CropBounds is {} crop)changed(crop);AnnotationsChanged?.Invoke();
    }
    private void ReleasePointer(){gesturePointer?.Capture(null);gesturePointer=null;moving=false;resizeHandle=null;CommitGesture();}
    public void Reset(){ReleasePointer();while(canvas.Children.Count>1)canvas.Children.RemoveAt(1);undo.Clear();redo.Clear();active=null;handles.Children.Clear();markNumber=1;PruneArtwork();AnnotationsChanged?.Invoke();}
    public void ClearMarks(){ReleasePointer();if(canvas.Children.Count==1)return;Remember(CaptureState());while(canvas.Children.Count>1)canvas.Children.RemoveAt(1);active=null;handles.Children.Clear();markNumber=1;PruneArtwork();AnnotationsChanged?.Invoke();}
    public void Undo(){ReleasePointer();if(undo.Count==0)return;var state=undo[^1];undo.RemoveAt(undo.Count-1);redo.Add(CaptureState());Restore(state);}
    public void Redo(){ReleasePointer();if(redo.Count==0)return;var state=redo[^1];redo.RemoveAt(redo.Count-1);undo.Add(CaptureState());Restore(state);}
    public void DeleteSelected(){ReleasePointer();if(active is null)return;Remember(CaptureState());canvas.Children.Remove(active);active=null;RefreshHandles();AnnotationsChanged?.Invoke();}
}
