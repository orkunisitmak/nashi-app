using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
namespace Nashi.Core;

public sealed record WindowsUpdate(string Version,string Url,long Length,string EdSignature,string? SetupUrl=null,long SetupLength=0,string? SetupEdSignature=null);
public static class PublicUpdates
{
    public const string Repository="orkunisitmak/nashi-app";
    public const string Feed="https://github.com/"+Repository+"/releases/latest/download/windows-update.json";
    private const string PublicKey="VPJpPpgXKda4c3ZOvP4zH8koa3ht1jm6lJWNBr+2saU=";
    public static async Task<WindowsUpdate?> CheckAsync(Version current,CancellationToken token=default)
    {
        using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};client.DefaultRequestHeaders.UserAgent.ParseAdd("Nashi/"+current.ToString());
        using var response=await client.GetAsync(Feed,HttpCompletionOption.ResponseHeadersRead,token);
        if(response.StatusCode==System.Net.HttpStatusCode.NotFound)return null;response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(token);using var memory=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await stream.ReadAsync(buffer,token))>0){if(memory.Length+count>65536)throw new InvalidDataException("Update manifest is too large.");memory.Write(buffer,0,count);}
        var update=JsonSerializer.Deserialize<WindowsUpdate>(memory.ToArray(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Invalid update manifest.");
        Validate(update);return Normalize(Version.Parse(update.Version))>Normalize(current)?update:null;
    }
    public static Version Normalize(Version value)=>new(value.Major,value.Minor,Math.Max(0,value.Build),Math.Max(0,value.Revision));
    public static void Validate(WindowsUpdate update)
    {
        if(!Version.TryParse(update.Version,out _)||!Uri.TryCreate(update.Url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/"+Repository+"/releases/download/",StringComparison.Ordinal)||!string.IsNullOrEmpty(uri.UserInfo)||uri.Query.Length>0||update.Length<1||update.Length>1024L*1024*1024)throw new InvalidDataException("Invalid public update location or size.");
        try{if(Convert.FromBase64String(update.EdSignature).Length!=64)throw new InvalidDataException("Invalid update signature length.");}catch(FormatException e){throw new InvalidDataException("Invalid update signature.",e);}
        // The optional per-user installer package is optional so older portable
        // feeds keep validating; when one field is present all three must be.
        if(update.SetupUrl is null)
        {
            if(update.SetupLength!=0||update.SetupEdSignature is not null)throw new InvalidDataException("Invalid public update setup location.");
            return;
        }
        if(!Uri.TryCreate(update.SetupUrl,UriKind.Absolute,out var setup)||setup.Scheme!="https"||setup.Host!="github.com"||!setup.AbsolutePath.StartsWith("/"+Repository+"/releases/download/",StringComparison.Ordinal)||!string.IsNullOrEmpty(setup.UserInfo)||setup.Query.Length>0||update.SetupLength<1||update.SetupLength>1024L*1024*1024)throw new InvalidDataException("Invalid public update setup location or size.");
        try{if(Convert.FromBase64String(update.SetupEdSignature??"").Length!=64)throw new InvalidDataException("Invalid update setup signature length.");}catch(FormatException e){throw new InvalidDataException("Invalid update setup signature.",e);}
    }
    public static Task VerifyAsync(string archive,WindowsUpdate update,CancellationToken token=default)
    {
        Validate(update);return VerifyFileAsync(archive,update.Length,update.EdSignature,token);
    }
    public static async Task VerifyFileAsync(string file,long expectedLength,string edSignature,CancellationToken token=default)
    {
        if(new FileInfo(file).Length!=expectedLength)throw new InvalidDataException("Update size mismatch.");
        var verifier=new Ed25519Signer();verifier.Init(false,new Ed25519PublicKeyParameters(Convert.FromBase64String(PublicKey),0));
        await using var source=File.OpenRead(file);var buffer=new byte[81920];int count;
        while((count=await source.ReadAsync(buffer,token))>0)verifier.BlockUpdate(buffer,0,count);
        if(!verifier.VerifySignature(Convert.FromBase64String(edSignature)))throw new InvalidDataException("The update signature is invalid. Your existing app was kept.");
    }
    public static void ExtractVerified(string archive,string destination,CancellationToken token=default)
    {
        // Caller must verify the archive signature before calling. This extractor
        // still rejects traversal, links, aliases and excessive expansion.
        using var zip=ZipFile.OpenRead(archive);long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(zip.Entries.Count>10000)throw new InvalidDataException("Too many update files.");
        foreach(var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();total=checked(total+entry.Length);if(total>3L*1024*1024*1024)throw new InvalidDataException("Expanded update is too large.");
            var relative=entry.FullName.TrimEnd('/','\\');if(relative.Length==0)continue;var path=SafePaths.ArchiveEntry(destination,relative);
            if(!names.Add(path)||((entry.ExternalAttributes>>16)&0xF000)==0xA000||(entry.ExternalAttributes&0x400)!=0)throw new InvalidDataException("Unsafe update entry.");
            if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(path);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path);
        }
        if(!File.Exists(Path.Combine(destination,"Nashi.exe")))throw new InvalidDataException("The update does not contain Nashi.");
    }
}
