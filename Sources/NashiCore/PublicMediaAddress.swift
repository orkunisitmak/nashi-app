import Darwin
import Foundation

enum PublicMediaAddress {
    typealias Check = @Sendable (URL) async throws -> Void

    static func requirePublic(_ url: URL) async throws {
        guard let host = url.host else { throw invalidAddress }
        let name = host.trimmingCharacters(in: CharacterSet(charactersIn: "[]"))
        let canonical = name.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "."))
        guard canonical.contains(".") || canonical.contains(":"),
              ![".localhost", ".local", ".home.arpa"].contains(where: canonical.hasSuffix) else {
            throw invalidAddress
        }
        let publicOnly = await Task.detached(priority: .utility) { resolvesPublicly(name) }.value
        try Task.checkCancellation()
        guard publicOnly else { throw invalidAddress }
    }

    static var invalidAddress: NashiError { .invalidInput("Paste one complete http:// or https:// media link.") }

    private static func resolvesPublicly(_ host: String) -> Bool {
        var hints = addrinfo()
        hints.ai_family = AF_UNSPEC
        hints.ai_socktype = SOCK_STREAM
        var result: UnsafeMutablePointer<addrinfo>?
        guard getaddrinfo(host, nil, &hints, &result) == 0, let first = result else { return false }
        defer { freeaddrinfo(first) }
        var current: UnsafeMutablePointer<addrinfo>? = first
        while let item = current {
            guard let address = item.pointee.ai_addr else { return false }
            switch item.pointee.ai_family {
            case AF_INET:
                let ipv4 = UnsafeRawPointer(address).assumingMemoryBound(to: sockaddr_in.self).pointee.sin_addr
                guard withUnsafeBytes(of: ipv4, isPublicIPv4) else { return false }
            case AF_INET6:
                let ipv6 = UnsafeRawPointer(address).assumingMemoryBound(to: sockaddr_in6.self).pointee
                guard ipv6.sin6_scope_id == 0, withUnsafeBytes(of: ipv6.sin6_addr, isPublicIPv6) else { return false }
            default:
                return false
            }
            current = item.pointee.ai_next
        }
        return true
    }

    private static func isPublicIPv4(_ bytes: UnsafeRawBufferPointer) -> Bool {
        let first = bytes[0], second = bytes[1], third = bytes[2]
        return !(first == 0 || first == 10 || first == 127 || first >= 224
            || first == 100 && (64...127).contains(second)
            || first == 169 && second == 254
            || first == 172 && (16...31).contains(second)
            || first == 192 && (second == 168 || second == 0 && [0, 2].contains(third) || second == 88 && third == 99)
            || first == 198 && ([18, 19].contains(second) || second == 51 && third == 100)
            || first == 203 && second == 0 && third == 113)
    }

    private static func isPublicIPv6(_ bytes: UnsafeRawBufferPointer) -> Bool {
        if bytes.prefix(10).allSatisfy({ $0 == 0 }), bytes[10] == 0xff, bytes[11] == 0xff {
            return isPublicIPv4(UnsafeRawBufferPointer(rebasing: bytes[12..<16]))
        }
        return bytes[0] & 0xe0 == 0x20
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && (bytes[2] < 2 || bytes[2] == 0x0d && bytes[3] == 0xb8))
            && !(bytes[0] == 0x20 && bytes[1] == 0x02)
            && !(bytes[0] == 0x3f && bytes[1] == 0xff && bytes[2] & 0xf0 == 0)
    }
}

class PublicMediaRedirects: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
    let checkAddress: PublicMediaAddress.Check
    private let lock = NSLock()
    private var blocked = false

    init(checkAddress: @escaping PublicMediaAddress.Check) {
        self.checkAddress = checkAddress
    }

    func requireAllowedRedirects() throws {
        lock.lock()
        defer { lock.unlock() }
        if blocked { throw PublicMediaAddress.invalidAddress }
    }

    private func blockRedirect() {
        lock.lock()
        blocked = true
        lock.unlock()
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        Task {
            do {
                guard let url = request.url, response.url?.scheme != "https" || url.scheme == "https" else {
                    throw PublicMediaAddress.invalidAddress
                }
                let checked = try WebDownloadEngine.link(url.absoluteString)
                try await checkAddress(checked)
                completionHandler(request)
            } catch {
                blockRedirect()
                completionHandler(nil)
            }
        }
    }
}
