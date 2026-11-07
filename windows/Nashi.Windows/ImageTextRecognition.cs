using System.Windows.Media;
using System.Windows.Media.Imaging;
using global::Windows.Storage.Streams;
using global::Windows.Graphics.Imaging;
using global::Windows.Media.Ocr;
namespace Nashi.Windows;

internal static class ImageTextRecognition
{
    internal static bool Available=>OcrEngine.TryCreateFromUserProfileLanguages() is not null||OcrEngine.TryCreateFromLanguage(new global::Windows.Globalization.Language("en-US")) is not null;
    internal static async Task<string> Recognize(BitmapSource source,CancellationToken token,int maximumDimension=int.MaxValue)
    {
        var engine=OcrEngine.TryCreateFromUserProfileLanguages()??OcrEngine.TryCreateFromLanguage(new global::Windows.Globalization.Language("en-US"))??throw new InvalidOperationException("Install an OCR language in Windows Language settings to recognize text.");
        token.ThrowIfCancellationRequested();var limit=Math.Min(maximumDimension,(int)OcrEngine.MaxImageDimension);var factor=Math.Min(1,(double)limit/Math.Max(source.PixelWidth,source.PixelHeight));if(factor<1)source=new TransformedBitmap(source,new ScaleTransform(factor,factor));
        using var memory=new InMemoryRandomAccessStream();using(var writer=new DataWriter(memory.GetOutputStreamAt(0))){writer.WriteBytes(ImageEngine.PngBytes(source));await writer.StoreAsync();}memory.Seek(0);
        var decoder=await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(memory);using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied);var result=await engine.RecognizeAsync(bitmap);token.ThrowIfCancellationRequested();return string.Join("\n",result.Lines.Select(line=>line.Text));
    }
}
