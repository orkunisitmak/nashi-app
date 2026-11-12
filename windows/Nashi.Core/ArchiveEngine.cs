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
    /// Names outputs of "Name.tar.gz" after "Name", as is already done for "Name.tgz".
    private static string Stem(string input)
    {
        var stem=Path.GetFileNameWithoutExtension(input);
        return FeatureCatalog.Extension(input)=="gz"&&Path.GetExtension(stem).Equals(".tar",StringComparison.OrdinalIgnoreCase)?Path.GetFileNameWithoutExtension(stem):stem;
    }
    /// <summary>Publish extracted files after applying the source archive's Windows origin mark.</summary>
    public static IReadOnlyList<string> Extract(string input,string destination,string? password=null,IProgress<OperationProgress>? progress=null,CancellationToken token=default)
        =>Extract(input,destination,password,progress,token,out _);

    /// <summary>Extract into staging and return the source zone for archive conversions.</summary>
    private static IReadOnlyList<string> Extract(string input,string destination,string? password,IProgress<OperationProgress>? progress,CancellationToken token,out string? zone)
    {
        input=Path.GetFullPath(input);
        using var tx=new ExportTransaction(destination);var folder=Path.Combine(tx.StagingDirectory,Stem(input)+" Extracted");Directory.CreateDirectory(folder);
        var sourcePath=input;
        using var original=File.OpenRead(input);
        zone=ReadInternetZone(input);
        var gzip=original.ReadByte()==0x1f&&original.ReadByte()==0x8b;original.Position=0;
        if(gzip)
        {
            sourcePath=Path.Combine(tx.StagingDirectory,"unpacked.tar");using(var source=new GZipStream(original,CompressionMode.Decompress,true))using(var output=new FileStream(sourcePath,FileMode.CreateNew)){var buffer=new byte[81920];long total=0;int read;while((read=source.Read(buffer))>0){token.ThrowIfCancellationRequested();total+=read;if(total>4L*1024*1024*1024)throw new InvalidDataException("GZIP expands beyond 4 GB.");output.Write(buffer,0,read);}}
            using var probe=File.OpenRead(sourcePath);var header=new byte[512];var count=probe.Read(header);var tar=count==512&&Encoding.ASCII.GetString(header,257,5)=="ustar";
            if(!tar)
            {
                probe.Close();
                var output=SafePaths.ArchiveEntry(folder,Path.GetFileNameWithoutExtension(input));
                File.Move(sourcePath,output);
                PreserveInternetZone(output,tx.StagingDirectory,zone);
                return tx.Publish([folder],token);
            }
        }
        // An empty password box means no password: SharpCompress would try "" and call it a bad password.
        password=string.IsNullOrEmpty(password)?null:password;
        try{return ExtractEntries(sourcePath,folder,tx,password,progress,token,zone);}
        // SharpCompress names a missing password in English, and a 7z whose names are encrypted fails to open with a wrong one
        // as "Data Error" (an internal exception type). Encrypted data cannot tell a wrong key from damage, so the message names both.
        catch(SharpCompress.Common.CryptographicException error)when(password is null){throw new InvalidDataException(PasswordNeeded,error);}
        catch(Exception error)when(password is not null&&error.GetType().FullName=="SharpCompress.Compressors.LZMA.DataErrorException"){throw new InvalidDataException(WrongPassword,error);}
        // A WinZip AES key that fails its check makes the ZIP unrecognizable, so the format probe gives up first.
        catch(SharpCompress.Common.ArchiveOperationException error)when(password is not null&&Encrypted(sourcePath)){throw new InvalidDataException(WrongPassword,error);}
        // A RAR whose headers are encrypted fails while listing its entries: RAR5 says the password did not match,
        // RAR4 decrypts the headers into garbage and reports an unknown header.
        catch(SharpCompress.Common.CryptographicException error)when(password is not null){throw new InvalidDataException(WrongPassword,error);}
        catch(SharpCompress.Common.InvalidFormatException error)when(password is not null&&Encrypted(sourcePath)){throw new InvalidDataException(WrongPassword,error);}
    }
    private static bool Encrypted(string path)
    {
        try{using var archive=ArchiveFactory.OpenArchive(path,new ReaderOptions{ArchiveEncoding=new ArchiveEncoding{CustomDecoder=DecodeName}});return archive.Entries.Any(e=>e.IsEncrypted);}
        catch(SharpCompress.Common.CryptographicException){return true;}
        catch(Exception error)when(error is not OperationCanceledException){return false;}
    }
    private const string PasswordNeeded="This archive is protected. Enter its password.",WrongPassword="The password is incorrect, or the archive is damaged.";

    /// <summary>Validate archive members and mark each file before publishing the extracted folder.</summary>
    private static IReadOnlyList<string> ExtractEntries(string sourcePath,string folder,ExportTransaction tx,string? password,IProgress<OperationProgress>? progress,CancellationToken token,string? zone)
    {
        using var archive=ArchiveFactory.OpenArchive(sourcePath,new ReaderOptions{Password=password,ArchiveEncoding=new ArchiveEncoding{CustomDecoder=DecodeName}});var entries=archive.Entries.ToArray();
        if(entries.Length>10000||entries.Sum(e=>Math.Max(0,e.Size))>4L*1024*1024*1024)throw new InvalidDataException("Archive exceeds 10,000 entries or 4 GB.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long copied=0;var index=0;
        foreach(var entry in entries)
        {
            token.ThrowIfCancellationRequested();var name=entry.Key?.TrimEnd('/','\\');while(name?.StartsWith("./",StringComparison.Ordinal)==true)name=name[2..];if(string.IsNullOrWhiteSpace(name)||name==".")continue;
            var output=SafePaths.ArchiveEntry(folder,name);if(!names.Add(output)||!string.IsNullOrEmpty(entry.LinkTarget))throw new InvalidDataException("Archive contains duplicate paths or links.");
            if(entry.IsDirectory){Directory.CreateDirectory(output);continue;}Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            try
            {
                using var source=entry.OpenEntryStream();using var target=new FileStream(output,FileMode.CreateNew);var buffer=new byte[81920];int count;
                while((count=source.Read(buffer))>0){token.ThrowIfCancellationRequested();copied+=count;if(copied>4L*1024*1024*1024)throw new InvalidDataException("Archive expands beyond 4 GB.");target.Write(buffer,0,count);}
            }
            // A wrong key fails as whatever SharpCompress meets in the garbled data: "bad password", an unknown method, a CRC…
            // Errors from writing the output (disk, permissions) keep their own message.
            catch(Exception error)when(entry.IsEncrypted&&password is not null&&error is not (OperationCanceledException or InvalidDataException or IOException or UnauthorizedAccessException)){throw new InvalidDataException(WrongPassword,error);}
            PreserveInternetZone(output,tx.StagingDirectory,zone);
            // Keep each file's modification date, as macOS does; the file must be closed first.
            // SharpCompress reports some RAR 4 file times only as CreatedTime.
            if((entry.LastModifiedTime??entry.CreatedTime) is {Year:>=1970} modified)File.SetLastWriteTime(output,modified);
            progress?.Report(new(++index/(double)Math.Max(1,entries.Length)*.98,"Extracting files…"));
        }
        return tx.Publish([folder],token);
    }

    private const string ZoneStream=":Zone.Identifier";

    /// <summary>Read the Internet or Restricted Sites zone without copying other origin metadata.</summary>
    private static string? ReadInternetZone(string archive)
    {
        if(!OperatingSystem.IsWindows())return null;
        FileStream stream;
        try{stream=File.OpenRead(archive+ZoneStream);}
        catch(FileNotFoundException){return null;}
        catch(IOException error)when(StreamsUnsupported(error)){return null;}

        // Windows writes ZoneId near the beginning. Bound the read and carry over only the security zone.
        using var reader=new StreamReader(stream);
        Span<char> text=stackalloc char[4096];
        var length=reader.ReadBlock(text);
        var inZoneTransfer=false;
        foreach(var range in text[..length].Split('\n'))
        {
            var line=text[..length][range].Trim();
            if(line.StartsWith("["))inZoneTransfer=line.Equals("[ZoneTransfer]",StringComparison.OrdinalIgnoreCase);
            if(!inZoneTransfer)continue;
            var separator=line.IndexOf('=');
            if(separator<0||!line[..separator].Trim().Equals("ZoneId",StringComparison.OrdinalIgnoreCase))continue;
            var zone=line[(separator+1)..].Trim();
            if(zone is "3" or "4")return zone.ToString();
        }
        return null;
    }

    /// <summary>Mark a staged output without following reparse points or publishing an unmarked file.</summary>
    private static void PreserveInternetZone(string output,string stagingDirectory,string? zone)
    {
        if(zone is null)return;
        for(var path=output;;path=Path.GetDirectoryName(path)??throw new InvalidDataException("Archive output is outside its staging directory."))
        {
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Cannot mark a linked archive output.");
            if(path==stagingDirectory)break;
        }

        try
        {
            using var stream=new FileStream(output+ZoneStream,FileMode.CreateNew);
            using var writer=new StreamWriter(stream);
            writer.Write("[ZoneTransfer]\r\nZoneId="+zone+"\r\n");
        }
        catch(IOException error)when(StreamsUnsupported(error))
        {
            throw new IOException("The destination cannot preserve this archive's internet security mark. Choose an NTFS folder.",error);
        }
    }

    /// <summary>Recognize filesystem errors that mean a named data stream cannot be opened.</summary>
    private static bool StreamsUnsupported(IOException error)=>error.HResult is
        unchecked((int)0x80070001) or // ERROR_INVALID_FUNCTION
        unchecked((int)0x80070032) or // ERROR_NOT_SUPPORTED
        unchecked((int)0x8007007B);   // ERROR_INVALID_NAME

    /// <summary>Repackage validated contents and transfer the source archive's Windows origin mark.</summary>
    public static IReadOnlyList<string> Convert(string input,string format,string destination,string? password=null,IProgress<OperationProgress>? progress=null,CancellationToken token=default)
    {
        if(format is not ("zip" or "tar" or "gz"))throw new ArgumentException("Choose ZIP, TAR or GZIP.");
        using var tx=new ExportTransaction(destination);var work=Path.Combine(tx.StagingDirectory,"contents");Directory.CreateDirectory(work);
        var extracted=Extract(input,work,password,progress,token,out var zone)[0];var output=Path.Combine(tx.StagingDirectory,Stem(input)+"."+format);
        if(format=="zip")
        {
            using var zip=ZipFile.Open(output,ZipArchiveMode.Create);foreach(var path in Directory.EnumerateFileSystemEntries(extracted,"*",SearchOption.AllDirectories)){token.ThrowIfCancellationRequested();var name=Path.GetRelativePath(extracted,path).Replace('\\','/');if(Directory.Exists(path)){zip.CreateEntry(name+"/");continue;}using var source=File.OpenRead(path);var item=zip.CreateEntry(name,CompressionLevel.Optimal);if(File.GetLastWriteTime(path) is {Year:>=1980 and <=2107} modified)item.LastWriteTime=modified;using var entry=item.Open();var buffer=new byte[81920];int read;while((read=source.Read(buffer))>0){token.ThrowIfCancellationRequested();entry.Write(buffer,0,read);}}
        }
        else
        {
            using var file=new FileStream(output,FileMode.CreateNew);using Stream stream=format=="gz"?new GZipStream(file,CompressionLevel.Optimal,true):file;
            using var writer=new TarWriter(stream,TarEntryFormat.Pax,true);
            foreach(var path in Directory.EnumerateFileSystemEntries(extracted,"*",SearchOption.AllDirectories)){token.ThrowIfCancellationRequested();writer.WriteEntry(path,Path.GetRelativePath(extracted,path).Replace('\\','/'));}
        }
        PreserveInternetZone(output,tx.StagingDirectory,zone);
        progress?.Report(new(.99,"Saving archive…"));return tx.Publish([output],token);
    }
}
