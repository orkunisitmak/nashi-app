using Nashi.Core;
using ZXing;
using ZXing.Common;
using ImageMagick;
namespace Nashi.Windows;

/// Uses Nashi's existing catalog, transaction, media, archive, metadata and Office engines.
public sealed class LinuxEngine
{
    public Toolchain Tools{get;}=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","tools"));
    public async Task<IReadOnlyList<string>> RunAsync(string action,IReadOnlyList<string> inputs,string destination,LinuxOptions options,string? format=null,IProgress<OperationProgress>? progress=null,CancellationToken token=default)
    {
        if(inputs.Count==0)throw new ArgumentException("Choose files first.");
        if(inputs.Any(p=>!File.Exists(p)&&!Directory.Exists(p)))throw new FileNotFoundException("A selected file is missing.");
        if(action!="convert"&&!FeatureCatalog.Tools(inputs).Contains(action)&&action!="zip")throw new ArgumentException("This tool is not available for the selected files.");
        if(action=="convert"&&(format is null||!FeatureCatalog.Formats(inputs).Contains(format)))throw new ArgumentException("Choose a supported output format.");
        if(action=="zip")return [DocumentEngine.Zip(inputs,destination,progress,token)];
        if(action=="extractArchive")
        {
            using var tx=new ExportTransaction(destination);var files=new List<string>();
            foreach(var input in inputs)files.AddRange(ArchiveEngine.Extract(input,tx.StagingDirectory,options.Password,progress,token));return tx.Publish(files,token);
        }
        if(action=="readQRCode")return await ReadQr(inputs,destination,options,token);
        if(action=="removeMetadata"&&!inputs.All(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p)))return await new MetadataService(Tools.DirectoryPath).WriteAsync(inputs,destination,options.RemoveAllMetadata,options.Metadata,progress:progress,token:token);
        if(inputs.All(p=>FeatureCatalog.IsAudio(p)||FeatureCatalog.IsVideo(p)))
        {
            var media=new MediaEngine(Tools.Executable("ffmpeg"),Tools.Executable("ffprobe"));
            return action=="convert"?await media.ConvertAsync(inputs,format!,destination,options.Media,progress,token):await media.ToolAsync(action,inputs,destination,options.Media,progress,token);
        }
        if(inputs.All(FeatureCatalog.IsImage))return await ImageEngine.Apply(action,inputs,destination,options,format,token,progress);
        if(inputs.All(p=>FeatureCatalog.Extension(p)=="pdf")&&action!="convert")
        {
            if(action=="compress")return await PdfCompression.RunAsync(inputs,destination,options,progress,token);
            using var source=UglyToad.PdfPig.PdfDocument.Open(inputs[0],new(){Password=options.Password});
            var pages=action=="organizePDF"?options.Pages??DocumentEngine.PageRange(options.PageRanges,source.NumberOfPages):null;
            return DocumentEngine.Pdf(action=="mergePDF"?"mergePDFs":action,inputs,destination,pages,token,pagesPerFile:options.PagesPerFile,password:options.Password,pageRanges:options.PageRanges,pageRotations:options.PageRotations,uniformWidth:options.UniformWidth);
        }
        if(action=="convert")return await new DocumentConversion(new OfficeRuntime(Tools.DirectoryPath),Tools).ConvertAsync(inputs,format!,destination,options.Document with{Password=options.Password},progress,token);
        throw new NotSupportedException("This operation has not been ported to Linux yet.");
    }
    private static async Task<IReadOnlyList<string>> ReadQr(IReadOnlyList<string> inputs,string destination,LinuxOptions options,CancellationToken token)
    {
        var values=new HashSet<string>();
        foreach(var input in inputs)
        {
            var images=FeatureCatalog.Extension(input)=="pdf"?await NativePdf.RenderAsync(input,144,token,options.Password):[PhotoCodec.Png(input)];
            foreach(var bytes in images)
            {
                token.ThrowIfCancellationRequested();using var image=new MagickImage(bytes);image.Alpha(AlphaAction.Remove);image.Depth=8;
                var rgb=image.ToByteArray(MagickFormat.Rgb);
                var luminance=new RGBLuminanceSource(rgb,(int)image.Width,(int)image.Height,RGBLuminanceSource.BitmapFormat.RGB24);
                var reader=new BarcodeReaderGeneric{AutoRotate=true,Options=new DecodingOptions{TryHarder=true,PossibleFormats=[BarcodeFormat.QR_CODE]}};
                foreach(var result in reader.DecodeMultiple(luminance)??[])values.Add(result.Text);
            }
        }
        if(values.Count==0)throw new InvalidDataException("No QR codes found.");
        using var tx=new ExportTransaction(destination);var output=Path.Combine(tx.StagingDirectory,"QR codes.txt");await File.WriteAllLinesAsync(output,values,token);return tx.Publish([output],token);
    }
}
