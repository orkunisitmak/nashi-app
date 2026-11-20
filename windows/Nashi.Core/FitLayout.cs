namespace Nashi.Core;
/// <summary>How an image takes a target size that has another shape.</summary>
public enum FitMode{
    /// <summary>Exactly the target size, proportions changed (the Resize behaviour before templates).</summary>
    Stretch,
    /// <summary>The whole image inside the target; the rest is filled (blurred copy, colour or transparency).</summary>
    Fit,
    /// <summary>The target filled completely; what sticks out is cropped evenly.</summary>
    Fill}

/// <summary>A ready-made output size, such as an Instagram portrait post or a passport photo.</summary>
public sealed record ImageTemplate(string Group,string Name,int Width,int Height)
{
    public double Ratio=>(double)Width/Height;
}

/// <summary>Resize to a target size and shape: geometry and the built-in templates.</summary>
public static class FitLayout
{
    /// <summary>Where the source image is drawn on the target canvas, in target pixels.</summary>
    public readonly record struct Placement(double X,double Y,double Width,double Height);

    // Sizes the platforms recommend for uploads (2026); print sizes at 300 dpi.
    public static readonly ImageTemplate[] Templates=
    [
        new("Instagram","Square post",1080,1080),new("Instagram","Portrait post",1080,1350),new("Instagram","Story and Reels",1080,1920),
        new("TikTok","Video cover",1080,1920),
        new("YouTube","Thumbnail",1280,720),new("YouTube","Channel banner",2560,1440),
        new("X","Post image",1600,900),new("X","Header",1500,500),
        new("LinkedIn","Post image",1200,627),new("LinkedIn","Profile banner",1584,396),
        new("Facebook","Post image",1200,630),new("Facebook","Cover photo",1640,624),
        new("Pinterest","Pin",1000,1500),
        new("WhatsApp","Profile photo",640,640),
        new("Web","Link preview (Open Graph)",1200,630),new("Web","Full HD",1920,1080),new("Web","4K UHD",3840,2160),
        new("App","Icon",1024,1024),
        new("Print","Passport photo 35×45 mm",413,531),new("Print","Biometric photo 50×60 mm",591,709),new("Print","A4 portrait",2480,3508),new("Print","A4 landscape",3508,2480),
    ];

    public static Placement Place(int sourceWidth,int sourceHeight,int targetWidth,int targetHeight,FitMode mode)
    {
        if(sourceWidth<1||sourceHeight<1||targetWidth<1||targetHeight<1)throw new ArgumentException("Choose a size of at least 1 pixel.");
        if(mode==FitMode.Stretch)return new(0,0,targetWidth,targetHeight);
        var scaleX=(double)targetWidth/sourceWidth;var scaleY=(double)targetHeight/sourceHeight;
        var scale=mode==FitMode.Fit?Math.Min(scaleX,scaleY):Math.Max(scaleX,scaleY);
        var width=sourceWidth*scale;var height=sourceHeight*scale;
        // Whole pixels on the side that matches exactly, so no hairline margin appears.
        if(Math.Abs(scale-scaleX)<1e-12)width=targetWidth;if(Math.Abs(scale-scaleY)<1e-12)height=targetHeight;
        return new((targetWidth-width)/2,(targetHeight-height)/2,width,height);
    }

    /// <summary>The source rectangle that stays visible with <see cref="FitMode.Fill"/>, in source pixels.</summary>
    public static (int X,int Y,int Width,int Height) Visible(int sourceWidth,int sourceHeight,int targetWidth,int targetHeight)
    {
        var place=Place(sourceWidth,sourceHeight,targetWidth,targetHeight,FitMode.Fill);var scale=place.Width/sourceWidth;
        var width=(int)Math.Round(targetWidth/scale);var height=(int)Math.Round(targetHeight/scale);
        width=Math.Clamp(width,1,sourceWidth);height=Math.Clamp(height,1,sourceHeight);
        return ((sourceWidth-width)/2,(sourceHeight-height)/2,width,height);
    }
}
