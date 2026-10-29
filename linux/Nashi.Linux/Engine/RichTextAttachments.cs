using System.Text;
using System.Text.RegularExpressions;
using Nashi.Core;
namespace Nashi.Linux.Engine;
internal static class RichTextAttachments
{
    internal static async Task<string> Materialize(string folder,string work,CancellationToken token)
    {
        var file=Path.Combine(folder,"TXT.rtf");if(!File.Exists(file)||new FileInfo(file).Length>20_000_000)throw new InvalidDataException("Choose a complete RTFD folder containing TXT.rtf.");
        var rtf=Encoding.Latin1.GetString(await File.ReadAllBytesAsync(file,token));var result=Regex.Replace(rtf,@"\{\\NeXTGraphic\s+([^{}]+)\}",match=>
        {
            token.ThrowIfCancellationRequested();var name=Regex.Split(match.Groups[1].Value,@"\s+\\(?:width|height|baseline)",RegexOptions.None,TimeSpan.FromSeconds(1))[0].Trim().Replace("\\ "," ");var image=SafePaths.ArchiveEntry(folder,name);
            if(!File.Exists(image)||new FileInfo(image).Length>20_000_000||(File.GetAttributes(image)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("An RTFD attachment is missing or too large: "+name);
            var png=PhotoCodec.Png(image);using var dimensions=PhotoCodec.Read(image);return "{\\pict\\pngblip\\picw"+dimensions.Width+"\\pich"+dimensions.Height+"\\picwgoal"+(dimensions.Width*15)+"\\pichgoal"+(dimensions.Height*15)+"\n"+Convert.ToHexString(png)+"}";
        },RegexOptions.None,TimeSpan.FromSeconds(10));
        if(result.Contains("\\NeXTGraphic",StringComparison.Ordinal))throw new InvalidDataException("This RTFD attachment syntax could not be preserved.");var output=Path.Combine(work,"Rich document.rtf");await File.WriteAllBytesAsync(output,Encoding.Latin1.GetBytes(result),token);return output;
    }
}
