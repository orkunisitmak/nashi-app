using ImageMagick;
namespace Nashi.Core;

// A Windows icon holds one PNG per size. The image is fitted into each square without
// cropping; transparent padding fills the rest. Smaller sources are scaled up so every
// size exists, because Explorer otherwise shows a small entry inside a blank large tile.
// macOS writes the same layout (ImageEngine.swift, IconContainer).
public static class IconCodec
{
    public static readonly int[] Sizes=[16,24,32,48,64,128,256];
    public static void Write(byte[] bgra,int width,int height,string output,CancellationToken token=default)
    {
        if(width<1||height<1||bgra.Length<(long)width*height*4)throw new ArgumentException("Choose a nonempty image.");
        using var source=new MagickImage();source.ReadPixels(bgra,new PixelReadSettings((uint)width,(uint)height,StorageType.Char,PixelMapping.BGRA));source.ColorSpace=ColorSpace.sRGB;source.Strip();
        // Shrink very large photos once, so each size is resampled from at most 256 px.
        var largest=Sizes[^1];if(Math.Max(width,height)>largest)source.Resize(new MagickGeometry((uint)largest,(uint)largest));
        var frames=new List<byte[]>();
        foreach(var size in Sizes)
        {
            token.ThrowIfCancellationRequested();
            using var frame=(MagickImage)source.Clone();var scale=(double)size/Math.Max(frame.Width,frame.Height);
            frame.Resize(new MagickGeometry((uint)Math.Max(1,Math.Round(frame.Width*scale)),(uint)Math.Max(1,Math.Round(frame.Height*scale))){IgnoreAspectRatio=true});
            frame.BackgroundColor=MagickColors.Transparent;frame.Extent((uint)size,(uint)size,Gravity.Center);
            frame.Settings.SetDefine(MagickFormat.Png,"exclude-chunks","date,time");frames.Add(frame.ToByteArray(MagickFormat.Png32));
        }
        token.ThrowIfCancellationRequested();
        var icon=Pack(frames);
        var file=new FileStream(output,FileMode.CreateNew);
        try{using(file)file.Write(icon);}
        catch{try{File.Delete(output);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}throw;}
    }
    // ICONDIR (6 bytes), one 16-byte ICONDIRENTRY per image, then the PNG data. 256 px is stored as 0.
    public static byte[] Pack(IReadOnlyList<byte[]> pngs)
    {
        if(pngs.Count is 0 or >ushort.MaxValue)throw new ArgumentException("Choose at least one icon size.");
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)pngs.Count);
        var offset=6+16*pngs.Count;
        foreach(var png in pngs)
        {
            var (width,height)=PngSize(png);if(width is <1 or >256||height is <1 or >256)throw new ArgumentException("Icon images must be 1 to 256 pixels.");
            writer.Write((byte)(width%256));writer.Write((byte)(height%256));writer.Write((byte)0);writer.Write((byte)0);
            writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(png.Length);writer.Write(offset);offset+=png.Length;
        }
        foreach(var png in pngs)writer.Write(png);
        writer.Flush();return stream.ToArray();
    }
    private static (int Width,int Height) PngSize(byte[] png)
    {
        if(png.Length<24||png[0]!=0x89||png[1]!=(byte)'P'||png[12]!=(byte)'I'||png[15]!=(byte)'R')throw new ArgumentException("Icon images must be PNG data.");
        static int Big(byte[] b,int i)=>b[i]<<24|b[i+1]<<16|b[i+2]<<8|b[i+3];return(Big(png,16),Big(png,20));
    }
}
