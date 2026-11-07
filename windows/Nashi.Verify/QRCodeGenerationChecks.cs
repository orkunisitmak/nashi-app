using ImageMagick;
using Nashi.Core;
using ZXing;

static class QRCodeGenerationChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var folder=Path.Combine(root,"Generated QR codes");Directory.CreateDirectory(folder);
        try
        {
        const string text="İstanbul ığüş 🍐 https://example.com/qr?x=1&y=2";
        foreach(var level in new[]{"L","M","Q","H"})foreach(var size in new[]{256,512})
        {
            var file=QRCodeGenerator.Export(text,folder,size,level);
            using var image=new MagickImage(file);var pixels=image.GetPixels().ToByteArray(PixelMapping.RGB)!;
            var result=new BarcodeReaderGeneric().Decode(pixels,(int)image.Width,(int)image.Height,RGBLuminanceSource.BitmapFormat.RGB24);
            check(result?.Text==text&&image.Width==size&&image.Height==size,"Generated QR did not round-trip Unicode and dimensions");
            check(pixels[0]==255&&pixels[^1]==255&&pixels.All(v=>v is 0 or 255),"Generated QR lost its quiet zone or crisp pixels");
        }
        var original=Path.Combine(folder,"QR Code.png");var before=File.ReadAllBytes(original);QRCodeGenerator.Export("another",folder);
        check(File.ReadAllBytes(original).SequenceEqual(before),"QR export replaced an existing file");
        foreach(var item in new[]{("",512,"M"),("hello",32,"M"),("hello",512,"X"),(new string('a',2001),512,"M")})
        {
            try{QRCodeGenerator.Png(item.Item1,item.Item2,item.Item3);throw new Exception("Invalid QR request accepted");}catch(ArgumentException){check(true,"Invalid QR request rejected");}
        }
        try{QRCodeGenerator.Export(text,folder,token:new CancellationToken(true));throw new Exception("Cancelled QR export accepted");}catch(OperationCanceledException){check(true,"Cancelled QR export rejected");}
        }
        finally{Directory.Delete(folder,true);}
    }
}
