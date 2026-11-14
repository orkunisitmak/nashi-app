using Nashi.Core;
internal static class BackgroundRemovalChecks
{
    internal static void Run(Action<bool,string> check)
    {
        static byte[] Canvas(int width,int height,(byte R,byte G,byte B) colour){var bgra=new byte[width*height*4];for(var i=0;i<bgra.Length;i+=4){bgra[i]=colour.B;bgra[i+1]=colour.G;bgra[i+2]=colour.R;bgra[i+3]=255;}return bgra;}
        static void Fill(byte[] bgra,int width,int x0,int y0,int w,int h,(byte R,byte G,byte B) colour){for(var y=y0;y<y0+h;y++)for(var x=x0;x<x0+w;x++){var i=(y*width+x)*4;bgra[i]=colour.B;bgra[i+1]=colour.G;bgra[i+2]=colour.R;bgra[i+3]=255;}}
        static byte Alpha(byte[] bgra,int width,int x,int y)=>bgra[(y*width+x)*4+3];
        (byte,byte,byte) White=(255,255,255),Red=(200,20,30);

        // A product on white: a red square with a white hole that is not connected to the border.
        var image=Canvas(100,80,White);Fill(image,100,30,20,40,40,Red);Fill(image,100,45,35,10,10,White);
        var result=BackgroundRemoval.Remove(image,100,80,new(12,0));
        check(result.Removed==100*80-40*40&&(result.Red,result.Green,result.Blue)==(255,255,255),"Only the border-connected white background must be removed: "+result);
        check(Alpha(image,100,0,0)==0&&Alpha(image,100,99,79)==0&&Alpha(image,100,10,40)==0,"Background corners and sides must become transparent");
        check(Alpha(image,100,35,25)==255&&Alpha(image,100,50,40)==255,"The subject and the same colour enclosed by it must stay opaque");
        check(image[0]==0&&image[1]==0&&image[2]==0,"Removed pixels must not keep hidden background colour");

        // Near-solid: JPEG-like noise around a light grey studio background.
        var random=new Random(617);var noisy=Canvas(64,64,(230,230,232));
        for(var i=0;i<noisy.Length;i+=4)for(var c=0;c<3;c++)noisy[i+c]=(byte)Math.Clamp(noisy[i+c]+random.Next(-6,7),0,255);
        Fill(noisy,64,20,20,24,24,(20,60,160));
        check(BackgroundRemoval.Remove(noisy,64,64,new(12,0)).Removed==64*64-24*24,"A noisy near-solid background must be removed whole");

        // A photo whose border has no dominant colour is refused instead of being damaged.
        var photo=Canvas(40,40,(10,120,40));Fill(photo,40,20,0,20,20,(200,180,20));Fill(photo,40,0,20,20,20,(30,30,200));Fill(photo,40,20,20,20,20,(120,60,10));
        var before=photo.ToArray();
        try{BackgroundRemoval.Remove(photo,40,40,new());check(false,"A photo without a solid background was changed");}
        catch(InvalidDataException error){check(error.Message==BackgroundRemoval.NoSolidBackground&&photo.SequenceEqual(before),"A photo without a solid background must be left untouched");}
        check(BackgroundRemoval.Background(Canvas(5,5,(0,0,0)).Select((v,i)=>i%4==3?(byte)0:v).ToArray(),5,5) is null,"A fully transparent border has no background colour");

        // Soft edges: an anti-aliased pink outline pixel next to the background.
        var edge=Canvas(20,20,White);Fill(edge,20,6,6,8,8,Red);Fill(edge,20,5,6,1,8,(240,190,192));
        var hard=edge.ToArray();BackgroundRemoval.Remove(hard,20,20,new(12,0));
        BackgroundRemoval.Remove(edge,20,20,new(12,2));
        check(Alpha(hard,20,5,8)==255,"Without feathering the outline must stay opaque");
        check(Alpha(edge,20,5,8) is >0 and <255&&Alpha(edge,20,8,8)==255,"Feathering must soften the outline, not the subject: "+Alpha(edge,20,5,8));

        var exact=Canvas(10,10,(0,128,0));Fill(exact,10,4,4,2,2,(0,129,0));
        check(BackgroundRemoval.Remove(exact,10,10,new(0,0)).Removed==96,"Tolerance 0 removes only the exact background colour");
        check(new BackgroundRemoval.Options(500,-3).Clamped==new BackgroundRemoval.Options(100,0),"Tolerance and feather must stay in range");
        var everything=Canvas(10,10,White);check(BackgroundRemoval.Remove(everything,10,10,new(100,0)).Removed==100,"Tolerance 100 may clear an image that is all background");

        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();var large=Canvas(1000,1000,White);try{BackgroundRemoval.Remove(large,1000,1000,new(),cancelled.Token);check(false,"Cancellation ignored");}catch(OperationCanceledException){check(true,"Cancelled removal");}}
        foreach(var (w,h,length) in new[]{(0,10,0),(10,10,399),(20_000,20_000,4)})
        {try{BackgroundRemoval.Background(new byte[length],w,h);check(false,"Invalid image size accepted");}catch(ArgumentException){check(true,"Invalid image rejected");}}
    }
}
