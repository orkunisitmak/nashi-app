using System.Formats.Tar;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Text;
namespace Nashi.Core;
public static class ArchiveEngine
{
    static ArchiveEngine()=>Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    private static readonly UTF8Encoding StrictUtf8=new(false,true);
    // Names without the UTF-8 flag are UTF-8 from macOS and EPUB tools, but the OEM code page
    // from 7-Zip and Explorer on Windows (cp857 in Turkish, cp437 as the ZIP spec's default).
    // Like 7-Zip, keep valid UTF-8 and read anything else in the OEM code page.
    private static string DecodeName(byte[] bytes,int index,int count,EncodingType type)
    {
        if(type==EncodingType.UTF8)return Encoding.UTF8.GetString(bytes,index,count);
        try{return StrictUtf8.GetString(bytes,index,count);}
        catch(DecoderFallbackException){return LegacyTextCodec.Oem.GetString(bytes,index,count);}
    }
    public static IReadOnlyList<string> Extract(string input,string destination,string? password=null,IProgress<OperationProgress>? progress=null,CancellationToken token=default)
    {
        using var tx=new ExportTransaction(destination);var folder=Path.Combine(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+" Extracted");Directory.CreateDirectory(folder);
        var sourcePath=input;
        using(var original=File.OpenRead(input))
        {
            var gzip=original.ReadByte()==0x1f&&original.ReadByte()==0x8b;original.Position=0;
            if(gzip)
            {
                sourcePath=Path.Combine(tx.StagingDirectory,"unpacked.tar");using(var source=new GZipStream(original,CompressionMode.Decompress,true))using(var output=new FileStream(sourcePath,FileMode.CreateNew)){var buffer=new byte[81920];long total=0;int read;while((read=source.Read(buffer))>0){token.ThrowIfCancellationRequested();total+=read;if(total>4L*1024*1024*1024)throw new InvalidDataException("GZIP expands beyond 4 GB.");output.Write(buffer,0,read);}}
                using var probe=File.OpenRead(sourcePath);var header=new byte[512];var count=probe.Read(header);var tar=count==512&&Encoding.ASCII.GetString(header,257,5)=="ustar";
                if(!tar){probe.Close();File.Move(sourcePath,SafePaths.ArchiveEntry(folder,Path.GetFileNameWithoutExtension(input)));return tx.Publish([folder],token);}
            }
        }
        using var archive=ArchiveFactory.OpenArchive(sourcePath,new ReaderOptions{Password=password,ArchiveEncoding=new ArchiveEncoding{CustomDecoder=DecodeName}});var entries=archive.Entries.ToArray();
        if(entries.Length>10000||entries.Sum(e=>Math.Max(0,e.Size))>4L*1024*1024*1024)throw new InvalidDataException("Archive exceeds 10,000 entries or 4 GB.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long copied=0;var index=0;
        foreach(var entry in entries)
        {
            token.ThrowIfCancellationRequested();var name=entry.Key?.TrimEnd('/','\\');if(string.IsNullOrWhiteSpace(name))continue;
            var output=SafePaths.ArchiveEntry(folder,name);if(!names.Add(output)||!string.IsNullOrEmpty(entry.LinkTarget))throw new InvalidDataException("Archive contains duplicate paths or links.");
            if(entry.IsDirectory){Directory.CreateDirectory(output);continue;}Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var source=entry.OpenEntryStream();using var target=new FileStream(output,FileMode.CreateNew);var buffer=new byte[81920];int count;
            while((count=source.Read(buffer))>0){token.ThrowIfCancellationRequested();copied+=count;if(copied>4L*1024*1024*1024)throw new InvalidDataException("Archive expands beyond 4 GB.");target.Write(buffer,0,count);}
            progress?.Report(new(++index/(double)Math.Max(1,entries.Length)*.98,"Extracting files…"));
        }
        return tx.Publish([folder],token);
    }
    public static IReadOnlyList<string> Convert(string input,string format,string destination,string? password=null,IProgress<OperationProgress>? progress=null,CancellationToken token=default)
    {
        if(format is not ("zip" or "tar" or "gz"))throw new ArgumentException("Choose ZIP, TAR or GZIP.");
        using var tx=new ExportTransaction(destination);var work=Path.Combine(tx.StagingDirectory,"contents");Directory.CreateDirectory(work);
        var extracted=Extract(input,work,password,progress,token)[0];var output=Path.Combine(tx.StagingDirectory,Path.GetFileNameWithoutExtension(input)+"."+format);
        if(format=="zip")
        {
            using var zip=ZipFile.Open(output,ZipArchiveMode.Create);foreach(var path in Directory.EnumerateFileSystemEntries(extracted,"*",SearchOption.AllDirectories)){token.ThrowIfCancellationRequested();var name=Path.GetRelativePath(extracted,path).Replace('\\','/');if(Directory.Exists(path)){zip.CreateEntry(name+"/");continue;}using var source=File.OpenRead(path);using var entry=zip.CreateEntry(name,CompressionLevel.Optimal).Open();var buffer=new byte[81920];int read;while((read=source.Read(buffer))>0){token.ThrowIfCancellationRequested();entry.Write(buffer,0,read);}}
        }
        else
        {
            using var file=new FileStream(output,FileMode.CreateNew);using Stream stream=format=="gz"?new GZipStream(file,CompressionLevel.Optimal,true):file;
            using var writer=new TarWriter(stream,TarEntryFormat.Pax,true);
            foreach(var path in Directory.EnumerateFileSystemEntries(extracted,"*",SearchOption.AllDirectories)){token.ThrowIfCancellationRequested();writer.WriteEntry(path,Path.GetRelativePath(extracted,path).Replace('\\','/'));}
        }
        progress?.Report(new(.99,"Saving archive…"));return tx.Publish([output],token);
    }
}
