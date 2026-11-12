using System.Net;
using System.Net.Sockets;
namespace Nashi.Core;
// Web links may only point at public hosts, never at this machine, the local network or a cloud metadata address.
public static class PublicWebHost
{
    public static bool Allows(string host)
    {
        host=host.TrimEnd('.');if(host.Length==0||host.Equals("localhost",StringComparison.OrdinalIgnoreCase)||host.EndsWith(".localhost",StringComparison.OrdinalIgnoreCase)||host.EndsWith(".local",StringComparison.OrdinalIgnoreCase))return false;
        return !IPAddress.TryParse(host,out var address)||IsPublic(address);
    }
    public static bool IsPublic(IPAddress address)
    {
        if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();var b=address.GetAddressBytes();
        if(address.AddressFamily==AddressFamily.InterNetwork)return !(b[0] is 0 or 10 or 127||b[0]>=224||b[0]==100&&b[1] is >=64 and <128||b[0]==169&&b[1]==254||b[0]==172&&b[1] is >=16 and <32||b[0]==192&&b[1]==168||b[0]==192&&b[1]==0&&b[2] is 0 or 2||b[0]==198&&b[1] is 18 or 19||b[0]==198&&b[1]==51&&b[2]==100||b[0]==203&&b[1]==0&&b[2]==113);
        if(address.AddressFamily!=AddressFamily.InterNetworkV6)return false;
        // ::, ::1, IPv4-compatible, unique local, link-local, site-local, multicast and Teredo.
        if(b.Take(10).All(x=>x==0)||(b[0]&0xFE)==0xFC||b[0]==0xFE&&(b[1]&0x80)==0x80||b[0]==0xFF||b[0]==0x20&&b[1]==0x01&&b[2]==0&&b[3]==0)return false;
        // NAT64 and 6to4 carry an IPv4 address that the gateway connects to.
        if(b[0]==0&&b[1]==0x64&&b[2]==0xFF&&b[3]==0x9B)return b[4]==0&&b[5]==0&&IsPublic(new IPAddress(b[12..16]));
        return b[0]!=0x20||b[1]!=0x02||IsPublic(new IPAddress(b[2..6]));
    }
}
