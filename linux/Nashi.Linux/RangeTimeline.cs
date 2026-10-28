using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
namespace Nashi.Linux;
// Port of Nashi.Windows.RangeTimeline's range validation, drawing and hit-testing.
internal sealed class RangeTimeline:Control
{
    internal const double MinimumSpan=.05;
    public double Duration{get;private set;}=1;public double Start{get;private set;}public double End{get;private set;}=1;
    public event Action<double,double>? Changed;
    private bool dragging,startHandle,moveRange;private double originalStart,originalEnd,originX,laneWidth;
    private Bitmap[] artwork=[];
    internal int ArtworkCount=>artwork.Length;
    public RangeTimeline(){Height=54;MinWidth=160;Focusable=true;Cursor=new Cursor(StandardCursorType.SizeWestEast);}
    public void SetArtwork(Bitmap[] images){foreach(var image in artwork)image.Dispose();artwork=images;InvalidateVisual();}
    public void Set(double duration,double start,double end)
    {
        Duration=double.IsFinite(duration)?Math.Max(MinimumSpan,duration):MinimumSpan;
        Start=double.IsFinite(start)?Math.Clamp(start,0,Duration-MinimumSpan):0;
        End=double.IsFinite(end)?Math.Clamp(end,Start+MinimumSpan,Duration):Duration;InvalidateVisual();
    }
    public bool SetEndpoint(bool start,double value)
    {
        if(!IsEnabled||!double.IsFinite(value))return false;
        var bounded=Math.Clamp(value,start?0:Math.Min(Duration,Start+MinimumSpan),start?Math.Max(0,End-MinimumSpan):Duration);
        if(start&&bounded==Start||!start&&bounded==End)return false;if(start)Start=bounded;else End=bounded;InvalidateVisual();Changed?.Invoke(Start,End);return true;
    }
    public override void Render(DrawingContext dc)
    {
        base.Render(dc);var w=Math.Max(1,Bounds.Width-24);var lane=new Rect(12,8,w,38);dc.DrawRectangle(NashiTheme.Brush("#D7E2CC"),null,lane,8,8);
        using(dc.PushClip(lane))for(var n=0;n<artwork.Length;n++)dc.DrawImage(artwork[n],new Rect(12+w*n/artwork.Length,8,w/artwork.Length,38));
        var left=12+w*Start/Duration;var right=12+w*End/Duration;
        dc.DrawRectangle(NashiTheme.Brush(artwork.Length>0?"#46E5BD50":"#E5BD50"),new Pen(NashiTheme.Gold,1),new Rect(left,8,Math.Max(4,right-left),38),6,6);
        foreach(var x in new[]{left,right}){dc.DrawRectangle(Brushes.DarkOliveGreen,null,new Rect(x-7,3,14,48),5,5);dc.DrawLine(new Pen(Brushes.White,2),new Point(x,19),new Point(x,35));}
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;Focus();var x=e.GetPosition(this).X;laneWidth=Math.Max(1,Bounds.Width-24);var a=12+laneWidth*Start/Duration;var b=12+laneWidth*End/Duration;
        moveRange=x>a+14&&x<b-14;startHandle=Math.Abs(x-a)<=Math.Abs(x-b);originalStart=Start;originalEnd=End;originX=x;dragging=true;e.Pointer.Capture(this);e.Handled=true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if(!dragging)return;var delta=(e.GetPosition(this).X-originX)/laneWidth*Duration;
        if(moveRange){var length=originalEnd-originalStart;Start=Math.Clamp(originalStart+delta,0,Duration-length);End=Start+length;}
        else if(startHandle)Start=Math.Clamp(originalStart+delta,0,originalEnd-MinimumSpan);else End=Math.Clamp(originalEnd+delta,originalStart+MinimumSpan,Duration);
        InvalidateVisual();Changed?.Invoke(Start,End);e.Handled=true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e){dragging=false;e.Pointer.Capture(null);e.Handled=true;}
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){dragging=false;base.OnPointerCaptureLost(e);}
}
