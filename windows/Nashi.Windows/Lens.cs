using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Nashi.Windows;

// Liquid Glass for small floating controls, as in the design: the window's
// backdrop bent inward along a convex bezel, with one fixed light from above drawing a fine rim.
// Nothing follows the pointer; the lens is redrawn only when the window moves or the backdrop changes.
public sealed class Lens:FrameworkElement
{
    public static readonly DependencyProperty PresetProperty=DependencyProperty.Register(nameof(Preset),typeof(string),typeof(Lens),new PropertyMetadata(null,(d,_)=>((Lens)d).PresetChanged()));
    public string? Preset{get=>(string?)GetValue(PresetProperty);set=>SetValue(PresetProperty,value);}
    public static readonly DependencyProperty RadiusProperty=DependencyProperty.Register(nameof(Radius),typeof(double),typeof(Lens),new PropertyMetadata(99d,(d,_)=>((Lens)d).Update()));
    public double Radius{get=>(double)GetValue(RadiusProperty);set=>SetValue(RadiusProperty,value);}
    // Only the rim light, bending nothing: for glass that stays truly see-through (the folded Float).
    public bool GlintOnly{get;init;}
    private static readonly List<WeakReference<Lens>> live=[];
    // What a lens bends: pixels covering a screen rectangle (physical pixels). Tinted sources get the window tint first.
    // Relative: Bounds are offsets from the window's top-left, so the source moves with the window.
    internal sealed record Supply(byte[] Pixels,int PixelWidth,int PixelHeight,Rect Bounds,bool Tinted,bool Relative=false);
    // A window can supply its own source (the editor bends its canvas, the wheel what is under it); otherwise the window glass is used.
    internal static readonly DependencyProperty SupplyProperty=DependencyProperty.RegisterAttached("Supply",typeof(Supply),typeof(Lens),new PropertyMetadata(null));
    internal static void SetSupply(Window window,Supply? supply){window.SetValue(SupplyProperty,supply);RefreshAll(window);}
    // What is on screen under a window right now, sharp and untinted (for the clear wheel). It is read off the UI thread,
    // so the wheel draws its first frames at once; its glass fades in over them anyway.
    internal static async Task<Supply?> UnderAsync(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return null;
        return await Behind.UnderAsync(handle,VisualTreeHelper.GetDpi(window).DpiScaleX) is {} view?new(view.Pixels,view.PixelWidth,view.PixelHeight,view.Bounds,false):null;
    }
    // A picture of one part of a window (an editor's canvas), placed relative to the window.
    internal static Supply? Snapshot(FrameworkElement element,Window window)
    {
        if(element.ActualWidth<1||element.ActualHeight<1||PresentationSource.FromVisual(element) is null)return null;var dpi=VisualTreeHelper.GetDpi(element).DpiScaleX;var scale=dpi*.5;
        int w=Math.Max(1,(int)(element.ActualWidth*scale)),h=Math.Max(1,(int)(element.ActualHeight*scale));var picture=new RenderTargetBitmap(w,h,96*scale,96*scale,PixelFormats.Pbgra32);picture.Render(element);
        var pixels=new byte[w*h*4];picture.CopyPixels(pixels,w*4,0);
        // Where the canvas has nothing, the window's own background shows through.
        if(window.Background is SolidColorBrush{Color:var paper})for(var i=0;i<pixels.Length;i+=4){var open=255-pixels[i+3];if(open==0)continue;pixels[i]=(byte)(pixels[i]+paper.B*open/255);pixels[i+1]=(byte)(pixels[i+1]+paper.G*open/255);pixels[i+2]=(byte)(pixels[i+2]+paper.R*open/255);pixels[i+3]=255;}
        var at=element.TranslatePoint(new Point(0,0),window);return new(pixels,w,h,new Rect(at.X*dpi,at.Y*dpi,element.ActualWidth*dpi,element.ActualHeight*dpi),false,true);
    }
    private Window? window;private BitmapSource? picture;private WriteableBitmap? surface;
    // Clearing a lens that shows nothing must not invalidate it: that starts a layout pass, and the editor
    // re-reads its canvas after every layout pass, which would refresh every lens again, forever.
    private void Show(BitmapSource? next){if(next is null&&picture is null){surface=null;return;}picture=next;if(next is null)surface=null;InvalidateVisual();}
    // Lenses are drawn one at a time on the UI thread, so they share one scratch buffer and keep only their own surface.
    private static byte[] scratch=[];
    private static byte[] Buffer(int size){if(scratch.Length<size)scratch=new byte[size];else Array.Clear(scratch,0,size);return scratch;}
    private void Present(byte[] pixels,int w,int h)
    {
        if(surface is null||surface.PixelWidth!=w||surface.PixelHeight!=h)surface=new WriteableBitmap(w,h,96,96,PixelFormats.Pbgra32,null);
        surface.WritePixels(new Int32Rect(0,0,w,h),pixels,w*4,0);if(!ReferenceEquals(picture,surface)){picture=surface;InvalidateVisual();}
    }
    protected override void OnRender(DrawingContext dc){if(picture is not null)dc.DrawImage(picture,new Rect(RenderSize));}
    public Lens()
    {
        IsHitTestVisible=false;RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
        SizeChanged+=(_,e)=>{if(Preset is null)return;if(e.PreviousSize.Width<1){Update();return;}Settle();};
        Loaded+=(_,_)=>{window=Window.GetWindow(this);if(window is null)return;if(Preset is not null)Join();Update();};
        Unloaded+=(_,_)=>{Leave();window=null;};
    }
    // Every button carries a lens in its template, but most have no preset: only lenses with one follow their
    // window and take part in refreshes.
    private bool joined;
    private void Join(){if(joined||window is null)return;joined=true;window.LocationChanged+=Moved;window.SizeChanged+=Moved;live.Add(new(this));}
    private void Leave(){settle?.Stop();if(!joined)return;joined=false;if(window is not null){window.LocationChanged-=Moved;window.SizeChanged-=Moved;}live.RemoveAll(r=>!r.TryGetTarget(out var l)||ReferenceEquals(l,this));}
    private void PresetChanged(){if(Preset is not null&&IsLoaded)Join();Update();}
    // Every lens, or only those of one window.
    internal static void RefreshAll(Window? only=null){foreach(var item in live.ToArray())if(item.TryGetTarget(out var lens)&&(only is null||ReferenceEquals(lens.window,only)))lens.Update();}
    // Moving or resizing a window redraws its lenses once it rests, not on every step of the move.
    private System.Windows.Threading.DispatcherTimer? settle;
    private void Settle()
    {
        if(settle is null){var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(90)};timer.Tick+=(_,_)=>{timer.Stop();Update();};settle=timer;}
        settle.Stop();settle.Start();
    }
    private void Moved(object? sender,EventArgs e)=>Settle();
    private void Update()
    {
        if(window is null||Preset is null||!Theme.Glass||ActualWidth<4||ActualHeight<4||!IsVisible&&IsLoaded&&Visibility!=Visibility.Visible){Show(null);return;}
        if(!GlintOnly&&window.GetValue(SupplyProperty) is null&&!Backdrop.GetEnabled(window)){Show(null);return;}
        var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero||!Behind.WindowRect(handle,out var frame)){Show(null);return;}
        var view=GlintOnly?null:window.GetValue(SupplyProperty) as Supply??(Backdrop.Scene(window) is {} scene?new Supply(scene.Pixels,scene.PixelWidth,scene.PixelHeight,scene.Bounds,true):null);if(view is null&&!GlintOnly){Show(null);return;}
        // Work in screen pixels, whatever scaling (a wheel's Viewbox, the monitor DPI) sits in between.
        Point origin,corner;try{origin=PointToScreen(new Point(0,0));corner=PointToScreen(new Point(ActualWidth,ActualHeight));}catch(InvalidOperationException){Show(null);return;}
        int w=(int)Math.Round(corner.X-origin.X),h=(int)Math.Round(corner.Y-origin.Y);if(w<4||h<4){Show(null);return;}var scale=VisualTreeHelper.GetDpi(this).DpiScaleX;double wd=w/scale,hd=h/scale;
        // Presets from the design (in design pixels): bezel is the rim width, bend a share of it, glint the rim light.
        var (bezelSize,share,glint,frost)=Preset switch{"dock"=>(Math.Min(hd/2,18),.4,.5,5d),"clear"=>(Math.Min(44,Math.Min(wd,hd)/3),.42,.6,1.2),"panel"=>(1d,0d,0d,22d),"orb"=>(Math.Min(hd/2,16),.3,.45,24d),_=>(Math.Min(hd/2,14),.4,.55,1.6)};
        var bezel=Math.Max(6*scale,Math.Round(bezelSize*scale));var bend=Math.Round(share*bezel);var radius=Math.Min(Radius*w/ActualWidth,Math.Min(w,h)/2d);
        var light=glint*(Theme.Dark?.6:1);var flat=bend==0&&light==0;var map=flat?null:Map(w,h,radius,bezel,scale);double hw=w/2d,hh=h/2d;
        if(view is null)
        {
            if(map is null){Show(null);return;}var glare=Buffer(w*h*4);
            for(var i=0;i<w*h;i++){var a=map.Spec[i]*light*map.Alpha[i];var v=(byte)Math.Round(255*a);glare[i*4]=glare[i*4+1]=glare[i*4+2]=glare[i*4+3]=v;}
            Present(glare,w,h);return;
        }
        var tint=((SolidColorBrush)Theme.Brush("Slab")).Color;double ta=view.Tinted?tint.A/255d:0;
        // Window glass arrives frosted already; a canvas or what is under the wheel gets the preset's frost here.
        var source=view.Tinted?view.Pixels:Frosted(view,frost*scale*view.PixelWidth/Math.Max(1,view.Bounds.Width));int pw=view.PixelWidth,ph=view.PixelHeight;var output=Buffer(w*h*4);var area=view.Relative?new Rect(frame.Left+view.Bounds.X,frame.Top+view.Bounds.Y,view.Bounds.Width,view.Bounds.Height):view.Bounds;
        void Row(int y){for(var x=0;x<w;x++)
        {
            var i=y*w+x;var alpha=map?.Alpha[i]??Math.Clamp(.5-Sdf(x+.5-hw,y+.5-hh,hw,hh,radius),0,1);if(alpha<=0)continue;
            // Sample what is behind the control, displaced inward near the edge.
            var sx=origin.X+x+.5+(map?.Dx[i]??0)*bend/2;var sy=origin.Y+y+.5+(map?.Dy[i]??0)*bend/2;
            var u=(sx-area.X)/area.Width*pw-.5;var v=(sy-area.Y)/area.Height*ph-.5;
            var x0=(int)Math.Floor(u);var y0=(int)Math.Floor(v);var fx=u-x0;var fy=v-y0;double b=0,g=0,r=0;
            for(var k=0;k<4;k++)
            {
                var cx=Math.Clamp(x0+(k&1),0,pw-1);var cy=Math.Clamp(y0+(k>>1),0,ph-1);var weight=((k&1)==1?fx:1-fx)*((k>>1)==1?fy:1-fy);var p=(cy*pw+cx)*4;
                b+=source[p]*weight;g+=source[p+1]*weight;r+=source[p+2]*weight;
            }
            // The window's tint sits between the desktop and the control, then glass lifts color (saturate 1.4).
            b=tint.B*ta+b*(1-ta);g=tint.G*ta+g*(1-ta);r=tint.R*ta+r*(1-ta);
            var l=.0722*b+.7152*g+.2126*r;b=l+(b-l)*1.4;g=l+(g-l)*1.4;r=l+(r-l)*1.4;
            // Rim light: white specular over the glass.
            var s=map?.Spec[i]??0;var a=s*light;b=b+(255*s-b)*a;g=g+(255*s-g)*a;r=r+(255*s-r)*a;
            var o=i*4;output[o]=(byte)Math.Clamp(b*alpha,0,255);output[o+1]=(byte)Math.Clamp(g*alpha,0,255);output[o+2]=(byte)Math.Clamp(r*alpha,0,255);output[o+3]=(byte)Math.Round(255*alpha);
        }}
        Rows(h,w*h,Row);Present(output,w,h);
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Supply,Dictionary<int,byte[]>> frosted=new();
    private static byte[] Frosted(Supply view,double sigma)
    {
        if(sigma<.5)return view.Pixels;var key=(int)Math.Round(sigma*10);var known=frosted.GetOrCreateValue(view);
        if(!known.TryGetValue(key,out var pixels)){pixels=(byte[])view.Pixels.Clone();Behind.Blur(pixels,view.PixelWidth,view.PixelHeight,sigma);known[key]=pixels;}return pixels;
    }
    private static void Rows(int rows,int pixels,Action<int> row){if(pixels>=40_000)System.Threading.Tasks.Parallel.For(0,rows,row);else for(var y=0;y<rows;y++)row(y);}
    private sealed record LensMap(float[] Dx,float[] Dy,float[] Spec,float[] Alpha);
    private static readonly Dictionary<(int,int,int,int),LensMap> maps=[];
    private static double Sdf(double px,double py,double hw,double hh,double r){var qx=Math.Abs(px)-hw+r;var qy=Math.Abs(py)-hh+r;return Math.Sqrt(Math.Pow(Math.Max(qx,0),2)+Math.Pow(Math.Max(qy,0),2))+Math.Min(Math.Max(qx,qy),0)-r;}
    // Displacement, specular light and coverage for one size, computed once (lens.js maps(), feSpecularLighting).
    private static LensMap Map(int w,int h,double r,double bezel,double scale)
    {
        var key=(w,h,(int)Math.Round(r*10),(int)Math.Round(bezel*10));if(maps.TryGetValue(key,out var known))return known;if(maps.Values.Sum(m=>(long)m.Alpha.Length)+(long)w*h>1_500_000)maps.Clear();
        var count=w*h;var dx=new float[count];var dy=new float[count];var alpha=new float[count];var height=new double[count];double hw=w/2d,hh=h/2d;
        void Shape(int y){for(var x=0;x<w;x++)
        {
            var i=y*w+x;double px=x+.5-hw,py=y+.5-hh;var inside=-Sdf(px,py,hw,hh,r);alpha[i]=(float)Math.Clamp(inside+.5,0,1);height[i]=inside>0?1:0;
            if(inside>-1&&inside<bezel)
            {
                var t=Math.Max(inside,0)/bezel;var m=Math.Pow(1-t,2);var nx=Sdf(px+.5,py,hw,hh,r)-Sdf(px-.5,py,hw,hh,r);var ny=Sdf(px,py+.5,hw,hh,r)-Sdf(px,py-.5,hw,hh,r);var len=Math.Sqrt(nx*nx+ny*ny);if(len==0)len=1;
                dx[i]=(float)(-nx/len*m);dy[i]=(float)(-ny/len*m);height[i]=inside>0?Math.Pow(1-Math.Pow(1-Math.Clamp(t,0,1),4),.25):0;
            }
        }}
        Rows(h,count,Shape);
        // One distant light: azimuth 262°, elevation 48° (from above), surface scale 5, exponent 40.
        double az=262*Math.PI/180,el=48*Math.PI/180;double lx=Math.Cos(az)*Math.Cos(el),ly=Math.Sin(az)*Math.Cos(el),lz=Math.Sin(el)+1;var ln=Math.Sqrt(lx*lx+ly*ly+lz*lz);lx/=ln;ly/=ln;lz/=ln;
        var surface=5*scale;var spec=new float[count];double H(int x,int y)=>height[Math.Clamp(y,0,h-1)*w+Math.Clamp(x,0,w-1)];
        void Light(int y){for(var x=0;x<w;x++)
        {
            var nx=-surface/4*((H(x+1,y-1)+2*H(x+1,y)+H(x+1,y+1))-(H(x-1,y-1)+2*H(x-1,y)+H(x-1,y+1)));
            var ny=-surface/4*((H(x-1,y+1)+2*H(x,y+1)+H(x+1,y+1))-(H(x-1,y-1)+2*H(x,y-1)+H(x+1,y-1)));
            var n=Math.Sqrt(nx*nx+ny*ny+1);var dot=(nx*lx+ny*ly+lz)/n;spec[y*w+x]=(float)Math.Clamp(.9*Math.Pow(Math.Max(dot,0),40),0,1);
        }}
        Rows(h,count,Light);return maps[key]=new LensMap(dx,dy,spec,alpha);
    }
}
