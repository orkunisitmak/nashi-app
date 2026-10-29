using ImageMagick;
using Nashi.Core;

static class IconChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        // Left half fully transparent, right half 50% green: alpha must survive every size.
        var transparent=Pixels(40,40,(x,_)=>x<20?[0,0,0,0]:[0,255,0,128]);
        var icon=Path.Combine(root,"Transparent.ico");IconCodec.Write(transparent,40,40,icon);
        var entries=Entries(File.ReadAllBytes(icon),check);
        check(entries.Select(e=>e.Size).SequenceEqual(IconCodec.Sizes),"ICO does not contain 16, 24, 32, 48, 64, 128 and 256 px images");
        foreach(var (size,png) in entries)
        {
            using var frame=new MagickImage(png);check(frame.Width==size&&frame.Height==size,"ICO entry size differs from its image: "+size);
            using var pixels=frame.GetPixels();var clear=pixels.GetPixel(size/4,size/2).ToColor()!;var half=pixels.GetPixel(size*3/4,size/2).ToColor()!;
            check(clear.A==0,"ICO lost full transparency at "+size+" px");check(Math.Abs(half.A-128)<=4&&half.G>200,"ICO lost partial alpha at "+size+" px");
        }
        using(var decoded=new MagickImageCollection(icon))check(decoded.Count==7,"ImageMagick could not read every ICO entry");

        // A 2:1 opaque photo is fitted, not cropped or stretched: transparent bands above and below.
        var wide=Pixels(200,100,(_,_)=>[0,0,255,255]);var wideIcon=Path.Combine(root,"Wide.ico");IconCodec.Write(wide,200,100,wideIcon);
        foreach(var (size,png) in Entries(File.ReadAllBytes(wideIcon),check))
        {
            using var frame=new MagickImage(png);using var pixels=frame.GetPixels();
            var rows=Enumerable.Range(0,size).Where(y=>pixels.GetPixel(size/2,y).ToColor()!.A>127).ToArray();
            var columns=Enumerable.Range(0,size).Where(x=>pixels.GetPixel(x,size/2).ToColor()!.A>127).Count();
            check(columns==size&&Math.Abs(rows.Length-size/2)<=1,"ICO changed a 2:1 image's proportions at "+size+" px");
            check(pixels.GetPixel(size/2,0).ToColor()!.A==0&&pixels.GetPixel(size/2,size-1).ToColor()!.A==0,"ICO padding is not transparent at "+size+" px");
            var centre=pixels.GetPixel(size/2,size/2).ToColor()!;check(centre.R>240&&centre.B<15&&centre.A==255,"ICO changed the image colour at "+size+" px");
        }

        // An 8 px source is scaled up, so Explorer always has a 256 px image.
        var tinyIcon=Path.Combine(root,"Tiny.ico");IconCodec.Write(Pixels(8,8,(_,_)=>[255,0,0,255]),8,8,tinyIcon);
        check(Entries(File.ReadAllBytes(tinyIcon),check).Select(e=>e.Size).SequenceEqual(IconCodec.Sizes),"A small source did not get every icon size");

        // Cancelling writes nothing, and a staged icon is never published.
        var cancelled=Path.Combine(root,"Cancelled.ico");using(var source=new CancellationTokenSource())
        {
            source.Cancel();try{IconCodec.Write(transparent,40,40,cancelled,source.Token);check(false,"A cancelled ICO export finished");}catch(OperationCanceledException){check(!File.Exists(cancelled),"A cancelled ICO export left a file");}
        }
        var destination=Directory.CreateDirectory(Path.Combine(root,"Cancelled export")).FullName;using(var source=new CancellationTokenSource())
        {
            using(var transaction=new ExportTransaction(destination))
            {
                var staged=Path.Combine(transaction.StagingDirectory,"Photo.ico");IconCodec.Write(transparent,40,40,staged,source.Token);source.Cancel();
                try{transaction.Publish([staged],source.Token);check(false,"A cancelled ICO batch was published");}catch(OperationCanceledException){}
            }
            check(Directory.GetFileSystemEntries(destination).Length==0,"A cancelled ICO export left a partial file or staging folder");
        }

        // An existing file with the same name is never replaced or removed.
        var existing=Path.Combine(root,"Existing.ico");File.WriteAllText(existing,"keep");
        try{IconCodec.Write(transparent,40,40,existing);check(false,"ICO export replaced an existing file");}catch(IOException){check(File.ReadAllText(existing)=="keep","ICO export changed an existing file");}
    }
    private static byte[] Pixels(int width,int height,Func<int,int,byte[]> bgra)
    {
        var pixels=new byte[width*height*4];for(var y=0;y<height;y++)for(var x=0;x<width;x++)bgra(x,y).CopyTo(pixels,(y*width+x)*4);return pixels;
    }
    // Reads ICONDIR and each ICONDIRENTRY, and returns every entry's PNG bytes.
    private static List<(int Size,byte[] Png)> Entries(byte[] icon,Action<bool,string> check)
    {
        check(icon.Length>6&&BitConverter.ToUInt16(icon,0)==0&&BitConverter.ToUInt16(icon,2)==1,"ICO header is not ICONDIR type 1");
        var count=BitConverter.ToUInt16(icon,4);var result=new List<(int,byte[])>();
        for(var n=0;n<count;n++)
        {
            var entry=6+16*n;int width=icon[entry]==0?256:icon[entry];int height=icon[entry+1]==0?256:icon[entry+1];
            var length=BitConverter.ToInt32(icon,entry+8);var offset=BitConverter.ToInt32(icon,entry+12);
            check(width==height&&BitConverter.ToUInt16(icon,entry+4)==1&&BitConverter.ToUInt16(icon,entry+6)==32,"ICO entry is not a square 32-bit image");
            check(offset>=6+16*count&&length>8&&offset+length<=icon.Length,"ICO entry points outside the file");
            var png=icon.AsSpan(offset,length).ToArray();check(png[0]==0x89&&png[1]=='P'&&png[2]=='N'&&png[3]=='G',"ICO entry is not PNG data");
            result.Add((width,png));
        }
        return result;
    }
}
