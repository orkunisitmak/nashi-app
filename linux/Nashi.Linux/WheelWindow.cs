using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
namespace Nashi.Linux;

public sealed class WheelWindow:Window
{
    private readonly (string Id,string Label)[] choices;
    private readonly Action<string> choose;
    private readonly Button[] buttons;
    private readonly WheelFace face;
    private int selected;
    public bool Dropped{get;private set;}
    public WheelWindow((string Id,string Label)[] choices,Action<string> choose,string title="Tools",PixelPoint? pointer=null)
    {
        this.choices=choices;this.choose=choose;Title="Nashi · "+title;Width=560;Height=560;CanResize=false;Background=Brushes.Transparent;Foreground=NashiTheme.Ink;
        WindowDecorations=Avalonia.Controls.WindowDecorations.None;TransparencyLevelHint=[WindowTransparencyLevel.Transparent];ShowInTaskbar=false;Topmost=true;ShowActivated=pointer is null;
        var canvas=new Canvas{Background=Brushes.Transparent};Content=canvas;face=new WheelFace(choices.Length){Width=560,Height=560};canvas.Children.Add(face);
        var center=NashiTheme.Text(title,20);center.Width=160;center.TextAlignment=TextAlignment.Center;Canvas.SetLeft(center,200);Canvas.SetTop(center,266);canvas.Children.Add(center);
        buttons=choices.Select((choice,index)=>
        {
            var angle=-Math.PI/2+(index+.5)*2*Math.PI/choices.Length;var button=NashiTheme.Button(choice.Label,()=>Complete(index));button.Content=new TextBlock{Text=Nashi.Core.AppLocalization.Text(choice.Label,NashiTheme.Language),TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,FontSize=12};button.Width=105;button.Height=62;button.Padding=new(4);button.Background=Brushes.Transparent;
            Canvas.SetLeft(button,227.5+175*Math.Cos(angle));Canvas.SetTop(button,249+175*Math.Sin(angle));canvas.Children.Add(button);return button;
        }).ToArray();
        Select(0);
        DragDrop.SetAllowDrop(this,true);AddHandler(DragDrop.DragOverEvent,Over);AddHandler(DragDrop.DropEvent,Drop);
        Opened+=(_,_)=>{
            if(pointer is {} point){var screen=Screens.ScreenFromPoint(point);var scale=RenderScaling;var w=(int)(Width*scale);var h=(int)(Height*scale);var area=screen?.WorkingArea??new PixelRect(point.X-w/2,point.Y-h/2,w,h);Position=new(Math.Clamp(point.X-w/2,area.X,Math.Max(area.X,area.Right-w)),Math.Clamp(point.Y-h/2,area.Y,Math.Max(area.Y,area.Bottom-h)));}
            else buttons[0].Focus();
        };
        KeyDown+=(_,e)=>
        {
            if(e.Key==Key.Escape){Close();e.Handled=true;return;}
            if(e.Key is Key.Right or Key.Down or Key.Tab){Select((selected+1)%buttons.Length);buttons[selected].Focus();e.Handled=true;}
            if(e.Key is Key.Left or Key.Up){Select((selected+buttons.Length-1)%buttons.Length);buttons[selected].Focus();e.Handled=true;}
        };
    }
    private void Select(int index){selected=index;for(var i=0;i<buttons.Length;i++)buttons[i].Foreground=i==index?NashiTheme.Brush("#243B2B"):NashiTheme.Ink;face.Selected=index;face.InvalidateVisual();}
    private int Hit(Point point)
    {
        var dx=point.X-280;var dy=point.Y-280;var radius=Math.Sqrt(dx*dx+dy*dy);if(radius<92||radius>260)return -1;
        var angle=(Math.Atan2(dy,dx)+Math.PI/2+2*Math.PI)%(2*Math.PI);return Math.Min(choices.Length-1,(int)(angle/(2*Math.PI/choices.Length)));
    }
    private void Over(object? sender,DragEventArgs e)
    {var index=Hit(e.GetPosition(this));if(index>=0)Select(index);e.DragEffects=index>=0?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
    private void Drop(object? sender,DragEventArgs e)
    {var index=Hit(e.GetPosition(this));if(index<0)return;e.DragEffects=DragDropEffects.Copy;e.Handled=true;Dropped=true;Complete(index);}
    private void Complete(int index){Close();choose(choices[index].Id);}
    private sealed class WheelFace(int count):Control
    {
        public int Selected{get;set;}
        public override void Render(DrawingContext context)
        {
            base.Render(context);var center=new Point(280,280);const double outer=258,inner=92;
            for(var i=0;i<count;i++)
            {
                var start=-Math.PI/2+i*2*Math.PI/count+.006;var end=-Math.PI/2+(i+1)*2*Math.PI/count-.006;
                Point P(double radius,double angle)=>new(center.X+radius*Math.Cos(angle),center.Y+radius*Math.Sin(angle));
                var shape=new StreamGeometry();using(var geometry=shape.Open()){
                    geometry.BeginFigure(P(inner,start),true);geometry.LineTo(P(outer,start));geometry.ArcTo(P(outer,end),new Size(outer,outer),0,end-start>Math.PI,SweepDirection.Clockwise);geometry.LineTo(P(inner,end));geometry.ArcTo(P(inner,start),new Size(inner,inner),0,end-start>Math.PI,SweepDirection.CounterClockwise);geometry.EndFigure(true);
                }
                context.DrawGeometry(i==Selected?NashiTheme.Gold:NashiTheme.Surface,new Pen(NashiTheme.Paper,1),shape);
            }
            context.DrawEllipse(NashiTheme.Paper,null,center,88,88);
        }
    }
}
