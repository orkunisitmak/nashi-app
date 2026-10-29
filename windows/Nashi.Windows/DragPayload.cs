using System.Text;
using System.Windows;
using Nashi.Core;
namespace Nashi.Windows;
internal static class DragPayload
{
    internal static string? Text(IDataObject data,string format,int limit=32768)
    {
        if(!data.GetDataPresent(format))return null;var value=data.GetData(format);if(value is string text)return text.Length<=limit?text:null;
        if(value is Stream stream&&stream.CanSeek){var original=stream.Position;try{if(stream.Length>limit*2L)return null;stream.Position=0;var bytes=new byte[checked((int)stream.Length)];stream.ReadExactly(bytes);return (format.EndsWith('W')?Encoding.Unicode:Encoding.UTF8).GetString(bytes).TrimEnd('\0');}finally{stream.Position=original;}}return null;
    }
    internal static WebMediaPayload? Parse(IDataObject data,string? hint=null)
    {
        // Explorer and other file sources can also advertise text/HTML/URLs.
        // An actual file list is authoritative over those formats and UIA hints.
        if(data.GetDataPresent(DataFormats.FileDrop)&&data.GetData(DataFormats.FileDrop) is string[]{Length:>0})return null;
        return WebMediaPayload.Parse(Text(data,DataFormats.UnicodeText)??Text(data,"UniformResourceLocatorW")??Text(data,"UniformResourceLocator")??Text(data,DataFormats.Text),Text(data,DataFormats.Html,2_000_000),hint);
    }
}
