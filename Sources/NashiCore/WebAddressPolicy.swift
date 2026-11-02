import Foundation

/// Downloads leave the machine, so they may only target public internet hosts.
///
/// A pasted link is user input, but an extractor can also report arbitrary
/// media URLs, and a public server can redirect anywhere. Loopback, link-local
/// (including the 169.254.169.254 metadata endpoint), private, CGNAT, multicast,
/// documentation and reserved ranges are therefore refused before URLSession or
/// yt-dlp receives a URL.
public enum WebAddressPolicy {
    /// One message for every blocked destination, so the UI never has to explain
    /// which internal range was hit.
    public static let blockedMessage = "Downloads work with public web addresses only. Private, local and link-local hosts are blocked."

    /// Checks what can be decided without touching the network: scheme, host
    /// shape, credentials in the URL, a named local host or a literal IP.
    @discardableResult
    public static func syntax(_ url: URL) throws -> URL {
        guard let scheme = url.scheme?.lowercased(), ["http", "https"].contains(scheme),
              let host = url.host, !host.isEmpty, url.user == nil, url.password == nil else {
            throw NashiError.invalidInput("Paste one complete http:// or https:// media link.")
        }
        let name = normalized(host)
        guard !name.isEmpty, !isLocalName(name) else { throw NashiError.invalidInput(blockedMessage) }
        if isNumericAddress(name) {
            guard isPublicAddress(name) else { throw NashiError.invalidInput(blockedMessage) }
        }
        return url
    }

    /// Checks the host and every address it resolves to. A host that does not
    /// resolve is refused here; the download would fail anyway.
    @discardableResult
    public static func validate(_ url: URL) throws -> URL {
        try validate(url, resolve: systemAddresses)
    }

    /// Same check with an injectable resolver, so tests never depend on DNS.
    @discardableResult
    static func validate(_ url: URL, resolve: (String) -> [String]) throws -> URL {
        try syntax(url)
        let name = normalized(url.host ?? "")
        guard !isNumericAddress(name) else { return url }
        let addresses = resolve(name)
        guard !addresses.isEmpty, addresses.allSatisfy(isPublicAddress) else { throw NashiError.invalidInput(blockedMessage) }
        return url
    }

    /// True for addresses a public server could legitimately use. Anything the
    /// parser does not understand counts as private, so a malformed value can
    /// never slip through.
    public static func isPublicAddress(_ address: String) -> Bool {
        guard let parsed = bytes(of: address) else { return false }
        return parsed.family == AF_INET ? isPublicIPv4(parsed.value) : isPublicIPv6(parsed.value)
    }

    /// Resolves `host` through the system resolver, returning numeric addresses.
    static func systemAddresses(_ host: String) -> [String] {
        var hints = addrinfo(ai_flags: AI_ADDRCONFIG, ai_family: AF_UNSPEC, ai_socktype: SOCK_STREAM,
                             ai_protocol: 0, ai_addrlen: 0, ai_canonname: nil, ai_addr: nil, ai_next: nil)
        var list: UnsafeMutablePointer<addrinfo>?
        guard getaddrinfo(host, nil, &hints, &list) == 0, let first = list else { return [] }
        defer { freeaddrinfo(first) }
        var result: [String] = []
        var current: UnsafeMutablePointer<addrinfo>? = first
        while let info = current, let address = info.pointee.ai_addr {
            var buffer = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            if getnameinfo(address, info.pointee.ai_addrlen, &buffer, socklen_t(buffer.count), nil, 0, NI_NUMERICHOST) == 0 {
                result.append(String(cString: buffer))
            }
            current = info.pointee.ai_next
        }
        return result
    }

    private static func normalized(_ host: String) -> String {
        host.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "."))
    }

    private static func isLocalName(_ name: String) -> Bool {
        let suffixes = [".localhost", ".local", ".internal", ".home.arpa"]
        if name == "localhost" || suffixes.contains(where: { name.hasSuffix($0) }) { return true }
        // A single-label name is an intranet name, never a public domain.
        return isNumericAddress(name) == false && !name.contains(".")
    }

    private static func isNumericAddress(_ name: String) -> Bool {
        bytes(of: name) != nil
    }

    private static func bytes(of address: String) -> (family: Int32, value: [UInt8])? {
        var v4 = in_addr()
        if inet_pton(AF_INET, address, &v4) == 1 {
            return (AF_INET, withUnsafeBytes(of: &v4) { Array($0) })
        }
        var v6 = in6_addr()
        if inet_pton(AF_INET6, address, &v6) == 1 {
            return (AF_INET6, withUnsafeBytes(of: &v6) { Array($0) })
        }
        return nil
    }

    private static func isPublicIPv4(_ bytes: [UInt8]) -> Bool {
        guard bytes.count == 4 else { return false }
        let ip = bytes.reduce(UInt32(0)) { ($0 << 8) | UInt32($1) }
        let blocked: [(UInt32, Int)] = [
            (0x0000_0000, 8),   // "this" network
            (0x0A00_0000, 8),   // RFC 1918
            (0x6440_0000, 10),  // RFC 6598 carrier-grade NAT
            (0x7F00_0000, 8),   // loopback
            (0xA9FE_0000, 16),  // link-local, includes 169.254.169.254
            (0xAC10_0000, 12),  // RFC 1918
            (0xC000_0000, 24),  // protocol assignments
            (0xC000_0200, 24),  // TEST-NET-1
            (0xC0A8_0000, 16),  // RFC 1918
            (0xC612_0000, 15),  // benchmarking
            (0xC633_6400, 24),  // TEST-NET-2
            (0xCB00_7100, 24),  // TEST-NET-3
            (0xE000_0000, 4),   // multicast
            (0xF000_0000, 4),   // reserved and broadcast
        ]
        return !blocked.contains { base, bits in ip >> (32 - bits) == base >> (32 - bits) }
    }

    private static func isPublicIPv6(_ bytes: [UInt8]) -> Bool {
        guard bytes.count == 16 else { return false }
        if bytes.allSatisfy({ $0 == 0 }) { return false }
        if bytes[0..<15].allSatisfy({ $0 == 0 }) && bytes[15] == 1 { return false } // ::1
        if bytes[0] & 0xFE == 0xFC { return false }                               // fc00::/7 unique local
        if bytes[0] == 0xFE, bytes[1] & 0xC0 == 0x80 { return false }             // fe80::/10 link-local
        if bytes[0] == 0xFE, bytes[1] & 0xC0 == 0xC0 { return false }             // fec0::/10 site-local
        if bytes[0] == 0xFF { return false }                                      // multicast
        if bytes[0] == 0x01, bytes[1] == 0x00, bytes[2..<8].allSatisfy({ $0 == 0 }) { return false } // 100::/64 discard
        if bytes[0..<12].allSatisfy({ $0 == 0 }) { return isPublicIPv4(Array(bytes[12..<16])) }     // ::a.b.c.d
        if bytes[0..<10].allSatisfy({ $0 == 0 }), bytes[10] == 0xFF, bytes[11] == 0xFF {            // ::ffff:a.b.c.d
            return isPublicIPv4(Array(bytes[12..<16]))
        }
        if bytes[0] == 0x00, bytes[1] == 0x64, bytes[2] == 0xFF, bytes[3] == 0x9B {                 // 64:ff9b::/96
            return isPublicIPv4(Array(bytes[12..<16]))
        }
        if bytes[0] == 0x20, bytes[1] == 0x01, bytes[2] == 0x0D, bytes[3] == 0xB8 { return false } // 2001:db8::/32
        if bytes[0] == 0x20, bytes[1] == 0x02 { return false }                                    // 6to4
        if bytes[0] == 0x20, bytes[1] == 0x01, bytes[2] == 0x00, bytes[3] == 0x00 { return false } // Teredo
        return true
    }
}

/// Refuses a redirect that would leave the public internet. Used by the HEAD
/// probe and by the direct download session, so a public URL cannot bounce to
/// an internal service after the first check.
final class PublicRedirectPolicy: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
    func urlSession(_ session: URLSession, task: URLSessionTask,
                    willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest,
                    completionHandler: @escaping (URLRequest?) -> Void) {
        guard let url = request.url, (try? WebAddressPolicy.validate(url)) != nil else {
            completionHandler(nil)
            return
        }
        completionHandler(request)
    }
}
