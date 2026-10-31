import Foundation
import Network

/// Rejects loopback, link-local, private, and other non-routable hosts so web
/// downloads cannot target local services or cloud metadata endpoints. The
/// policy is fail-closed: a host that cannot be shown to resolve only to
/// public addresses is refused.
enum PublicWebHost {
    /// Maps a hostname to its resolved address literals; nil means the lookup
    /// failed. Tests inject a scripted resolver.
    typealias Resolver = @Sendable (String) -> [String]?

    /// Syntax and literal policy applied to the URL host before any lookup.
    static func allows(_ host: String) -> Bool {
        guard let name = normalized(host) else { return false }
        if name == "localhost" || name.hasSuffix(".localhost") || name.hasSuffix(".local") { return false }
        // Foundation parses leading-zero octal spellings (0177.0.0.1) that
        // inet_pton rejects and inet_aton maps to loopback; classify numerically.
        if name.contains("."), name.split(separator: ".").allSatisfy({ segment in segment.allSatisfy({ $0.isNumber }) && !segment.isEmpty }) {
            return isOctalDecimalIPv4(name)
        }
        if let address = IPv4Address(name) { return isPublic(address) }
        if let address = IPv6Address(name) { return isPublic(address) }
        if name.hasPrefix("0x") || name.hasPrefix("0o") { return false }
        // inet_aton accepts decimal, octal, and abbreviated spellings such as
        // 2130706433 or 127.1 that IPv4Address rejects, and the system resolver
        // still connects them. A name spelled without any letter cannot be a
        // real DNS label sequence (top-level labels are never all digits), so
        // refuse it instead of betting on which parser agrees.
        if name.range(of: "[^0-9.]", options: .regularExpression) == nil { return false }
        return true
    }

    /// Full egress decision: the literal policy plus, for hostnames, a check
    /// of every DNS answer. One non-public answer refuses the whole host, and
    /// a failed or empty lookup refuses it too.
    static func egressAllowed(_ host: String, resolve: Resolver = { systemAddresses($0) }) -> Bool {
        guard allows(host), let name = normalized(host) else { return false }
        if IPv4Address(name) != nil || IPv6Address(name) != nil { return true }
        guard let answers = resolve(name), !answers.isEmpty else { return false }
        return answers.allSatisfy(isPublicLiteral)
    }

    /// Lowercased host without brackets, IPv6 zone, or root dot; nil when empty.
    private static func normalized(_ host: String) -> String? {
        var name = host.trimmingCharacters(in: CharacterSet(charactersIn: "[]")).lowercased()
        if let zone = name.firstIndex(of: "%") { name = String(name[..<zone]) }
        while name.hasSuffix(".") { name.removeLast() }
        return name.isEmpty ? nil : name
    }

    private static func isPublicLiteral(_ value: String) -> Bool {
        let name = value.lowercased()
        if let address = IPv4Address(name) { return isPublic(address) }
        if let address = IPv6Address(name) { return isPublic(address) }
        return false
    }

    /// inet_aton semantics: a leading zero makes a segment octal, the last
    /// present segment absorbs the remaining 32-bit tail, and an out-of-range
    /// segment makes the whole spelling invalid (so it can never connect).
    private static func isOctalDecimalIPv4(_ name: String) -> Bool {
        let parts = name.split(separator: ".", omittingEmptySubsequences: false)
        guard (1...4).contains(parts.count) else { return false }
        var values: [UInt64] = []
        for part in parts {
            guard !part.isEmpty else { return false }
            let value: UInt64?
            if part.count > 1, part.hasPrefix("0") {
                guard part.dropFirst().allSatisfy({ "0"..."7" ~= $0 }) else { return false }
                value = UInt64(part.dropFirst(), radix: 8)
            } else {
                value = UInt64(part)
            }
            guard let value else { return false }
            values.append(value)
        }
        let last = values.count - 1
        if values.dropLast().contains(where: { $0 > 255 }) { return false }
        // The last segment absorbs the 5 - count remaining bytes of the address.
        if values[last] >= (UInt64(1) << (8 * UInt64(5 - parts.count))) { return false }
        var address: UInt64 = 0
        for value in values.dropLast() { address = (address << 8) | value }
        address = (address << (8 * UInt64(5 - parts.count))) | values[last]
        let bytes = [UInt8((address >> 24) & 0xff), UInt8((address >> 16) & 0xff), UInt8((address >> 8) & 0xff), UInt8(address & 0xff)]
        return isPublicIPv4Bytes(bytes)
    }

    private static func isPublic(_ address: IPv4Address) -> Bool {
        guard [UInt8](address.rawValue).count == 4 else { return false }
        return isPublicIPv4Bytes([UInt8](address.rawValue))
    }

    private static func isPublicIPv4Bytes(_ o: [UInt8]) -> Bool {
        guard o.count == 4 else { return false }
        if o[0] == 0 { return false }
        if o[0] == 127 { return false }
        if o[0] == 169 && o[1] == 254 { return false }
        if o[0] >= 224 { return false }
        if o[0] == 10 { return false }
        if o[0] == 100 && (o[1] & 0xC0) == 64 { return false }
        if o[0] == 172 && (o[1] & 0xF0) == 16 { return false }
        if o[0] == 192 && o[1] == 168 { return false }
        if o[0] == 192 && o[1] == 0 && (o[2] == 0 || o[2] == 2) { return false }
        if o[0] == 198 && (o[1] == 18 || o[1] == 19) { return false }
        if o[0] == 198 && o[1] == 51 && o[2] == 100 { return false }
        if o[0] == 203 && o[1] == 0 && o[2] == 113 { return false }
        if o[0] >= 240 { return false }
        return true
    }

    private static func isPublic(_ address: IPv6Address) -> Bool {
        if address.isLoopback || address.isLinkLocal || address.isMulticast { return false }
        let b = [UInt8](address.rawValue)
        guard b.count == 16 else { return false }
        // Unique-local fc00::/7
        if (b[0] & 0xfe) == 0xfc { return false }
        // Unspecified ::
        if b.allSatisfy({ $0 == 0 }) { return false }
        // 6to4 2002::/16 embeds an IPv4 destination; re-check it.
        if b[0] == 0x20 && b[1] == 0x02 { return isEmbeddedIPv4(b, offset: 2) }
        // Teredo 2001:0000::/32 carries an obfuscated client address; macOS
        // never builds such routes, so refuse the family outright.
        if b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00 { return false }
        if b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b {
            // NAT64: 64:ff9b:1::/48 is local-use; the well-known 64:ff9b::/96
            // embeds an IPv4 destination that must pass the IPv4 policy.
            if b[4] == 0x00 && b[5] == 0x01 { return false }
            return isEmbeddedIPv4(b, offset: 12)
        }
        // IPv4-mapped ::ffff:0:0/96 — apply the same IPv4 policy.
        if b.prefix(10).allSatisfy({ $0 == 0 }), b[10] == 0xff, b[11] == 0xff {
            return isEmbeddedIPv4(b, offset: 12)
        }
        // IPv4-compatible ::0:0/96 (deprecated, still parsed) — same again.
        if b.prefix(12).allSatisfy({ $0 == 0 }) { return isEmbeddedIPv4(b, offset: 12) }
        return true
    }

    private static func isEmbeddedIPv4(_ bytes: [UInt8], offset: Int) -> Bool {
        let embedded = IPv4Address("\(bytes[offset]).\(bytes[offset + 1]).\(bytes[offset + 2]).\(bytes[offset + 3])")
        return embedded.map(isPublic) ?? false
    }

    private static func systemAddresses(_ host: String) -> [String]? {
        var hints = addrinfo(ai_flags: 0, ai_family: AF_UNSPEC, ai_socktype: SOCK_STREAM,
                             ai_protocol: IPPROTO_TCP, ai_addrlen: 0, ai_canonname: nil, ai_addr: nil, ai_next: nil)
        var first: UnsafeMutablePointer<addrinfo>?
        guard getaddrinfo(host, nil, &hints, &first) == 0, let head = first else { return nil }
        defer { freeaddrinfo(head) }
        var answers: [String] = []
        var cursor: UnsafeMutablePointer<addrinfo>? = head
        while let node = cursor {
            cursor = node.pointee.ai_next
            guard let raw = node.pointee.ai_addr else { continue }
            var text = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            switch node.pointee.ai_family {
            case AF_INET:
                var v4 = raw.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee }
                if inet_ntop(AF_INET, &v4.sin_addr, &text, socklen_t(text.count)) != nil { answers.append(String(cString: text)) }
            case AF_INET6:
                var v6 = raw.withMemoryRebound(to: sockaddr_in6.self, capacity: 1) { $0.pointee }
                if inet_ntop(AF_INET6, &v6.sin6_addr, &text, socklen_t(text.count)) != nil { answers.append(String(cString: text)) }
            default:
                continue
            }
        }
        return answers
    }
}
