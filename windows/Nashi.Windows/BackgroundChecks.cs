using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;
/// <summary>Convert › Remove background on Windows: real exports in every transparent format, the editor option and its live preview.</summary>
internal static class BackgroundChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"Remove background");Directory.CreateDirectory(folder);
        // A synthetic product shot: a blue square with a white highlight on a white background.
        var pixels=new byte[160*120*4];
        for(var y=0;y<120;y++)for(var x=0;x<160;x++){var i=(y*160+x)*4;var subject=x is >=50 and <110&&y is >=30 and <90;var highlight=x is >=70 and <80&&y is >=50 and <60;
            var (r,g,b)=subject&&!highlight?((byte)30,(byte)80,(byte)200):((byte)255,(byte)255,(byte)255);pixels[i]=b;pixels[i+1]=g;pixels[i+2]=r;pixels[i+3]=255;}
        var source=Path.Combine(folder,"Product.jpg");ImageEngine.Save(BitmapSource.Create(160,120,96,96,PixelFormats.Bgra32,null,pixels,640),source,"jpg",95);var original=File.ReadAllBytes(source);
        var options=new ImageOptions{RemoveBackground=new(12,1)};var output=Path.Combine(folder,"Out");Directory.CreateDirectory(output);
        byte Alpha(BitmapSource image,int x,int y){var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var one=new byte[4];converted.CopyPixels(new Int32Rect(x,y,1,1),one,4,0);return one[3];}
        foreach(var format in ImageEngine.TransparentFormats)
        {
            string file;try{file=ImageEngine.Apply("convert",[source],output,options,format).Single();}
            catch(Exception error)when(format=="avif"&&error is InvalidOperationException or NotSupportedException or IOException){check(true,"AVIF needs the media tools here: "+error.Message);continue;}
            var image=ImageEngine.Load(file);
            check(Alpha(image,2,2)==0&&Alpha(image,157,117)==0,$"{format}: the white background must be transparent");
            check(Alpha(image,60,40)==255&&Alpha(image,74,54)==255,$"{format}: the subject and its enclosed highlight must stay opaque");
        }
        var jpg=ImageEngine.Load(ImageEngine.Apply("convert",[source],output,options,"jpg").Single());
        check(Alpha(jpg,2,2)==255,"JPG has no transparency, so the background must be kept");
        // A new background colour works for JPG too: a light blue studio background.
        var blue=ImageEngine.Load(ImageEngine.Apply("convert",[source],output,new ImageOptions{RemoveBackground=new(12,1,((byte)210,(byte)230,(byte)255))},"jpg").Single());
        var corner=new byte[4];new FormatConvertedBitmap(blue,PixelFormats.Bgra32,null,0).CopyPixels(new Int32Rect(2,2,1,1),corner,4,0);var middle=new byte[4];new FormatConvertedBitmap(blue,PixelFormats.Bgra32,null,0).CopyPixels(new Int32Rect(60,40,1,1),middle,4,0);
        check(Math.Abs(corner[2]-210)<8&&Math.Abs(corner[1]-230)<8&&corner[0]>245&&middle[0]>180&&middle[2]<60,"A filled JPG must show the new background around the unchanged subject");
        check(original.SequenceEqual(File.ReadAllBytes(source))&&Directory.GetFiles(output,"Product*.png").Length==1,"The original must stay as it was");
        var second=ImageEngine.Apply("convert",[source],output,options,"png").Single();check(Path.GetFileName(second)!="Product.png"&&File.Exists(Path.Combine(output,"Product.png")),"A second export must get a new name");

        // A photo without a plain background is refused and nothing is published.
        var noisy=new byte[80*80*4];var random=new Random(7);random.NextBytes(noisy);for(var i=3;i<noisy.Length;i+=4)noisy[i]=255;
        var photo=Path.Combine(folder,"Photo.png");ImageEngine.Save(BitmapSource.Create(80,80,96,96,PixelFormats.Bgra32,null,noisy,320),photo,"png");
        var refused=Path.Combine(folder,"Refused");Directory.CreateDirectory(refused);
        try{ImageEngine.Apply("convert",[source,photo],refused,options,"png");check(false,"A photo without a plain background was exported");}
        catch(InvalidDataException error){check(error.Message==BackgroundRemoval.NoSolidBackground&&!Directory.EnumerateFileSystemEntries(refused).Any(),"A refused batch must publish nothing, not even the images before it");}
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{ImageEngine.Apply("convert",[source],refused,options,"png",cancelled.Token);check(false,"Cancellation ignored");}catch(OperationCanceledException){check(!Directory.EnumerateFileSystemEntries(refused).Any(),"A cancelled export must publish nothing");}}

        // The editor: the option follows the format, previews live and reaches the export settings.
        var owner=new Window{Width=1,Height=1,Left=-4000,Top=-4000,ShowInTaskbar=false,WindowStyle=WindowStyle.None};owner.Show();
        try
        {
            var editor=new ToolEditor(owner,"convert","Convert",source,preview:ImageEngine.Load(source),inputs:[source],outputFormats:["jpg","png","webp"]);
            var box=editor.RemoveBackgroundForTest;check(box is not null,"Convert must offer Remove background for images");
            var formats=(System.Windows.Controls.ComboBox)Descendants(editor).OfType<System.Windows.Controls.ComboBox>().First(c=>c.Items.Contains("png"));
            formats.SelectedItem="jpg";box!.IsChecked=true;check(box.IsEnabled&&editor.BackgroundSettings is null,"JPG must not get a transparent background");
            editor.FillForTest.SelectedItem=ToolEditor.White;check(editor.BackgroundSettings?.Fill==((byte)255,(byte)255,(byte)255),"JPG must accept a new background colour");
            editor.FillForTest.SelectedItem=ToolEditor.Custom;editor.FieldForTest(ToolEditor.CustomColor).Text="#1E90FF";check(editor.BackgroundSettings?.Fill==((byte)30,(byte)144,(byte)255),"A custom background colour must be read");
            editor.FieldForTest(ToolEditor.CustomColor).Text="blue";try{_=editor.BackgroundSettings;check(false,"An invalid colour was accepted");}catch(ArgumentException error){check(error.Message==ToolEditor.InvalidColor,"An invalid colour must be explained");}
            editor.FillForTest.SelectedItem=ToolEditor.Transparent;box.IsChecked=false;
            formats.SelectedItem="png";box.IsChecked=true;
            var until=DateTime.UtcNow.AddSeconds(15);while(!editor.BackgroundPreviewForTest.IsCompleted&&DateTime.UtcNow<until)DesktopInput.Pump(30);DesktopInput.Pump(60);
            check(editor.PreviewForTest is {} preview&&Alpha(preview,2,2)==0&&Alpha(preview,60,40)==255,"The preview must show the background removed");
            editor.FieldForTest(ToolEditor.BackgroundTolerance).Text="30";editor.FieldForTest(ToolEditor.EdgeSoftness).Text="0";
            check(editor.AcceptForTest()&&editor.ImageOptions.RemoveBackground==new BackgroundRemoval.Options(30,0),"The chosen tolerance and edge softness must reach the export");
            box.IsChecked=false;DesktopInput.Pump(60);check(editor.PreviewForTest is {} restored&&Alpha(restored,2,2)==255&&editor.AcceptForTest()&&editor.ImageOptions.RemoveBackground is null,"Turning the option off must export the image as it is");
            editor.Close();
        }
        finally{owner.Close();}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()){yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
}
