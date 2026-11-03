using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;
/// <summary>Resize › templates on Windows: Fit with every margin fill, Fill and crop, batches, and the editor.</summary>
internal static class ResizeChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"Resize templates");Directory.CreateDirectory(folder);
        // A 400×300 landscape: red left half, blue right half, a green 20 px band across the middle.
        var pixels=new byte[400*300*4];
        for(var y=0;y<300;y++)for(var x=0;x<400;x++){var i=(y*400+x)*4;var (r,g,b)=y is >=140 and <160?((byte)0,(byte)200,(byte)0):x<200?((byte)220,(byte)30,(byte)30):((byte)30,(byte)60,(byte)220);pixels[i]=b;pixels[i+1]=g;pixels[i+2]=r;pixels[i+3]=255;}
        var source=Path.Combine(folder,"Landscape.png");ImageEngine.Save(BitmapSource.Create(400,300,96,96,PixelFormats.Bgra32,null,pixels,1600),source,"png");var original=File.ReadAllBytes(source);
        (byte R,byte G,byte B,byte A) At(BitmapSource image,int x,int y){var one=new byte[4];new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0).CopyPixels(new Int32Rect(x,y,1,1),one,4,0);return (one[2],one[1],one[0],one[3]);}
        bool Near((byte R,byte G,byte B,byte A) c,int r,int g,int b,int slack=30)=>Math.Abs(c.R-r)<=slack&&Math.Abs(c.G-g)<=slack&&Math.Abs(c.B-b)<=slack;
        BitmapSource Export(string name,ImageOptions options){var output=Path.Combine(folder,name);Directory.CreateDirectory(output);return ImageEngine.Load(ImageEngine.Apply("resize",[source],output,options).Single());}

        // Instagram portrait (1080×1350): Fit keeps the whole photo 1080×810 in the middle.
        var white=Export("Fit white",new(1080,1350){Fit=FitMode.Fit,Matte="white"});
        check(white.PixelWidth==1080&&white.PixelHeight==1350,"A template must give its exact size");
        check(Near(At(white,540,100),255,255,255)&&Near(At(white,540,1250),255,255,255),"Fit must fill the margins with white");
        check(Near(At(white,100,400),220,30,30)&&Near(At(white,980,400),30,60,220)&&Near(At(white,540,675),0,200,0),"Fit must keep the whole photo, left and right edges included");
        var black=Export("Fit black",new(1080,1350){Fit=FitMode.Fit,Matte="black"});check(Near(At(black,540,100),0,0,0),"Fit must fill the margins with black");
        var clear=Export("Fit transparent",new(1080,1350){Fit=FitMode.Fit,Matte="transparent"});check(At(clear,540,100).A==0&&At(clear,540,675).A==255,"Transparent margins must stay transparent in PNG");
        var blur=Export("Fit blur",new(1080,1350){Fit=FitMode.Fit,Matte="blur"});var top=At(blur,100,100);var right=At(blur,980,100);
        check(top.A==255&&top.R>top.B&&right.B>right.R&&!Near(top,255,255,255,10),"Blurred margins must continue the photo's colours (red left, blue right): "+top+" "+right);
        check(Near(At(blur,100,400),220,30,30),"The photo itself must stay sharp on a blurred background");
        // Fill crops the sides evenly: the centre band stays, the far left and right are cut.
        var fill=Export("Fill",new(1080,1350){Fit=FitMode.Fill});
        check(fill.PixelWidth==1080&&Near(At(fill,540,675),0,200,0)&&Near(At(fill,10,100),220,30,30)&&Near(At(fill,1070,100),30,60,220),"Fill must cover the whole canvas from the photo's centre");
        var stretched=Export("Stretch",new(1080,1350));check(stretched.PixelWidth==1080&&stretched.PixelHeight==1350&&Near(At(stretched,540,675),0,200,0)&&Near(At(stretched,100,100),220,30,30),"Stretch must keep the previous behaviour");
        check(original.SequenceEqual(File.ReadAllBytes(source)),"The original must stay as it was");
        try{ImageEngine.Fitted(ImageEngine.Load(source),40000,10,FitMode.Fit,"blur");check(false,"An oversized template was accepted");}catch(ArgumentException){check(true,"Oversized refused");}
        try{ImageEngine.Fitted(ImageEngine.Load(source),100,100,FitMode.Fit,"plaid");check(false,"An unknown margin fill was accepted");}catch(ArgumentException){check(true,"Unknown margin refused");}
        // A batch of different shapes all get the template size.
        var square=Path.Combine(folder,"Tall.png");ImageEngine.Save(BitmapSource.Create(100,300,96,96,PixelFormats.Bgra32,null,new byte[100*300*4].Select((_,i)=>i%4==3?(byte)255:(byte)90).ToArray(),400),square,"png");
        var batch=Path.Combine(folder,"Batch");Directory.CreateDirectory(batch);
        var outputs=ImageEngine.Apply("resize",[source,square],batch,new ImageOptions(1080,1080){Fit=FitMode.Fit,Matte="white"});
        check(outputs.Count==2&&outputs.Select(ImageEngine.Load).All(i=>i.PixelWidth==1080&&i.PixelHeight==1080),"Every image in a batch must get the template size");

        // The editor: choosing a template fills the size, switches to Fit and previews in the target shape.
        var owner=new Window{Width=1,Height=1,Left=-4000,Top=-4000,ShowInTaskbar=false,WindowStyle=WindowStyle.None};owner.Show();
        try
        {
            var editor=new ToolEditor(owner,"resize","Resize",source,preview:ImageEngine.Load(source),inputs:[source]);
            var instagram=editor.TemplateForTest.Items.Cast<ToolEditor.TemplateChoice>().First(t=>t.Template?.Name=="Portrait post");
            check(editor.ShapeForTest.SelectedItem as string==ToolEditor.StretchToSize&&!editor.MarginsForTest.IsEnabled,"Resize must start as before: stretch, no margins");
            editor.TemplateForTest.SelectedItem=instagram;
            check(editor.FieldForTest("Width").Text=="1080"&&editor.FieldForTest("Height").Text=="1350"&&editor.FitSetting==FitMode.Fit&&editor.MarginsForTest.IsEnabled,"A template must fill the size and keep the whole photo");
            var until=DateTime.UtcNow.AddSeconds(10);while(!editor.ResizePreviewForTest.IsCompleted&&DateTime.UtcNow<until)DesktopInput.Pump(30);DesktopInput.Pump(80);
            var preview=editor.PreviewImageForTest;check(preview is not null&&Math.Abs(preview.Width/preview.Height-0.8)<0.01,"The preview must take the template's 4:5 shape");
            editor.ShapeForTest.SelectedItem=ToolEditor.FillCrop;editor.MarginsForTest.SelectedItem=ToolEditor.WhiteMargins;
            check(editor.AcceptForTest()&&editor.ImageOptions.Width==1080&&editor.ImageOptions.Height==1350&&editor.ImageOptions.Fit==FitMode.Fill,"The template and mode must reach the export");
            editor.ShapeForTest.SelectedItem=ToolEditor.FitInside;check(editor.AcceptForTest()&&editor.ImageOptions.Matte=="white","The margin fill must reach the export");
            editor.FieldForTest("Width").Text="1000";check(editor.TemplateForTest.SelectedIndex==0,"Editing the size must switch to Custom size");
            editor.Close();
        }
        finally{owner.Close();}
    }
}
