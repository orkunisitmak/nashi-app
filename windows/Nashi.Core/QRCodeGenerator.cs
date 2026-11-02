using System.Text;
using ImageMagick;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace Nashi.Core;
public static class QRCodeGenerator
{
    public static byte[] Png(string text,int size=512,string correction="M")
    {
        if(string.IsNullOrWhiteSpace(text)||Encoding.UTF8.GetByteCount(text)>2000)throw new ArgumentException("Enter between 1 and 2,000 UTF-8 bytes for the QR code.");
        if(size is <64 or >2048||correction is not ("L" or "M" or "Q" or "H"))throw new ArgumentException("Choose a QR size between 64 and 2,048 pixels and L, M, Q, or H correction.");
        var level=correction switch{"L"=>ErrorCorrectionLevel.L,"Q"=>ErrorCorrectionLevel.Q,"H"=>ErrorCorrectionLevel.H,_=>ErrorCorrectionLevel.M};
        var hints=new Dictionary<EncodeHintType,object>{{EncodeHintType.CHARACTER_SET,"UTF-8"},{EncodeHintType.ERROR_CORRECTION,level},{EncodeHintType.MARGIN,4}};
        ZXing.Common.BitMatrix matrix;
        try{matrix=new QRCodeWriter().encode(text,BarcodeFormat.QR_CODE,size,size,hints);}catch(WriterException){throw new ArgumentException("The text is too long for this QR correction level.");}
        if(matrix.Width!=size||matrix.Height!=size)throw new ArgumentException("Choose a larger QR image size for this text.");
        var pixels=new byte[size*size*3];
        for(var y=0;y<size;y++)for(var x=0;x<size;x++){var value=matrix[x,y]?(byte)0:(byte)255;var i=(y*size+x)*3;pixels[i]=value;pixels[i+1]=value;pixels[i+2]=value;}
        using var image=new MagickImage();image.ReadPixels(pixels,new PixelReadSettings((uint)size,(uint)size,StorageType.Char,PixelMapping.RGB));
        return image.ToByteArray(MagickFormat.Png);
    }
    public static string Export(string text,string directory,int size=512,string correction="M",string filename="QR Code.png",CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if(Path.GetFileName(filename)!=filename||filename.IndexOfAny(['/', '\\', ':', '\0'])>=0||!filename.EndsWith(".png",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a local folder and a PNG filename.");
        var bytes=Png(text,size,correction);token.ThrowIfCancellationRequested();
        using var transaction=new ExportTransaction(directory);
        var staged=Path.Combine(transaction.StagingDirectory,filename);File.WriteAllBytes(staged,bytes);
        return transaction.Publish([staged],token).Single();
    }
}
