using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Nashi.Windows;

// Add Background, made visual: pick a backdrop from tiles, set padding, corners and shadow on sliders, choose the canvas
// shape. The text fields stay underneath as the source of truth (Save and the tests read them).
public sealed partial class ToolEditor
{
    private double? frameRatio;private Vector shadowAim;
    // 0–100%: faint, soft around the middle, a deep shadow at the top. Its size follows the picture,
    // so a percentage reads the same on a small screenshot and on a large photo.
    private static double ShadowOpacity(double t)=>Math.Round(.85*t,3);
    private double ShadowRadius(double t)=>Math.Round(Math.Max(24,(bitmap is null?1000:Math.Min(bitmap.PixelWidth,bitmap.PixelHeight))*.09)*(.35+.65*t));
    private void AimShadow(){var reach=Value("Shadow radius",24)*.6;fields["Shadow X"].Text=Number(Math.Round(shadowAim.X*reach));fields["Shadow Y"].Text=Number(Math.Round(shadowAim.Y*reach));}
    private void BuildFrame()
    {
        panelTitle.Visibility=Visibility.Collapsed;var stash=new StackPanel{Visibility=Visibility.Collapsed};
        foreach(var name in new[]{"Background color","Gradient end","Padding","Corner radius","Shadow radius","Shadow opacity","Background blur","Image scale","Position X","Position Y","Canvas width","Canvas height","Shadow X","Shadow Y"})
        {var box=fields[name];var at=options.Children.IndexOf(box);if(at>0)options.Children.RemoveAt(at-1);options.Children.Remove(box);stash.Children.Add(box);}
        // Backdrops: solid colors, soft gradients, or a picture of your own.
        options.Children.Add(L10n.Label(new TextBlock{Text="Background",Margin=new(2,0,0,8)}));var tiles=new WrapPanel();var all=new List<Border>();
        void Mark(Border chosen){foreach(var tile in all)tile.SetResourceReference(Border.BorderBrushProperty,ReferenceEquals(tile,chosen)?"Ink":"Line");}
        Border Tile(Brush fill,string tip,Action pick,UIElement? glyph=null)
        {
            var tile=new Border{Width=34,Height=34,CornerRadius=new(10),Background=fill,BorderThickness=new(2),Margin=new(0,0,8,8),Cursor=Cursors.Hand,ToolTip=L10n.Text(tip),Child=glyph};
            tile.MouseLeftButtonUp+=(_,_)=>{pick();Mark(tile);QueuePhotoPreview();};all.Add(tile);tiles.Children.Add(tile);return tile;
        }
        Brush Color(string hex)=>(Brush)new BrushConverter().ConvertFromString(hex)!;
        foreach(var hex in new[]{"#FFFFFF","#F4F3F0","#111214","#F2553A","#DCEBFF","#DDF4E4"})Tile(Color(hex),hex,()=>{backdrop.SelectedItem="solid";fields["Background color"].Text=hex;});
        foreach(var (from,to) in new[]{("#FFD85C","#FF7A59"),("#A1C4FD","#C2E9FB"),("#FBC2EB","#A6C1EE"),("#84FAB0","#8FD3F4"),("#3A3B40","#111214")})
        {var blend=new LinearGradientBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(from),(System.Windows.Media.Color)ColorConverter.ConvertFromString(to),45);blend.Freeze();Tile(blend,from+" → "+to,()=>{backdrop.SelectedItem="gradient";fields["Background color"].Text=from;fields["Gradient end"].Text=to;});}
        var picture=Tile(Brushes.Transparent,"Image…",()=>{var picker=new Microsoft.Win32.OpenFileDialog{Filter="Images|*.png;*.jpg;*.jpeg;*.webp;*.heic;*.avif;*.tiff;*.bmp"};if(picker.ShowDialog(this)==true){backgroundImage=picker.FileName;backdrop.SelectedItem="image";}},Icons.Make("image",16));picture.SetResourceReference(Border.BackgroundProperty,"Well");
        Mark(all[0]);options.Children.Add(tiles);
        // Sliders with their value, for what is adjusted by eye.
        string Pixels(double v)=>Math.Round(v).ToString(CultureInfo.CurrentCulture)+" px";
        Slide("Padding",0,400,()=>Value("Padding",80),Pixels,v=>{fields["Padding"].Text=Number(Math.Round(v));FitCanvas();});
        Slide("Corner radius",0,160,()=>Value("Corner radius",24),Pixels,v=>fields["Corner radius"].Text=Number(Math.Round(v)));
        Slide("Shadow",0,100,()=>Value("Shadow opacity",.25)/.85*100,v=>Math.Round(v).ToString(CultureInfo.CurrentCulture)+"%",v=>{fields["Shadow opacity"].Text=Number(ShadowOpacity(v/100));fields["Shadow radius"].Text=Number(ShadowRadius(v/100));AimShadow();});
        // Where the shadow falls: nine directions; the middle spreads it all around.
        options.Children.Add(L10n.Label(new TextBlock{Text="Shadow direction"}));var pad=new System.Windows.Controls.Primitives.UniformGrid{Rows=3,Columns=3,Width=108,HorizontalAlignment=HorizontalAlignment.Left};var aims=new List<Button>();
        foreach(var dy in new[]{-1,0,1})foreach(var dx in new[]{-1,0,1})
        {
            var aim=new Vector(dx,dy);UIElement glyph=dx==0&&dy==0?new System.Windows.Shapes.Ellipse{Width=6,Height=6,Fill=Theme.Brush("Ink")}:new Border{Child=Icons.Make("chevRight",14),RenderTransformOrigin=new(.5,.5),RenderTransform=new RotateTransform(Math.Atan2(dy,dx)*180/Math.PI)};
            var button=new Button{Style=Ui.Style("IconPlain"),Width=32,Height=32,Margin=new(2),Content=glyph};aims.Add(button);
            button.Click+=(_,_)=>{shadowAim=aim;AimShadow();foreach(var other in aims)other.Background=Brushes.Transparent;button.SetResourceReference(BackgroundProperty,"Sel");};pad.Children.Add(button);
        }
        aims[4].SetResourceReference(BackgroundProperty,"Sel");options.Children.Add(pad);afterReset+=()=>{shadowAim=new();foreach(var other in aims)other.Background=Brushes.Transparent;aims[4].SetResourceReference(BackgroundProperty,"Sel");};
        Slide("Background blur",0,40,()=>Value("Background blur"),Pixels,v=>fields["Background blur"].Text=Number(Math.Round(v)));
        // The canvas: just around the picture, or a shape for where it will be posted.
        options.Children.Add(L10n.Label(new TextBlock{Text="Canvas",Margin=new(2,16,0,8)}));var shapes=new Segmented(small:true){HorizontalAlignment=HorizontalAlignment.Left};
        foreach(var (title,ratio) in new (string,double?)[]{("Auto",null),("1:1",1),("4:3",4/3d),("16:9",16/9d),("9:16",9/16d)}){var r=ratio;shapes.Add(title,()=>{frameRatio=r;FitCanvas();});}
        shapes.Select(0);options.Children.Add(shapes);options.Children.Add(stash);
        afterReset+=()=>{frameRatio=null;shapes.Select(0);Mark(all[0]);};
        framePixels.Tick+=async(_,_)=>
        {
            framePixels.Stop();if(exactFrame is not {} wanted)return;var generation=photoGeneration;
            try{var exact=await PreviewWorker.Run(()=>ImageEngine.Frame(wanted.Source,wanted.Options));if(generation==photoGeneration&&!showingOriginal&&IsVisible)SetPhotoPreview(exact);}
            catch(Exception error)when(error is ArgumentException or FormatException or InvalidDataException or IOException or NotSupportedException){ToolTip=error.Message;}
        };
        Closed+=(_,_)=>{framePixels.Stop();ImageEngine.ForgetCovers();};
    }
    private Action? afterReset;
    private void Slide(string title,double minimum,double maximum,Func<double> read,Func<double,string> show,Action<double> changed)
    {
        var value=read();
        var head=new DockPanel{Margin=new(2,14,0,4)};var number=new TextBlock{Text=show(value),Margin=new(0),FontWeight=FontWeights.SemiBold};number.SetResourceReference(TextBlock.ForegroundProperty,"Ink");DockPanel.SetDock(number,Dock.Right);head.Children.Add(number);
        head.Children.Add(L10n.Label(new TextBlock{Text=title,Margin=new(0)}));var slider=new Slider{Minimum=minimum,Maximum=maximum,Value=Math.Clamp(value,minimum,maximum)};
        slider.ValueChanged+=(_,_)=>{number.Text=show(slider.Value);changed(slider.Value);};options.Children.Add(head);options.Children.Add(slider);
        afterReset+=()=>{try{slider.Value=Math.Clamp(read(),minimum,maximum);}catch(ArgumentException){}};
    }
    // Draws the preview away from the UI thread, so a slider follows the pointer while the picture catches up.
    private static class PreviewWorker
    {
        private static readonly System.Collections.Concurrent.BlockingCollection<Action> jobs=new();
        // The thread's own drawing context starts first, while it holds no picture: started by the first WriteableBitmap
        // in the middle of reading a picture the window is drawing, it waits on the window while the window waits on it.
        static PreviewWorker(){var thread=new Thread(()=>{new System.Windows.Media.Imaging.WriteableBitmap(System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,PixelFormats.Pbgra32,null,new byte[4],4)).Freeze();foreach(var job in jobs.GetConsumingEnumerable())job();}){IsBackground=true,Name="Nashi preview"};thread.SetApartmentState(ApartmentState.STA);thread.Start();}
        internal static Task<T> Run<T>(Func<T> job){var done=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);jobs.Add(()=>{try{done.SetResult(job());}catch(Exception error){done.SetException(error);}});return done.Task;}
    }
    // Auto: the picture plus padding. A ratio: the smallest canvas of that shape around it.
    private void FitCanvas()
    {
        if(bitmap is null)return;var padding=Value("Padding",80);double width=bitmap.PixelWidth+2*padding,height=bitmap.PixelHeight+2*padding;
        if(frameRatio is {} ratio){if(width/height>ratio)height=width/ratio;else width=height*ratio;}
        fields["Canvas width"].Text=Number(Math.Round(width));fields["Canvas height"].Text=Number(Math.Round(height));
    }
}
