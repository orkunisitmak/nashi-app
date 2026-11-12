using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Nashi.Windows;

// Adjust Photo, as in the design: the dials of one section in a glass panel, a ruler under them for the chosen
// dial, and the sections, Before / After, rotate and flip in the dock. The text fields stay the source of truth
// (Save and the tests read them); the dials and the ruler write to them.
public sealed partial class ToolEditor
{
    private sealed record Dial(string Name,string Section,double Minimum,double Maximum,double Neutral,Button Chip,Ellipse Face,Grid Glyph,System.Windows.Shapes.Path Arc,TextBlock Caption);
    private static readonly (string Title,string Icon,(string Name,string Label,string Icon,double Minimum,double Maximum)[] Dials)[] PhotoSections=
    [
        ("Lighting","sun",[("Exposure","Exposure","exposure",-3,3),("Brightness","Brightness","sun",-1,1),("Contrast","Contrast","contrast",0,2),("Highlights","Highlights","highlights",-1,1),("Shadows","Shadows","shadows",-1,1),("Whites","Whites","whites",-1,1),("Blacks","Blacks","blacks",-1,1),("Gamma","Gamma","gamma",.2,3)]),
        ("Color","drop",[("Temperature","Temperature","temperature",-1,1),("Tint","Tint","tint",-1,1),("Saturation","Saturation","saturation",0,2),("Vibrance","Vibrance","vibrance",-1,1),("Hue","Hue","hue",-180,180)]),
        ("Detail","aperture",[("Clarity","Clarity","clarity",0,1),("Texture","Texture","texture",-1,1),("Sharpen","Sharpen","sharpen",0,2),("Dehaze","Dehaze","dehaze",0,1),("Grain","Grain","grain",0,1),("NoiseReduction","Noise reduction","noise",0,1),("Vignette","Vignette","vignette",0,2),("Fade","Fade","fade",0,1),("Blur","Blur","blur",0,30)]),
    ];
    private readonly Dictionary<string,Dial> photoDials=[];
    private readonly StackPanel dialRow=new(){Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
    private readonly PhotoRuler ruler=new(){Margin=new(0,12,0,0)};
    private readonly Grid photoSize=new(){Visibility=Visibility.Collapsed,Width=380};
    private string chosenDial="Exposure";
    private bool adjusting;

    private void BuildAdjust()
    {
        panelTitle.Visibility=Visibility.Collapsed;var stash=new StackPanel{Visibility=Visibility.Collapsed};
        // The text fields move out of sight; each dial reads and writes its own.
        TextBox Hide(string name){var box=fields[name];var at=options.Children.IndexOf(box);if(at>0)options.Children.RemoveAt(at-1);options.Children.Remove(box);stash.Children.Add(box);return box;}
        foreach(var (title,_,list) in PhotoSections)foreach(var (name,label,icon,minimum,maximum) in list)
        {
            var neutral=(double)typeof(Nashi.Core.PhotoAdjustments).GetProperty(name)!.GetValue(new Nashi.Core.PhotoAdjustments())!;Field(name,neutral);var box=Hide(name);
            var face=new Ellipse{Width=42,Height=42,StrokeThickness=1};face.SetResourceReference(Shape.StrokeProperty,"Line");
            var track=new Ellipse{Width=46,Height=46,StrokeThickness=2.2};track.SetResourceReference(Shape.StrokeProperty,"LineStrong");
            var arc=new System.Windows.Shapes.Path{StrokeThickness=2.2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Width=50,Height=50};
            var glyph=new Grid{Width=50,Height=50,Children={face,track,arc,Icons.Make(icon,18)}};
            var caption=L10n.Label(new TextBlock{Text=label,FontSize=11,LineHeight=13,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=66,Margin=new(0,4,0,0)});
            var chip=new Button{Style=Ui.Style("WheelLabelButton"),Width=66,Margin=new(3,0,3,0),VerticalAlignment=VerticalAlignment.Top,Content=new StackPanel{Children={glyph,caption}}};System.Windows.Automation.AutomationProperties.SetName(chip,L10n.Text(label));
            var dial=new Dial(name,title,minimum,maximum,neutral,chip,face,glyph,arc,caption);photoDials[name]=dial;chip.Click+=(_,_)=>Choose(name);
            // Double-click a dial to put just that one back to neutral.
            chip.MouseDoubleClick+=(_,e)=>{fields[name].Text=Number(neutral);e.Handled=true;};L10n.Bind(chip,ToolTipProperty,"Double-click to reset");
            box.TextChanged+=(_,_)=>{Paint(dial);if(chosenDial==name)ShowRuler();};Paint(dial);
        }
        // Size, made quick: one-click scales, width and height that keep the picture's shape, a straighten ruler, the flips.
        foreach(var name in new[]{"Width","Height","Degrees"})Hide(name);var rows=new StackPanel();photoSize.Children.Add(rows);
        var scales=new WrapPanel();foreach(var (title,factor) in new[]{("Original",1d),("75%",.75),("50%",.5),("25%",.25)}){var chip=MainWindow.Button(title,()=>ScalePhoto(factor));chip.Style=Ui.Style("Chip");chip.Margin=new(0,0,6,0);chip.MinHeight=30;scales.Children.Add(chip);}rows.Children.Add(scales);
        var sides=new Grid{Margin=new(0,12,0,0)};sides.ColumnDefinitions.Add(new());sides.ColumnDefinitions.Add(new(){Width=GridLength.Auto});sides.ColumnDefinitions.Add(new());
        TextBox Side(string name,int column){var box=fields[name];stash.Children.Remove(box);box.Style=Ui.Style("Unit");box.Tag="px";box.Margin=new(0);var cell=new StackPanel{Children={L10n.Label(new TextBlock{Text=name,FontSize=11,Margin=new(2,0,0,5)}),box}};Grid.SetColumn(cell,column);sides.Children.Add(cell);return box;}
        var widthBox=Side("Width",0);var heightBox=Side("Height",2);
        var link=MainWindow.Button("Keep proportions",()=>{});link.Style=Ui.Style("IconPlain");link.Width=link.Height=32;link.Margin=new(6,0,6,1);link.VerticalAlignment=VerticalAlignment.Bottom;Ui.SetIcon(link,"link",15);Ui.SetIconOnly(link,true);L10n.Bind(link,ToolTipProperty,"Keep proportions");
        void ShowLink(){if(keepShape)link.SetResourceReference(BackgroundProperty,"Sel");else link.Background=Brushes.Transparent;}link.Click+=(_,_)=>{keepShape=!keepShape;ShowLink();};ShowLink();Grid.SetColumn(link,1);sides.Children.Add(link);rows.Children.Add(sides);
        widthBox.TextChanged+=(_,_)=>FollowSide(widthBox,heightBox,true);heightBox.TextChanged+=(_,_)=>FollowSide(heightBox,widthBox,false);
        rows.Children.Add(L10n.Label(new TextBlock{Text="Straighten",FontSize=11,Margin=new(2,16,0,0)}));rows.Children.Add(straighten);
        straighten.Changed+=fine=>{var turn=Math.Round(DegreesNow()/90)*90;if(Math.Abs(fine)<1.2)fine=0;fields["Degrees"].Text=Number(turn+Math.Round(fine,1));};fields["Degrees"].TextChanged+=(_,_)=>ShowStraighten();ShowStraighten();
        var flips=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,14,0,0)};foreach(var flip in new[]{flipHorizontal,flipVertical}){flip.Margin=new(0,0,24,0);flips.Children.Add(flip);}rows.Children.Add(flips);
        options.Children.Add(dialRow);options.Children.Add(ruler);options.Children.Add(photoSize);options.Children.Add(stash);
        // A magnet at neutral: dragging near it snaps there, so "back to zero" needs no precision.
        ruler.Changed+=value=>{if(!fields.TryGetValue(chosenDial,out var box))return;var dial=photoDials[chosenDial];if(Math.Abs(value-dial.Neutral)<(dial.Maximum-dial.Minimum)*.02)value=dial.Neutral;box.Text=Number(Math.Round(value,Decimals(dial)));};
        ruler.Dragging+=held=>{adjusting=held;if(!held)QueuePhotoPreview();};
        // The dock: the sections, then Before / After, rotate and flip.
        var sections=new Segmented();foreach(var (title,icon,_) in PhotoSections){var section=title;Ui.SetIcon(sections.Add(title,()=>Section(section)),icon,15);}
        Ui.SetIcon(sections.Add("Size",()=>Section("Size")),"frame",15);sections.Select(0);dock.Children.Add(sections);dock.Children.Add(DockDivider());
        Button? compare=null;
        compare=MainWindow.Button("Before / After",()=>{showingOriginal=!showingOriginal;if(showingOriginal){photoGeneration++;SetPhotoPreview(bitmap!);compare!.SetResourceReference(BackgroundProperty,"Sel");}else{compare!.Background=Brushes.Transparent;QueuePhotoPreview();}});
        compare.Style=Ui.Style("Plain");compare.MinHeight=34;compare.Margin=new(0);Ui.SetIcon(compare,"compare",15);
        resetPhotoView=()=>{if(!showingOriginal)return;showingOriginal=false;compare.Background=Brushes.Transparent;};dock.Children.Add(compare);
        Button Turn(string title,string icon,int degrees,bool mirror=false)
        {
            var button=MainWindow.Button(title,()=>RotatePhoto(degrees));button.Style=Ui.Style("IconPlain");button.Width=button.Height=36;Ui.SetIcon(button,icon,17);Ui.SetIconOnly(button,true);L10n.Bind(button,ToolTipProperty,title);
            if(mirror&&Ui.GetIcon(button) is FrameworkElement glyph){glyph.RenderTransformOrigin=new(.5,.5);glyph.RenderTransform=new ScaleTransform(-1,1);}return button;
        }
        dock.Children.Add(Turn("Rotate Left","rotL",-90));dock.Children.Add(Turn("Rotate Right","rotL",90,true));
        var mirrorButton=MainWindow.Button("Flip",()=>{flipHorizontal.IsChecked=flipHorizontal.IsChecked!=true;QueuePhotoPreview();});mirrorButton.Style=Ui.Style("IconPlain");mirrorButton.Width=mirrorButton.Height=36;Ui.SetIcon(mirrorButton,"flip",17);Ui.SetIconOnly(mirrorButton,true);L10n.Bind(mirrorButton,ToolTipProperty,"Flip horizontally");dock.Children.Add(mirrorButton);
        Section("Lighting");
    }
    private void Section(string section)
    {
        var size=section=="Size";photoSize.Visibility=size?Visibility.Visible:Visibility.Collapsed;dialRow.Visibility=ruler.Visibility=size?Visibility.Collapsed:Visibility.Visible;if(size)return;
        dialRow.Children.Clear();foreach(var dial in photoDials.Values.Where(d=>d.Section==section))dialRow.Children.Add(dial.Chip);
        Choose(photoDials.Values.First(d=>d.Section==section).Name);
    }
    private void Choose(string name){chosenDial=name;foreach(var dial in photoDials.Values)Paint(dial);ShowRuler();}
    private static int Decimals(Dial dial)=>dial.Maximum-dial.Minimum>=100?0:dial.Maximum-dial.Minimum>=10?1:2;
    private double DialValue(Dial dial)=>double.TryParse(fields[dial.Name].Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value)&&double.IsFinite(value)?Math.Clamp(value,dial.Minimum,dial.Maximum):dial.Neutral;
    private void ShowRuler()
    {
        if(!photoDials.TryGetValue(chosenDial,out var dial))return;var value=DialValue(dial);var shown=value-dial.Neutral;
        var text=Math.Round(shown,Decimals(dial)).ToString(Decimals(dial) switch{0=>"0",1=>"0.0",_=>"0.00"},CultureInfo.CurrentCulture);if(shown>0)text="+"+text;
        ruler.Show(dial.Minimum,dial.Maximum,dial.Neutral,value,text);
    }
    // A dial: a well with the icon, the chosen one solid ink; the ring shows how far the value is from neutral (half a turn at the end of the range).
    private void Paint(Dial dial)
    {
        var chosen=dial.Name==chosenDial;dial.Face.SetResourceReference(Shape.FillProperty,chosen?"Solid":"Well");dial.Glyph.SetResourceReference(TextElement.ForegroundProperty,chosen?"OnSolid":"Ink");
        dial.Arc.SetResourceReference(Shape.StrokeProperty,chosen?"Ink":"Muted");dial.Caption.SetResourceReference(TextBlock.ForegroundProperty,chosen?"Ink":"Muted");dial.Caption.FontWeight=chosen?FontWeights.SemiBold:FontWeights.Normal;
        var value=DialValue(dial);var reach=value>=dial.Neutral?(dial.Maximum-dial.Neutral):(dial.Neutral-dial.Minimum);var share=reach<=0?0:Math.Clamp((value-dial.Neutral)/reach,-1,1);
        if(Math.Abs(share)<.002){dial.Arc.Data=null;return;}
        var angle=share*Math.PI;var end=new Point(25+23*Math.Sin(angle),25-23*Math.Cos(angle));
        var figure=new PathFigure{StartPoint=new(25,2)};figure.Segments.Add(new ArcSegment(end,new Size(23,23),0,false,share>0?SweepDirection.Clockwise:SweepDirection.Counterclockwise,true));dial.Arc.Data=new PathGeometry([figure]);
    }
    private readonly PhotoRuler straighten=new(){Margin=new(0,2,0,0)};private bool keepShape=true,sizing;
    private double DegreesNow()=>double.TryParse(fields["Degrees"].Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value)&&double.IsFinite(value)?value:0;
    // The picture's size as it will be saved, as ImageEngine.Rotate draws it: a quarter turn swaps its sides,
    // any other angle grows the canvas to hold the turned corners.
    private (double Width,double Height) PhotoCanvas()
    {
        double w=bitmap!.PixelWidth,h=bitmap.PixelHeight;var angle=(((int)DegreesNow()%360)+360)%360;if(angle%90==0)return angle%180==0?(w,h):(h,w);
        var radians=angle*Math.PI/180;var cos=Math.Abs(Math.Cos(radians));var sin=Math.Abs(Math.Sin(radians));return(Math.Ceiling(w*cos+h*sin),Math.Ceiling(w*sin+h*cos));
    }
    private double PhotoAspect(){var (w,h)=PhotoCanvas();return w/h;}
    private void FollowSide(TextBox changed,TextBox other,bool width)
    {
        if(sizing||!keepShape||bitmap is null||!double.TryParse(changed.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value)||value<1)return;
        sizing=true;try{other.Text=Number(Math.Max(1,Math.Round(width?value/PhotoAspect():value*PhotoAspect())));}finally{sizing=false;}
    }
    private void ScalePhoto(double factor)
    {
        if(bitmap is null)return;var (w,h)=PhotoCanvas();
        sizing=true;try{fields["Width"].Text=Number(Math.Max(1,Math.Round(w*factor)));fields["Height"].Text=Number(Math.Max(1,Math.Round(h*factor)));}finally{sizing=false;}
    }
    private void ShowStraighten()
    {
        var degrees=DegreesNow();var fine=degrees-Math.Round(degrees/90)*90;var text=Math.Round(fine,1).ToString("0.#",CultureInfo.CurrentCulture)+"°";if(fine>0)text="+"+text;straighten.Show(-45,45,0,fine,text);
    }
    private bool NudgeDial(Key key)
    {
        if(tool!="editImage"||photoSize.IsVisible||!photoDials.TryGetValue(chosenDial,out var dial)||key is not (Key.Left or Key.Right))return false;
        var step=(dial.Maximum-dial.Minimum)/50*(key==Key.Right?1:-1);fields[dial.Name].Text=Number(Math.Round(Math.Clamp(DialValue(dial)+step,dial.Minimum,dial.Maximum),Decimals(dial)));return true;
    }
}

// The ruler under the dials: ticks slide under a fixed needle as the value changes. Drag it or scroll it;
// double-click returns to neutral. Like the design, the whole range spans 600 px: a minor tick every 12 px,
// a major one every 60 px, and the edges fade out.
internal sealed class PhotoRuler:FrameworkElement
{
    private double minimum=-1,maximum=1,neutral,value;private string text="";private Point grab;private double grabValue;
    internal event Action<double>? Changed;internal event Action<bool>? Dragging;
    private double Scale=>600/Math.Max(1e-9,maximum-minimum);
    public PhotoRuler()
    {
        Height=46;Cursor=Cursors.SizeWE;
        var fade=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,0)};foreach(var (offset,alpha) in new[]{(0d,(byte)0),(.18,(byte)255),(.82,(byte)255),(1d,(byte)0)})fade.GradientStops.Add(new(Color.FromArgb(alpha,0,0,0),offset));fade.Freeze();OpacityMask=fade;
    }
    internal void Show(double minimum,double maximum,double neutral,double value,string text){this.minimum=minimum;this.maximum=maximum;this.neutral=neutral;this.value=value;this.text=text;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        var width=ActualWidth;var height=ActualHeight;dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,width,height));var center=Math.Round(width/2);
        var minor=Theme.Brush("Faint");var major=Theme.Brush("Muted");var step=(maximum-minimum)/50;
        for(var k=0;k<=50;k++)
        {
            var x=center+(minimum+k*step-value)*Scale;if(x<-2||x>width+2)continue;var big=k%5==0;var size=big?16:9;var thick=big?1.5:1;
            dc.DrawRectangle(big?major:minor,null,new Rect(x-thick/2,height-4-size,thick,size));
        }
        dc.DrawRoundedRectangle(Theme.Brush("Ink"),null,new Rect(center-1,height-26,2,26),1,1);
        var label=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface((FontFamily)Application.Current.FindResource("UiFont"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),12,Theme.Brush("Ink"),VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label,new Point(center-label.Width/2,0));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if(e.ClickCount==2){Changed?.Invoke(neutral);e.Handled=true;return;}
        grab=e.GetPosition(this);grabValue=value;CaptureMouse();Dragging?.Invoke(true);e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e){if(!IsMouseCaptured)return;Changed?.Invoke(Math.Clamp(grabValue-(e.GetPosition(this).X-grab.X)/Scale,minimum,maximum));}
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){if(!IsMouseCaptured)return;ReleaseMouseCapture();e.Handled=true;}
    protected override void OnLostMouseCapture(MouseEventArgs e){base.OnLostMouseCapture(e);Dragging?.Invoke(false);}
    protected override void OnMouseWheel(MouseWheelEventArgs e){Changed?.Invoke(Math.Clamp(value+(maximum-minimum)/50*Math.Sign(e.Delta),minimum,maximum));e.Handled=true;}
}
