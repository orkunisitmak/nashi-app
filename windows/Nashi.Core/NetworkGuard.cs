using System.Net;
using System.Net.Sockets;
namespace Nashi.Core;

/// Keeps web downloads on the public internet. A pasted or dropped link must never make Nashi
/// (or yt-dlp on its behalf) reach the computer itself, the home network (router, printer, NAS)
/// or cloud metadata services, whether by an address, a local name or a name that resolves there.
public static class NetworkGuard
{
    private static readonly string[] LocalSuffixes=[".local",".localhost",".lan",".home",".internal",".intranet",".corp",".home.arpa",".localdomain"];
    /// Rejects addresses and names that point inside the machine or the local network.
    public static void CheckHost(Uri url)
    {
        var host=url.IdnHost.TrimEnd('.').ToLowerInvariant();
        if(host.Length==0||host=="localhost"||LocalSuffixes.Any(s=>host.EndsWith(s,StringComparison.Ordinal)))throw Refused();
        if(IPAddress.TryParse(host.Trim('[',']'),out var address)){if(!IsPublic(address))throw Refused();return;}
        // A single-label name ("router", "nas") is resolved by the local network.
        if(!host.Contains('.'))throw Refused();
    }
    /// Resolves the name and refuses it when any address it points to is not public.
    public static async Task CheckResolvedAsync(Uri url,CancellationToken token)
    {
        CheckHost(url);if(IPAddress.TryParse(url.IdnHost.Trim('[',']'),out _))return;
        IPAddress[] addresses;
        try{addresses=await Dns.GetHostAddressesAsync(url.IdnHost.TrimEnd('.'),token).ConfigureAwait(false);}
        catch(SocketException error){throw new HttpRequestException("This link’s address could not be found.",error);}
        if(addresses.Length==0||addresses.Any(a=>!IsPublic(a)))throw Refused();
    }
    /// For HttpClient: every connection (including after redirects) goes only to a checked public address,
    /// so a name cannot be re-pointed at the local network between the check and the request.
    public static SocketsHttpHandler Handler()=>new()
    {
        AllowAutoRedirect=false,ConnectTimeout=TimeSpan.FromSeconds(30),
        ConnectCallback=async(context,token)=>
        {
            var host=context.DnsEndPoint.Host;
            var addresses=IPAddress.TryParse(host.Trim('[',']'),out var literal)?[literal]:await Dns.GetHostAddressesAsync(host,token).ConfigureAwait(false);
            var target=addresses.FirstOrDefault(IsPublic);if(target is null||addresses.Any(a=>!IsPublic(a)))throw Refused();
            var socket=new Socket(target.AddressFamily,SocketType.Stream,ProtocolType.Tcp){NoDelay=true};
            try{await socket.ConnectAsync(new IPEndPoint(target,context.DnsEndPoint.Port),token).ConfigureAwait(false);return new NetworkStream(socket,true);}
            catch{socket.Dispose();throw;}
        }
    };
    public static bool IsPublic(IPAddress address)
    {
        if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();
        if(IPAddress.IsLoopback(address))return false;
        if(address.AddressFamily==AddressFamily.InterNetwork)
        {
            var b=address.GetAddressBytes();
            return !(b[0]==0||b[0]==10||b[0]==127||(b[0]==100&&b[1]>=64&&b[1]<=127)||(b[0]==169&&b[1]==254)||(b[0]==172&&b[1]>=16&&b[1]<=31)||(b[0]==192&&b[1]==168)||(b[0]==192&&b[1]==0&&b[2]==0)||(b[0]==198&&b[1] is 18 or 19)||b[0]>=224);
        }
        if(address.AddressFamily==AddressFamily.InterNetworkV6)
        {
            var b=address.GetAddressBytes();
            if(address.Equals(IPAddress.IPv6Any)||address.IsIPv6LinkLocal||address.IsIPv6SiteLocal||address.IsIPv6Multicast||(b[0]&0xFE)==0xFC)return false;
            // 6to4 and NAT64 forms carry an IPv4 address inside; judge that one.
            if(b[0]==0x20&&b[1]==0x02)return IsPublic(new IPAddress(b[2..6]));
            if(b[0]==0&&b[1]==0x64&&b[2]==0xff&&b[3]==0x9b)return IsPublic(new IPAddress(b[12..16]));
            return true;
        }
        return false;
    }
    private static ArgumentException Refused()=>new("This link points to your own computer or local network. Nashi only downloads from public internet addresses.");
}

/// Windows' "came from the internet" mark (the Zone.Identifier stream). Downloads get it, and
/// files Nashi makes from a marked file (extracted, converted) keep it, so SmartScreen and Office
/// Protected View still warn before anything inside is opened.
public static class MarkOfTheWeb
{
    public static string Internet(Uri source)=>"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl="+source.GetLeftPart(UriPartial.Path)+"\r\n";
    public static string? Read(string path)
    {
        try{using var reader=new StreamReader(path+":Zone.Identifier");var text=reader.ReadToEnd();return text.Length is >0 and <4096&&text.Contains("ZoneId=",StringComparison.OrdinalIgnoreCase)?text:null;}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException){return null;}
    }
    /// Marks files (and every file inside folders) with the given zone text; best effort.
    public static void Apply(IEnumerable<string> paths,string? zone)
    {
        if(zone is null)return;
        foreach(var path in paths)
        {
            var files=Directory.Exists(path)?Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories):File.Exists(path)?[path]:[];
            foreach(var file in files)try{File.WriteAllText(file+":Zone.Identifier",zone);}catch(Exception error)when(error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException){}
        }
    }
    /// The strongest mark among the sources (internet beats intranet beats none).
    public static string? Strongest(IEnumerable<string> sources)=>sources.Select(Read).Where(z=>z is not null).OrderByDescending(z=>z!.Contains("ZoneId=4")?4:z!.Contains("ZoneId=3")?3:z!.Contains("ZoneId=2")?2:1).FirstOrDefault();
}
