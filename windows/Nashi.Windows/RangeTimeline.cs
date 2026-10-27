using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Nashi.Windows;
public sealed class RangeTimeline:FrameworkElement
{
    internal const double MinimumSpan=.05;
    public double Duration{get;set;}=1;public double Start{get;private set;}public double End{get;private set;}=1;
    public event Action<double,double>? Changed;
    private bool dragging,startHandle,moveRange;private double originalStart,originalEnd,originX,laneWidth;
    private BitmapSource[] artwork=[];
    public void SetArtwork(BitmapSource[] images){artwork=images;InvalidateVisual();}
    public RangeTimeline()
    {
        Height=54;MinWidth=160;Cursor=Cursors.SizeWE;Focusable=true;AutomationProperties.SetAutomationId(this,"media.range.timeline");
        IsEnabledChanged+=(_,args)=>{if(UIElementAutomationPeer.FromElement(this) is RangeTimelineAutomationPeer peer)peer.EnabledChanged((bool)args.OldValue,(bool)args.NewValue);};
    }
    public void Set(double duration,double start,double end)
    {
        var oldStart=Start;var oldEnd=End;var oldDuration=Duration;
        Duration=double.IsFinite(duration)?Math.Max(MinimumSpan,duration):MinimumSpan;
        Start=double.IsFinite(start)?Math.Clamp(start,0,Duration-MinimumSpan):0;
        End=double.IsFinite(end)?Math.Clamp(end,Start+MinimumSpan,Duration):Duration;
        InvalidateVisual();NotifyAccessibility(oldStart,oldEnd,oldDuration);
    }
    internal bool HasValidRange=>double.IsFinite(Duration)&&double.IsFinite(Start)&&double.IsFinite(End)&&Duration>=MinimumSpan&&Start>=0&&End<=Duration&&End>Start;
    internal (double Minimum,double Maximum) EndpointBounds(bool start)=>!HasValidRange?(0,0):start?(0,Math.Max(0,End-MinimumSpan)):(Math.Min(Duration,Start+MinimumSpan),Duration);
    /// Semantic edits enter the same Changed callback used by pointer drags.
    /// The UIA provider validates its published bounds before reaching this path.
    internal bool SetEndpoint(bool start,double value)
    {
        VerifyAccess();if(!IsEnabled||!HasValidRange||!double.IsFinite(value))return false;
        var limits=EndpointBounds(start);var bounded=Math.Clamp(value,limits.Minimum,limits.Maximum);
        var oldStart=Start;var oldEnd=End;if(start)Start=bounded;else End=bounded;
        if(Start==oldStart&&End==oldEnd)return false;
        InvalidateVisual();NotifyAccessibility(oldStart,oldEnd,Duration);Changed?.Invoke(Start,End);return true;
    }
    internal Rect EndpointScreenBounds(bool start)
    {
        if(!HasValidRange||!IsVisible||PresentationSource.FromVisual(this) is null)return Rect.Empty;
        var x=12+Math.Max(1,ActualWidth-24)*(start?Start:End)/Duration;
        var local=Rect.Intersect(new Rect(x-7,3,14,48),new Rect(RenderSize));if(local.IsEmpty)return Rect.Empty;
        return new Rect(PointToScreen(local.TopLeft),PointToScreen(local.BottomRight));
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new RangeTimelineAutomationPeer(this);
    private void NotifyAccessibility(double oldStart,double oldEnd,double oldDuration)
    {
        if(UIElementAutomationPeer.FromElement(this) is RangeTimelineAutomationPeer peer)peer.ValuesChanged(oldStart,oldEnd,oldDuration);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);var w=Math.Max(1,ActualWidth-24);dc.DrawRoundedRectangle(Theme.Brush("Inset"),null,new Rect(12,8,w,38),8,8);
        if(artwork.Length>0){dc.PushClip(new RectangleGeometry(new Rect(12,8,w,38),8,8));for(var n=0;n<artwork.Length;n++)dc.DrawImage(artwork[n],new Rect(12+w*n/artwork.Length,8,w/artwork.Length,38));dc.Pop();}
        var left=12+w*Start/Duration;var right=12+w*End/Duration;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(artwork.Length>0?(byte)70:(byte)150,0xC8,0xE6,0x4C)),new Pen(Theme.Brush("Gold"),1),new Rect(left,8,Math.Max(4,right-left),38),6,6);
        foreach(var x in new[]{left,right}){dc.DrawRoundedRectangle(Theme.Primary,null,new Rect(x-7,3,14,48),5,5);dc.DrawLine(new Pen(new SolidColorBrush(Theme.OnPrimary),2),new Point(x,19),new Point(x,35));}
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();var x=e.GetPosition(this).X;laneWidth=Math.Max(1,ActualWidth-24);var a=12+laneWidth*Start/Duration;var b=12+laneWidth*End/Duration;
        moveRange=x>a+14&&x<b-14;startHandle=Math.Abs(x-a)<=Math.Abs(x-b);originalStart=Start;originalEnd=End;originX=x;dragging=CaptureMouse();e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(!dragging)return;var delta=(e.GetPosition(this).X-originX)/laneWidth*Duration;var oldStart=Start;var oldEnd=End;
        if(moveRange){var length=originalEnd-originalStart;Start=Math.Clamp(originalStart+delta,0,Duration-length);End=Start+length;}else if(startHandle)Start=Math.Clamp(originalStart+delta,0,originalEnd-.05);else End=Math.Clamp(originalEnd+delta,originalStart+.05,Duration);
        InvalidateVisual();NotifyAccessibility(oldStart,oldEnd,Duration);Changed?.Invoke(Start,End);e.Handled=true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){dragging=false;ReleaseMouseCapture();e.Handled=true;}
    protected override void OnLostMouseCapture(MouseEventArgs e){dragging=false;base.OnLostMouseCapture(e);}
}

/// The painted lane keeps its existing hit targets; stable virtual child peers
/// expose each edge as a real UIA RangeValue slider in seconds.
internal sealed class RangeTimelineAutomationPeer:FrameworkElementAutomationPeer
{
    private readonly RangeEndpointAutomationPeer start,end;
    internal RangeTimelineAutomationPeer(RangeTimeline owner):base(owner){start=new(owner,true);end=new(owner,false);}
    protected override string GetClassNameCore()=>nameof(RangeTimeline);
    protected override string GetNameCore()=>L10n.Text("Trim");
    protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Group;
    protected override List<AutomationPeer> GetChildrenCore()=>[start,end];
    internal void EnabledChanged(bool oldEnabled,bool newEnabled){start.EnabledChanged(oldEnabled,newEnabled);end.EnabledChanged(oldEnabled,newEnabled);}
    internal void ValuesChanged(double oldStart,double oldEnd,double oldDuration)
    {
        var lane=(RangeTimeline)Owner;
        start.ValueChanged(oldStart,lane.Start,0,Math.Max(0,oldEnd-RangeTimeline.MinimumSpan));
        end.ValueChanged(oldEnd,lane.End,Math.Min(oldDuration,oldStart+RangeTimeline.MinimumSpan),oldDuration);
    }
}

internal sealed class RangeEndpointAutomationPeer(RangeTimeline lane,bool start):AutomationPeer,IRangeValueProvider
{
    private double Current=>start?lane.Start:lane.End;
    public bool IsReadOnly=>!lane.IsEnabled||!lane.HasValidRange;
    public double Value=>lane.HasValidRange?Current:0;
    public double Minimum=>lane.EndpointBounds(start).Minimum;
    public double Maximum=>lane.EndpointBounds(start).Maximum;
    public double SmallChange=>.1;
    public double LargeChange=>1;
    public void SetValue(double value)
    {
        lane.VerifyAccess();
        if(!lane.IsEnabled)throw new ElementNotEnabledException();
        if(!lane.HasValidRange)throw new InvalidOperationException("The source range is unavailable.");
        // RangeValue's contract rejects values outside the bounds it publishes.
        if(!double.IsFinite(value)||value<Minimum||value>Maximum)throw new ArgumentOutOfRangeException(nameof(value));
        lane.SetEndpoint(start,value);
    }
    internal void EnabledChanged(bool oldEnabled,bool newEnabled)
    {
        RaisePropertyChangedEvent(AutomationElementIdentifiers.IsEnabledProperty,oldEnabled&&lane.HasValidRange,newEnabled&&lane.HasValidRange);
        RaisePropertyChangedEvent(RangeValuePatternIdentifiers.IsReadOnlyProperty,!oldEnabled||!lane.HasValidRange,!newEnabled||!lane.HasValidRange);
    }
    internal void ValueChanged(double oldValue,double newValue,double oldMinimum,double oldMaximum)
    {
        if(oldValue!=newValue)RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty,oldValue,newValue);
        if(oldMinimum!=Minimum)RaisePropertyChangedEvent(RangeValuePatternIdentifiers.MinimumProperty,oldMinimum,Minimum);
        if(oldMaximum!=Maximum)RaisePropertyChangedEvent(RangeValuePatternIdentifiers.MaximumProperty,oldMaximum,Maximum);
    }
    public override object? GetPattern(PatternInterface pattern)=>pattern==PatternInterface.RangeValue?this:null;
    protected override string GetAutomationIdCore()=>start?"media.range.0.start":"media.range.0.end";
    protected override string GetNameCore()=>L10n.Text(start?"Start":"End");
    protected override string GetClassNameCore()=>"RangeEndpoint";
    protected override string GetHelpTextCore()=>L10n.Text("Seconds");
    protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Slider;
    protected override Rect GetBoundingRectangleCore()=>lane.EndpointScreenBounds(start);
    protected override Point GetClickablePointCore(){var rect=GetBoundingRectangleCore();return rect.IsEmpty?new Point(double.NaN,double.NaN):new Point(rect.X+rect.Width/2,rect.Y+rect.Height/2);}
    protected override bool IsEnabledCore()=>!IsReadOnly;
    protected override bool IsOffscreenCore()=>GetBoundingRectangleCore().IsEmpty;
    protected override bool IsControlElementCore()=>true;
    protected override bool IsContentElementCore()=>true;
    protected override bool IsKeyboardFocusableCore()=>false;
    protected override bool HasKeyboardFocusCore()=>false;
    protected override bool IsPasswordCore()=>false;
    protected override bool IsRequiredForFormCore()=>false;
    protected override string GetAcceleratorKeyCore()=>string.Empty;
    protected override string GetAccessKeyCore()=>string.Empty;
    protected override string GetItemStatusCore()=>string.Empty;
    protected override string GetItemTypeCore()=>string.Empty;
    protected override AutomationPeer? GetLabeledByCore()=>null;
    protected override List<AutomationPeer>? GetChildrenCore()=>null;
    protected override AutomationOrientation GetOrientationCore()=>AutomationOrientation.Horizontal;
    protected override void SetFocusCore()=>throw new InvalidOperationException("Adjust this endpoint using the RangeValue pattern.");
}
