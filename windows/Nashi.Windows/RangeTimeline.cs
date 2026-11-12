using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Nashi.Windows;

// The media timeline: the film (frames at their own shape) or the sound (its waveform), with the playhead in it.
// A click or a drag on the film moves the playhead. On top, what each editor marks in time: the kept range and
// its handles, split points and the parts between them, chosen frames, and the parts of a sound to cover.
public sealed class RangeTimeline:FrameworkElement
{
    internal const double MinimumSpan=.05;
    public double Duration{get;set;}=1;public double Start{get;private set;}public double End{get;private set;}=1;
    public event Action<double,double>? Changed;
    internal event Action<double>? Seeked;
    internal event Action<int,double>? MarkMoved;internal event Action<int>? MarkRemoved,SpanChosen,SpanRemoved,SegmentToggled;internal event Action<double,double>? SpanDrawn;internal event Action? GrabStarted,GrabEnded;
    // What this timeline shows: the range with handles, points in time, the parts between them, covered spans.
    internal bool ShowsRange{get=>showsRange;set{showsRange=value;InvalidateVisual();}}
    internal bool MarksMovable,ShowsSegments,DrawsSpans,Ruler;
    internal double[] Marks{get=>marks;set{marks=value;InvalidateVisual();}}
    internal (double Start,double End)[] Spans{get=>spans;set{spans=value;InvalidateVisual();}}
    internal int ActiveSpan{get=>activeSpan;set{activeSpan=value;InvalidateVisual();}}
    internal IReadOnlySet<int> Dropped{get=>dropped;set{dropped=value;InvalidateVisual();}}
    internal double Position{get=>position;set{if(position==value||double.IsNaN(position)&&double.IsNaN(value))return;position=value;InvalidateVisual();}}
    private bool showsRange=true;private double[] marks=[];private (double Start,double End)[] spans=[];private int activeSpan=-1;private IReadOnlySet<int> dropped=new HashSet<int>();private double position=double.NaN,hover=double.NaN;
    private enum Grab{None,Start,End,Mark,Scrub,Draw}
    private Grab grab;private int grabbed;private double originalStart,originalEnd,originX,drawFrom;
    private BitmapSource[] artwork=[];
    public void SetArtwork(BitmapSource[] images){artwork=images;InvalidateVisual();}
    public RangeTimeline()
    {
        Height=54;MinWidth=160;Focusable=true;AutomationProperties.SetAutomationId(this,"media.range.timeline");
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
        if(!showsRange||!HasValidRange||!IsVisible||PresentationSource.FromVisual(this) is null)return Rect.Empty;
        var lane=Lane();var x=X(start?Start:End);
        var local=Rect.Intersect(new Rect(x-7,lane.Top-5,14,lane.Height+10),new Rect(RenderSize));if(local.IsEmpty)return Rect.Empty;
        return new Rect(PointToScreen(local.TopLeft),PointToScreen(local.BottomRight));
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new RangeTimelineAutomationPeer(this);
    private void NotifyAccessibility(double oldStart,double oldEnd,double oldDuration)
    {
        if(UIElementAutomationPeer.FromElement(this) is RangeTimelineAutomationPeer peer)peer.ValuesChanged(oldStart,oldEnd,oldDuration);
    }
    // The film lies 12 px in from each side (the handles' half width), with room for the time labels under it.
    private Rect Lane()=>new(12,8,Math.Max(1,ActualWidth-24),Math.Max(20,ActualHeight-16-(Ruler?20:0)));
    private double X(double seconds)=>12+Math.Max(1,ActualWidth-24)*Math.Clamp(seconds,0,Duration)/Duration;
    private double TimeAt(double x)=>Math.Clamp((x-12)/Math.Max(1,ActualWidth-24),0,1)*Duration;
    private double[] Bounds()=>[0,..marks.Where(m=>m>0&&m<Duration).Order(),Duration];
    private static readonly Brush Shade=Frozen(new SolidColorBrush(Color.FromArgb(128,0,0,0))),Dark=Frozen(new SolidColorBrush(Color.FromArgb(150,17,18,20))),Pill=Frozen(new SolidColorBrush(Color.FromArgb(214,17,18,20)));
    private static readonly Pen Outline=Frozen(new Pen(new SolidColorBrush(Color.FromArgb(90,0,0,0)),4)),Head=Frozen(new Pen(Brushes.White,2)),Ghost=Frozen(new Pen(new SolidColorBrush(Color.FromArgb(170,255,255,255)),1));
    private static T Frozen<T>(T value)where T:Freezable{value.Freeze();return value;}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);var lane=Lane();var coral=Theme.Brush("Coral");var dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawRoundedRectangle(Theme.Brush("Sel"),null,lane,8,8);dc.PushClip(new RectangleGeometry(lane,8,8));
        // Frames keep their own shape, side by side across the film; each shows the moment under it. A sound is one waveform.
        if(artwork.Length==1)dc.DrawImage(artwork[0],lane);
        else if(artwork.Length>1)
        {
            var aspect=artwork[0].PixelHeight>0?(double)artwork[0].PixelWidth/artwork[0].PixelHeight:16/9d;var tile=Math.Max(8,lane.Height*aspect);
            for(var x=lane.Left;x<lane.Right;x+=tile){var at=Math.Clamp((x+tile/2-lane.Left)/lane.Width,0,.999);dc.DrawImage(artwork[(int)(at*artwork.Length)],new Rect(x,lane.Top,tile,lane.Height));}
        }
        // The parts of a split: a left-out part goes dark.
        var bounds=ShowsSegments?Bounds():[];
        for(var n=0;n+1<bounds.Length;n++)if(dropped.Contains(n))dc.DrawRectangle(Dark,null,new Rect(X(bounds[n]),lane.Top,Math.Max(0,X(bounds[n+1])-X(bounds[n])),lane.Height));
        // Covered spans (the chosen one is drawn as the range).
        for(var n=0;n<spans.Length;n++)
        {
            if(n==activeSpan&&showsRange)continue;var (a,b)=spans[n];var box=new Rect(X(a),lane.Top,Math.Max(3,X(b)-X(a)),lane.Height);var fill=coral.Clone();fill.Opacity=.38;
            dc.DrawRectangle(fill,new Pen(coral,1.5),box);
        }
        if(showsRange)
        {
            var left=X(Start);var right=X(End);
            if(artwork.Length>0&&spans.Length==0){dc.DrawRectangle(Shade,null,new Rect(lane.Left,lane.Top,Math.Max(0,left-lane.Left),lane.Height));dc.DrawRectangle(Shade,null,new Rect(right,lane.Top,Math.Max(0,lane.Right-right),lane.Height));}
            else{var fill=coral.Clone();fill.Opacity=spans.Length>0?.42:.22;dc.DrawRectangle(fill,null,new Rect(left,lane.Top,Math.Max(2,right-left),lane.Height));}
        }
        dc.Pop();
        if(showsRange)
        {
            var left=X(Start);var right=X(End);dc.DrawRoundedRectangle(null,new Pen(coral,2.5),new Rect(left,lane.Top,Math.Max(4,right-left),lane.Height),6,6);
            foreach(var x in new[]{left,right}){dc.DrawRoundedRectangle(coral,null,new Rect(x-7,lane.Top-5,14,lane.Height+10),5,5);var mid=lane.Top+lane.Height/2;dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(140,17,18,20)),2),new Point(x,mid-8),new Point(x,mid+8));}
        }
        // A × on each covered span, to take it away.
        for(var n=0;n<spans.Length;n++){var (_,b)=spans[n];if(X(b)-X(spans[n].Start)<22)continue;Badge(dc,new Point(X(b)-10,lane.Top+10),"×",dpi);}
        // Points in time: a coral line with a round head above the film.
        foreach(var m in marks.Where(m=>m>0&&m<Duration)){var x=X(m);dc.DrawLine(new Pen(coral,2),new Point(x,lane.Top-2),new Point(x,lane.Bottom+2));dc.DrawEllipse(coral,new Pen(Brushes.White,1.5),new Point(x,lane.Top),5,5);}
        // Each part's number, on a small tag at the top; a left-out part's tag is struck through.
        for(var n=0;n+1<bounds.Length;n++)
        {
            var a=X(bounds[n]);var b=X(bounds[n+1]);if(b-a<26)continue;var label=Text((n+1).ToString(CultureInfo.CurrentCulture),11,Brushes.White,dpi);var tag=new Rect((a+b)/2-Math.Max(11,label.Width/2+7),lane.Bottom-24,Math.Max(22,label.Width+14),18);
            dc.DrawRoundedRectangle(dropped.Contains(n)?Theme.Brush("Faint"):Pill,null,tag,9,9);dc.DrawText(label,new Point(tag.Left+(tag.Width-label.Width)/2,tag.Top+(tag.Height-label.Height)/2));
            if(dropped.Contains(n))dc.DrawLine(new Pen(Brushes.White,1.5),new Point(tag.Left+4,tag.Top+tag.Height/2),new Point(tag.Right-4,tag.Top+tag.Height/2));
        }
        // Where the pointer would move the playhead, with its time.
        if(double.IsFinite(hover)&&grab==Grab.None&&IsMouseOver){var x=X(hover);dc.DrawLine(Ghost,new Point(x,lane.Top),new Point(x,lane.Bottom));Badge(dc,new Point(Math.Clamp(x,lane.Left+26,lane.Right-26),lane.Top+10),Clock(hover),dpi);}
        if(double.IsFinite(position)){var x=X(position);dc.DrawLine(Outline,new Point(x,lane.Top-3),new Point(x,lane.Bottom+3));dc.DrawLine(Head,new Point(x,lane.Top-3),new Point(x,lane.Bottom+3));dc.DrawRoundedRectangle(Brushes.White,null,new Rect(x-4,lane.Top-6,8,6),2,2);}
        if(Ruler)
        {
            var steps=new[]{.5,1,2,5,10,15,30,60,120,300,600,900,1800,3600};var step=steps.FirstOrDefault(s=>lane.Width*s/Duration>=72,3600);
            for(var t=0d;t<=Duration+1e-6;t+=step){var x=X(t);dc.DrawLine(new Pen(Theme.Brush("LineStrong"),1),new Point(x,lane.Bottom+3),new Point(x,lane.Bottom+7));var label=Text(step>=1?TimeSpan.FromSeconds(t).ToString(t>=3600?@"h\:mm\:ss":@"m\:ss",CultureInfo.CurrentCulture):Clock(t),10.5,Theme.Brush("Muted"),dpi);dc.DrawText(label,new Point(Math.Clamp(x-label.Width/2,0,Math.Max(0,ActualWidth-label.Width)),lane.Bottom+8));}
        }
    }
    private static void Badge(DrawingContext dc,Point center,string text,double dpi)
    {
        var label=Text(text,text=="×"?12:10.5,Brushes.White,dpi);var width=Math.Max(16,label.Width+(text=="×"?6:12));var box=new Rect(center.X-width/2,center.Y-8,width,16);
        dc.DrawRoundedRectangle(Pill,null,box,8,8);dc.DrawText(label,new Point(box.Left+(box.Width-label.Width)/2,box.Top+(box.Height-label.Height)/2));
    }
    private static FormattedText Text(string text,double size,Brush brush,double dpi)=>new(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),size,brush,dpi);
    internal static string Clock(double seconds)=>TimeSpan.FromSeconds(Math.Max(0,seconds)).ToString(seconds>=3600?@"h\:mm\:ss":@"m\:ss\.f",CultureInfo.CurrentCulture);
    // What is under the pointer, most specific first: a ×, a part's tag, a handle, a point, then the film itself.
    private (Grab Kind,int Index,bool Remove,bool Toggle) HitTest(Point p)
    {
        var lane=Lane();
        for(var n=spans.Length-1;n>=0;n--){var (a,b)=spans[n];if(X(b)-X(a)>=22&&Math.Abs(p.X-(X(b)-10))<=8&&Math.Abs(p.Y-(lane.Top+10))<=9)return(Grab.None,n,true,false);}
        if(ShowsSegments){var bounds=Bounds();for(var n=0;n+1<bounds.Length;n++){var a=X(bounds[n]);var b=X(bounds[n+1]);if(b-a>=26&&Math.Abs(p.X-(a+b)/2)<=14&&p.Y>=lane.Bottom-26&&p.Y<=lane.Bottom-4)return(Grab.None,n,false,true);}}
        if(showsRange){var a=X(Start);var b=X(End);var near=Math.Min(Math.Abs(p.X-a),Math.Abs(p.X-b));if(near<=10)return(Math.Abs(p.X-a)<=Math.Abs(p.X-b)?Grab.Start:Grab.End,-1,false,false);}
        if(MarksMovable)for(var n=0;n<marks.Length;n++)if(Math.Abs(p.X-X(marks[n]))<=6)return(Grab.Mark,n,false,false);
        for(var n=spans.Length-1;n>=0;n--)if(p.X>=X(spans[n].Start)&&p.X<=X(spans[n].End)&&n!=activeSpan)return(Grab.Scrub,n,false,false);
        return(DrawsSpans?Grab.Draw:Grab.Scrub,-1,false,false);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();var p=e.GetPosition(this);var (kind,index,remove,toggle)=HitTest(p);e.Handled=true;
        if(remove){SpanRemoved?.Invoke(index);return;}if(toggle){SegmentToggled?.Invoke(index);return;}
        if(index>=0&&kind==Grab.Scrub)SpanChosen?.Invoke(index);
        grab=kind;grabbed=index;originalStart=Start;originalEnd=End;originX=p.X;drawFrom=TimeAt(p.X);
        GrabStarted?.Invoke();if(grab==Grab.Scrub)Seeked?.Invoke(TimeAt(p.X));CaptureMouse();InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p=e.GetPosition(this);hover=TimeAt(p.X);
        if(grab==Grab.None){var (kind,_,remove,toggle)=HitTest(p);Cursor=remove||toggle?Cursors.Hand:kind is Grab.Start or Grab.End or Grab.Mark?Cursors.SizeWE:kind==Grab.Draw?Cursors.Cross:Cursors.Arrow;InvalidateVisual();return;}
        var delta=(p.X-originX)/Math.Max(1,ActualWidth-24)*Duration;var oldStart=Start;var oldEnd=End;
        switch(grab)
        {
            case Grab.Start:Start=Math.Clamp(originalStart+delta,0,originalEnd-MinimumSpan);break;
            case Grab.End:End=Math.Clamp(originalEnd+delta,originalStart+MinimumSpan,Duration);break;
            case Grab.Mark:MarkMoved?.Invoke(grabbed,Math.Clamp(TimeAt(p.X),MinimumSpan,Duration-MinimumSpan));return;
            case Grab.Scrub:Seeked?.Invoke(TimeAt(p.X));InvalidateVisual();return;
            case Grab.Draw:InvalidateVisual();return;
        }
        InvalidateVisual();NotifyAccessibility(oldStart,oldEnd,Duration);Changed?.Invoke(Start,End);e.Handled=true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var p=e.GetPosition(this);
        // A drag across the film covers that part; a click there just moves the playhead.
        if(grab==Grab.Draw){var to=TimeAt(p.X);if(Math.Abs(p.X-originX)>=4&&Math.Abs(to-drawFrom)>=MinimumSpan)SpanDrawn?.Invoke(Math.Min(drawFrom,to),Math.Max(drawFrom,to));else Seeked?.Invoke(to);}
        var was=grab;grab=Grab.None;ReleaseMouseCapture();InvalidateVisual();e.Handled=true;if(was!=Grab.None)GrabEnded?.Invoke();
    }
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        var p=e.GetPosition(this);for(var n=0;n<marks.Length;n++)if(MarksMovable&&Math.Abs(p.X-X(marks[n]))<=6){MarkRemoved?.Invoke(n);e.Handled=true;return;}
    }
    protected override void OnMouseLeave(MouseEventArgs e){hover=double.NaN;InvalidateVisual();base.OnMouseLeave(e);}
    protected override void OnLostMouseCapture(MouseEventArgs e){var was=grab;grab=Grab.None;base.OnLostMouseCapture(e);if(was!=Grab.None)GrabEnded?.Invoke();}
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
    protected override List<AutomationPeer> GetChildrenCore()=>((RangeTimeline)Owner).ShowsRange?[start,end]:[];
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
