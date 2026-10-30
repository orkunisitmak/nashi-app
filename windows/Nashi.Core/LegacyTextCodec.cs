using System.Buffers.Binary;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace Nashi.Core;
public static class LegacyTextCodec
{
    static LegacyTextCodec()=>Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static string Decode(byte[] data)
    {
        if(Unicode(data,data.Length,out var bom) is {} unicode)return unicode.GetString(data.AsSpan(bom));
        try{return new UTF8Encoding(false,true).GetString(data).TrimStart('\uFEFF');}catch(DecoderFallbackException){return Ansi.GetString(data);}
    }
    // Decode for files, streamed to UTF-8 so large tables are never held in memory.
    public static async Task ToUtf8Async(string input,string output,CancellationToken token=default)
    {
        await using var source=File.OpenRead(input);var head=new byte[128*1024];var count=await source.ReadAtLeastAsync(head,head.Length,false,token);
        var encoding=Unicode(head.AsSpan(0,count),source.Length,out var bom);
        if(encoding is null){source.Position=0;encoding=await IsUtf8Async(source,token)?new UTF8Encoding(false):Ansi;}
        source.Position=bom;using var reader=new StreamReader(source,encoding,false);await using var writer=new StreamWriter(output,false,new UTF8Encoding(false));
        var buffer=new char[64*1024];var first=true;
        for(int read;(read=await reader.ReadAsync(buffer,token))>0;first=false){var skip=first&&buffer[0]=='\uFEFF'?1:0;await writer.WriteAsync(buffer.AsMemory(skip,read-skip),token);}
    }
    // UTF-32 and UTF-16 by byte order mark, or UTF-16 without one by its NUL bytes.
    private static Encoding? Unicode(ReadOnlySpan<byte> head,long length,out int bom)
    {
        bom=4;if(head.StartsWith(new byte[]{0xff,0xfe,0,0}))return new UTF32Encoding(false,false);if(head.StartsWith(new byte[]{0,0,0xfe,0xff}))return new UTF32Encoding(true,false);
        bom=2;if(head.StartsWith(new byte[]{0xff,0xfe}))return new UnicodeEncoding(false,false);if(head.StartsWith(new byte[]{0xfe,0xff}))return new UnicodeEncoding(true,false);
        bom=0;if(length>=4&&length%2==0){var count=(int)Math.Min(Math.Min(length,head.Length),128*1024)&~1;var odd=0;var even=0;for(var i=0;i<count;i+=2){if(head[i]==0)even++;if(head[i+1]==0)odd++;}if(odd*6>=count&&odd>even*2)return new UnicodeEncoding(false,false);if(even*6>=count&&even>odd*2)return new UnicodeEncoding(true,false);}
        return null;
    }
    private static async Task<bool> IsUtf8Async(Stream stream,CancellationToken token)
    {
        var decoder=new UTF8Encoding(false,true).GetDecoder();var buffer=new byte[64*1024];
        try{for(int read;(read=await stream.ReadAsync(buffer,token))>0;)decoder.GetCharCount(buffer,0,read,false);decoder.GetCharCount([],0,0,true);return true;}
        catch(DecoderFallbackException){return false;}
    }
    // Notepad, Excel and 7-Zip write legacy text and ZIP names in the system code pages
    // ("Language for non-Unicode programs"), which can differ from the regional format:
    // 1254/857 in Turkish (\u011F \u015F \u0131 \u0130), 1252/437 in English. When Windows uses UTF-8 for those,
    // and on other platforms, the current culture is the best guess.
    internal static (int Ansi,int Oem) SystemCodePages{get;set;}=DefaultCodePages();
    private static (int,int) DefaultCodePages()
    {
        if(OperatingSystem.IsWindows()&&GetACP() is var ansi&&ansi!=65001)return((int)ansi,(int)GetOEMCP());
        var text=System.Globalization.CultureInfo.CurrentCulture.TextInfo;return(text.ANSICodePage,text.OEMCodePage);
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]private static extern uint GetACP();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]private static extern uint GetOEMCP();
    internal static Encoding Oem=>CodePage(SystemCodePages.Oem,437);
    private static Encoding Ansi=>CodePage(SystemCodePages.Ansi,1252);
    private static Encoding CodePage(int codePage,int fallback){try{return Encoding.GetEncoding(codePage);}catch(Exception error)when(error is ArgumentException or NotSupportedException){return Encoding.GetEncoding(fallback);}}
    public static (byte[] Bytes,string Extension) Clipping(byte[] data,byte[]? resourceFork=null)
    {
        if(data.Length>200_000_000)throw new InvalidDataException("The clipping exceeds 200 MB.");
        object? model=null;if(data.AsSpan().StartsWith("bplist00"u8))model=BinaryPlist(data);else if(Decode(data).TrimStart().StartsWith("<?xml",StringComparison.Ordinal))
        {using var stream=new MemoryStream(data);using var reader=XmlReader.Create(stream,new(){DtdProcessing=DtdProcessing.Ignore,XmlResolver=null,MaxCharactersInDocument=20_000_000});var xml=XDocument.Load(reader);model=XmlPlist(xml.Root?.Elements().FirstOrDefault());}
        (byte[],string)? Find(object? node)
        {
            if(node is Dictionary<string,object?> dictionary){foreach(var (key,extension) in new[]{("public.rtf","rtf"),("public.utf8-plain-text","txt"),("public.plain-text","txt")})if(dictionary.TryGetValue(key,out var value)){if(value is byte[] bytes)return(bytes,extension);if(value is string text)return(Encoding.UTF8.GetBytes(text),extension);}foreach(var item in dictionary.Values)if(Find(item) is {} found)return found;}
            if(node is object?[] array)foreach(var item in array)if(Find(item) is {} found)return found;return null;
        }
        if(Find(model) is {} textValue)return textValue;if(data.Length>0&&model is null)return(Encoding.UTF8.GetBytes(Decode(data)),"txt");
        if(resourceFork is {Length:>=16})
        {
            int U16(int offset){if(offset<0||offset>resourceFork.Length-2)throw new InvalidDataException("Damaged clipping resource.");return BinaryPrimitives.ReadUInt16BigEndian(resourceFork.AsSpan(offset,2));}
            int U32(int offset){if(offset<0||offset>resourceFork.Length-4)throw new InvalidDataException("Damaged clipping resource.");return checked((int)BinaryPrimitives.ReadUInt32BigEndian(resourceFork.AsSpan(offset,4)));}
            var start=U32(0);var map=U32(4);var types=map+U16(map+24);var count=U16(types);if(count>=4096)throw new InvalidDataException("Too many clipping resources.");var candidates=new Dictionary<string,byte[]>();
            for(var i=0;i<=count;i++){var item=types+2+i*8;var type=Encoding.ASCII.GetString(resourceFork,item,4);var resources=U16(item+4);var refs=U16(item+6);if(resources>=4096)throw new InvalidDataException("Too many clipping resources.");for(var n=0;n<=resources;n++){var reference=types+refs+n*12;if(reference<0||reference>resourceFork.Length-8)throw new InvalidDataException("Damaged clipping references.");var offset=start+(resourceFork[reference+5]<<16|resourceFork[reference+6]<<8|resourceFork[reference+7]);var size=U32(offset);if(size<0||offset+4>resourceFork.Length-size)throw new InvalidDataException("Damaged clipping payload.");candidates.TryAdd(type,resourceFork.AsSpan(offset+4,size).ToArray());}}
            foreach(var type in new[]{"RTF ","utf8","utxt","TEXT"})if(candidates.TryGetValue(type,out var bytes)){if(type=="RTF ")return(bytes,"rtf");var encoding=type=="utxt"?Encoding.BigEndianUnicode:type=="TEXT"?Encoding.GetEncoding(10000):Encoding.UTF8;return(Encoding.UTF8.GetBytes(encoding.GetString(bytes)),"txt");}
        }
        throw new InvalidDataException("The clipping has no readable text. Include its original resource fork/AppleDouble sidecar, or paste its text into Nashi.");
    }
    private static object? XmlPlist(XElement? node)
    {
        if(node is null)return null;if(node.Name.LocalName=="dict"){var children=node.Elements().ToArray();var result=new Dictionary<string,object?>();for(var n=0;n<children.Length-1;n+=2)result[children[n].Value]=XmlPlist(children[n+1]);return result;}if(node.Name.LocalName=="array")return node.Elements().Select(XmlPlist).ToArray();if(node.Name.LocalName=="data")return Convert.FromBase64String(node.Value);return node.Value;
    }
    private static object? BinaryPlist(byte[] data)
    {
        if(data.Length<40)throw new InvalidDataException("Damaged binary plist.");var trailer=data.Length-32;var offsetSize=data[trailer+6];var refSize=data[trailer+7];
        ulong UInt(int start,int count){if(count<1||count>8||start<0||start>data.Length-count)throw new InvalidDataException("Damaged plist integer.");ulong value=0;for(var i=0;i<count;i++)value=value<<8|data[start+i];return value;}
        var objects=UInt(trailer+8,8);var root=UInt(trailer+16,8);var table=UInt(trailer+24,8);if(objects>1_000_000||root>=objects||table>(ulong)trailer||offsetSize is <1 or >8||refSize is <1 or >8||objects*(uint)offsetSize>(ulong)trailer-table)throw new InvalidDataException("Invalid plist object table.");
        var cache=new Dictionary<ulong,object?>();var active=new HashSet<ulong>();long budget=0;
        object? Read(ulong index,int depth)
        {
            if(cache.TryGetValue(index,out var cached))return cached;if(!active.Add(index))throw new InvalidDataException("Cyclic plist reference.");try{var value=ReadValue(index,depth);cache[index]=value;return value;}finally{active.Remove(index);}
        }
        object? ReadValue(ulong index,int depth)
        {
            if(index>=objects||depth>64)throw new InvalidDataException("Invalid plist reference.");var offset=checked((int)UInt(checked((int)(table+index*offsetSize)),offsetSize));if(offset>=trailer)throw new InvalidDataException("Invalid plist offset.");var marker=data[offset++];var kind=marker>>4;var size=marker&15;
            var length=size==15&&kind is 4 or 5 or 6 or 10 or 13?ReadLength():size;
            int ReadLength(){if(offset>=trailer||data[offset]>>4!=1)throw new InvalidDataException("Invalid plist length.");var bytes=1<<(data[offset++]&15);var count=checked((int)UInt(offset,bytes));offset+=bytes;return count;}
            void Bounds(long bytes){if(bytes<0||offset<0||bytes>trailer-offset)throw new InvalidDataException("Invalid plist data range.");}
            if(kind==4){Bounds(length);return data.AsSpan(offset,length).ToArray();}if(kind==5){Bounds(length);return Encoding.ASCII.GetString(data,offset,length);}if(kind==6){Bounds((long)length*2);return Encoding.BigEndianUnicode.GetString(data,offset,checked(length*2));}
            if(kind is 10 or 13){budget+=length;if(length>1_000_000||budget>2_000_000)throw new InvalidDataException("Plist contains too many references.");}
            if(kind==10){Bounds((long)length*refSize);var array=new object?[length];for(var n=0;n<length;n++)array[n]=Read(UInt(offset+n*refSize,refSize),depth+1);return array;}
            if(kind==13){Bounds((long)length*refSize*2);var dictionary=new Dictionary<string,object?>();for(var n=0;n<length;n++){var key=Read(UInt(offset+n*refSize,refSize),depth+1) as string;if(key is not null)dictionary[key]=Read(UInt(offset+(length+n)*refSize,refSize),depth+1);}return dictionary;}return null;
        }
        return Read(root,0);
    }
    public static byte[]? AppleDoubleResource(byte[] data)
    {
        if(data.Length<26||BinaryPrimitives.ReadUInt32BigEndian(data)!=0x00051607)return null;var count=BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(24,2));if(count>4096||26+count*12>data.Length)throw new InvalidDataException("Invalid AppleDouble header.");
        for(var i=0;i<count;i++){var entry=26+i*12;if(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry,4))!=2)continue;var offset=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry+4,4));var length=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry+8,4));if(offset>data.Length||length>data.Length-offset)throw new InvalidDataException("Invalid clipping resource sidecar.");return data.AsSpan((int)offset,(int)length).ToArray();}return null;
    }
}
