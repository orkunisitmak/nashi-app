using System.Net;
using System.Net.Sockets;

namespace Nashi.Core;

internal static class PublicMediaAddress
{
    /// <summary>Checks literals and local hostname suffixes before DNS resolution.</summary>
    internal static bool IsPublicHost(string host)
    {
        host = host.TrimEnd('.').Trim('[', ']');
        if (IPAddress.TryParse(host, out var address)) return IsPublic(address);
        return host.Contains('.') && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Accepts globally routed unicast and checks mapped IPv6 addresses as IPv4.</summary>
    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            var first = bytes[0];
            var second = bytes[1];
            var third = bytes[2];
            return !(first is 0 or 10 or 127 or >= 224
                || first == 100 && second is >= 64 and <= 127
                || first == 169 && second == 254
                || first == 172 && second is >= 16 and <= 31
                || first == 192 && (second == 168 || second == 0 && third is 0 or 2 || second == 88 && third == 99)
                || first == 198 && (second is 18 or 19 || second == 51 && third == 100)
                || first == 203 && second == 0 && third == 113);
        }

        return address.ScopeId == 0 && (bytes[0] & 0xe0) == 0x20
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && (bytes[2] < 2 || bytes[2] == 0x0d && bytes[3] == 0xb8))
            && !(bytes[0] == 0x20 && bytes[1] == 0x02)
            && !(bytes[0] == 0x3f && bytes[1] == 0xff && (bytes[2] & 0xf0) == 0);
    }

    /// <summary>Refuses a DNS answer containing any nonpublic address.</summary>
    internal static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken token)
    {
        if (!IsPublicHost(host)) throw new ArgumentException("Choose a public web address.");
        var addresses = await Dns.GetHostAddressesAsync(host.Trim('[', ']'), token);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublic(address)))
            throw new ArgumentException("Choose a public web address.");
        return addresses;
    }

    /// <summary>Connects to the checked numeric addresses without a second DNS lookup.</summary>
    internal static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await ResolveAsync(endpoint.Host, token);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            // Connect to the checked addresses so a second DNS lookup cannot change the destination.
            await socket.ConnectAsync(addresses, endpoint.Port, token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
