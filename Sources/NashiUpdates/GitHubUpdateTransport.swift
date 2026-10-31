import Foundation
import Network

public enum GitHubUpdateTransportError: LocalizedError, Equatable {
    case invalidRepository, invalidToken, unauthorized, forbidden, notFound, rateLimited
    case noRelease, missingAppcast, invalidAssetURL, invalidResponse, tooLarge
    case network, cancelled, bridgeUnavailable, invalidRequest

    public var errorDescription: String? {
        switch self {
        case .invalidRepository: return "Enter a GitHub repository as owner/repository."
        case .invalidToken: return "Enter a valid GitHub access token."
        case .unauthorized: return "GitHub rejected the token. Replace an expired or revoked token."
        case .forbidden: return "GitHub denied access. Check repository permissions and organization approval."
        case .notFound: return "The repository or release asset is unavailable. Check its name and token access."
        case .rateLimited: return "GitHub’s request limit was reached. Try again later."
        case .noRelease: return "This repository has no published stable release yet."
        case .missingAppcast: return "The latest release does not contain an uploaded appcast.xml."
        case .invalidAssetURL: return "The update download is outside the configured GitHub repository."
        case .invalidResponse: return "GitHub returned an invalid update response."
        case .tooLarge: return "The update response exceeded the transport’s safe limits."
        case .network: return "The update connection failed. Check your connection and try again."
        case .cancelled: return "The update connection was cancelled."
        case .bridgeUnavailable: return "The private update connection is unavailable. Try checking again."
        case .invalidRequest: return "The private update request is invalid."
        }
    }
}

public struct GitHubRepository: Equatable, Codable, Sendable {
    public let slug: String
    public var websiteURL: URL { URL(string: "https://github.com/\(slug)")! }
    var owner: String { String(slug.split(separator: "/")[0]) }
    var name: String { String(slug.split(separator: "/")[1]) }

    public init(_ input: String) throws {
        var value = input.trimmingCharacters(in: .whitespacesAndNewlines)
        guard value.utf8.count <= 512 else { throw GitHubUpdateTransportError.invalidRepository }
        if value.contains("://") {
            guard let components = URLComponents(string: value),
                  components.scheme?.lowercased() == "https", components.host?.lowercased() == "github.com",
                  components.port == nil || components.port == 443,
                  components.user == nil, components.password == nil,
                  components.query == nil, components.fragment == nil else { throw GitHubUpdateTransportError.invalidRepository }
            value = components.percentEncodedPath
            guard value.hasPrefix("/") else { throw GitHubUpdateTransportError.invalidRepository }
            value.removeFirst()
        }
        if value.hasSuffix("/") { value.removeLast() }
        if value.hasSuffix(".git") { value.removeLast(4) }
        let parts = value.split(separator: "/", omittingEmptySubsequences: false).map(String.init)
        guard parts.count == 2,
              parts[0].range(of: "^[A-Za-z0-9][A-Za-z0-9-]{0,38}$", options: .regularExpression) != nil,
              parts[1].range(of: "^[A-Za-z0-9_.-]{1,100}$", options: .regularExpression) != nil,
              parts[1] != ".", parts[1] != ".." else { throw GitHubUpdateTransportError.invalidRepository }
        slug = parts.joined(separator: "/").lowercased()
    }

    private enum CodingKeys: String, CodingKey { case slug }
    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        try self.init(container.decode(String.self, forKey: .slug))
    }
    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(slug, forKey: .slug)
    }
}

enum GitHubArchiveReference: Equatable {
    case release(tag: String, filename: String)
    case asset(Int64)
}

public final class GitHubUpdateClient: @unchecked Sendable {
    public let repository: GitHubRepository
    private let token: String?
    private let configuration: URLSessionConfiguration
    static let metadataLimit = 2 * 1024 * 1024

    public convenience init(repository: GitHubRepository, token: String?) {
        self.init(repository: repository, token: token, configuration: .ephemeral)
    }

    // A private session configuration also permits credential-free fixture tests.
    init(repository: GitHubRepository, token: String?, configuration: URLSessionConfiguration) {
        self.repository = repository
        self.token = token?.trimmingCharacters(in: .whitespacesAndNewlines)
        let isolated = configuration.copy() as! URLSessionConfiguration
        isolated.urlCache = nil
        isolated.requestCachePolicy = .reloadIgnoringLocalCacheData
        isolated.httpCookieStorage = nil
        isolated.httpShouldSetCookies = false
        isolated.urlCredentialStorage = nil
        isolated.httpAdditionalHeaders = nil
        isolated.timeoutIntervalForRequest = 30
        isolated.timeoutIntervalForResource = 3600
        self.configuration = isolated
    }

    public func validateAccess() async throws {
        struct RepositoryResponse: Decodable { let id: Int64 }
        let data = try await apiData([], accept: "application/vnd.github+json")
        guard let response = try? JSONDecoder().decode(RepositoryResponse.self, from: data), response.id > 0 else {
            throw GitHubUpdateTransportError.invalidResponse
        }
    }

    public func fetchLatestAppcast() async throws -> Data {
        let data: Data
        do { data = try await apiData(["releases", "latest"], accept: "application/vnd.github+json") }
        catch GitHubUpdateTransportError.notFound { throw GitHubUpdateTransportError.noRelease }
        guard let release = try? JSONDecoder().decode(Release.self, from: data),
              release.draft != true, release.prerelease != true else { throw GitHubUpdateTransportError.invalidResponse }
        let matches = release.assets.filter { $0.name == "appcast.xml" && $0.state == "uploaded" }
        guard matches.count == 1 else { throw GitHubUpdateTransportError.missingAppcast }
        let asset = matches[0]
        try asset.validate()
        guard asset.size <= Int64(Self.metadataLimit) else { throw GitHubUpdateTransportError.tooLarge }
        let result = try await perform(downloadRequest(asset.id), maximumBytes: Self.metadataLimit, allowAssetRedirects: true)
        guard Int64(result.count) == asset.size else { throw GitHubUpdateTransportError.invalidResponse }
        return result
    }

    func archiveReference(for original: URL) throws -> GitHubArchiveReference {
        guard Self.isHTTPS(original), original.query == nil, original.fragment == nil,
              let components = URLComponents(url: original, resolvingAgainstBaseURL: false) else {
            throw GitHubUpdateTransportError.invalidAssetURL
        }
        let raw = components.percentEncodedPath.split(separator: "/", omittingEmptySubsequences: false)
        guard raw.first == "", raw.count == 7 else { throw GitHubUpdateTransportError.invalidAssetURL }
        let parts = try raw.dropFirst().map { value -> String in
            guard let decoded = String(value).removingPercentEncoding,
                  !decoded.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }) else {
                throw GitHubUpdateTransportError.invalidAssetURL
            }
            return decoded
        }
        switch original.host?.lowercased() {
        case "github.com":
            guard UpdateBrandMigration.acceptsArchive(owner:parts[0],name:parts[1],for:repository),
                  parts[2] == "releases", parts[3] == "download",
                  !parts[4].isEmpty, parts[4] != ".", parts[4] != "..", parts[4].utf8.count <= 1024,
                  Asset.validName(parts[5]) else { throw GitHubUpdateTransportError.invalidAssetURL }
            return .release(tag: parts[4], filename: parts[5])
        case "api.github.com":
            guard parts[0] == "repos", UpdateBrandMigration.acceptsArchive(owner:parts[1],name:parts[2],for:repository), parts[3] == "releases", parts[4] == "assets",
                  parts[5].allSatisfy({ $0.isASCII && $0.isNumber }),
                  let identifier = Int64(parts[5]), identifier > 0 else { throw GitHubUpdateTransportError.invalidAssetURL }
            return .asset(identifier)
        default: throw GitHubUpdateTransportError.invalidAssetURL
        }
    }

    private struct Release: Decodable {
        let draft: Bool?
        let prerelease: Bool?
        let assets: [Asset]
    }
    private struct Asset: Decodable {
        let id: Int64
        let name: String
        let size: Int64
        let state: String
        static func validName(_ value: String) -> Bool {
            !value.isEmpty && value != "." && value != ".." && value.utf8.count <= 255 &&
            !value.contains("/") && !value.contains("\\") &&
            !value.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) })
        }
        func validate() throws {
            guard id > 0, size > 0, state == "uploaded", Self.validName(name) else {
                throw GitHubUpdateTransportError.invalidResponse
            }
        }
    }

    private func resolve(_ reference: GitHubArchiveReference) async throws -> Asset {
        let asset: Asset
        switch reference {
        case .asset(let identifier):
            let data = try await apiData(["releases", "assets", String(identifier)], accept: "application/vnd.github+json")
            guard let decoded = try? JSONDecoder().decode(Asset.self, from: data), decoded.id == identifier else {
                throw GitHubUpdateTransportError.invalidResponse
            }
            asset = decoded
        case .release(let tag, let filename):
            let data = try await apiData(["releases", "tags", tag], accept: "application/vnd.github+json")
            guard let release = try? JSONDecoder().decode(Release.self, from: data), release.draft != true else {
                throw GitHubUpdateTransportError.invalidResponse
            }
            let matches = release.assets.filter { $0.name == filename && $0.state == "uploaded" }
            guard matches.count == 1 else { throw GitHubUpdateTransportError.notFound }
            asset = matches[0]
        }
        try asset.validate()
        return asset
    }

    fileprivate func streamArchive(_ reference: GitHubArchiveReference, to sink: GitHubArchiveSink) async throws {
        let asset = try await resolve(reference)
        _ = try await perform(downloadRequest(asset.id), maximumBytes: 0, allowAssetRedirects: true,
                              sink: sink, expectedSize: asset.size, filename: asset.name)
    }

    private func apiData(_ suffix: [String], accept: String) async throws -> Data {
        try await perform(apiRequest(suffix, accept: accept), maximumBytes: Self.metadataLimit, allowAssetRedirects: false)
    }

    func apiRequest(_ suffix: [String], accept: String) throws -> URLRequest {
        let segmentCharacters = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~")
        let segments = ["repos", repository.owner, repository.name] + suffix
        let encoded = try segments.map { segment -> String in
            guard !segment.isEmpty, segment != ".", segment != "..",
                  let value = segment.addingPercentEncoding(withAllowedCharacters: segmentCharacters) else {
                throw GitHubUpdateTransportError.invalidAssetURL
            }
            return value
        }
        let url = URL(string: "https://api.github.com/" + encoded.joined(separator: "/"))!
        var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 30)
        request.setValue(accept, forHTTPHeaderField: "Accept")
        request.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
        request.setValue("Nashi-Private-Updater", forHTTPHeaderField: "User-Agent")
        if let token, !token.isEmpty {
            guard token.utf8.count <= 1024, token.unicodeScalars.allSatisfy({ $0.value >= 33 && $0.value <= 126 }) else {
                throw GitHubUpdateTransportError.invalidToken
            }
            request.setValue("Bearer " + token, forHTTPHeaderField: "Authorization")
        }
        return request
    }

    private func downloadRequest(_ identifier: Int64) throws -> URLRequest {
        try apiRequest(["releases", "assets", String(identifier)], accept: "application/octet-stream")
    }

    static func isHTTPS(_ url: URL) -> Bool {
        url.scheme?.lowercased() == "https" && url.host != nil && url.user == nil && url.password == nil &&
        (url.port == nil || url.port == 443)
    }

    static func assetRedirectRequest(to url: URL) throws -> URLRequest {
        let hosts = ["release-assets.githubusercontent.com", "objects.githubusercontent.com", "github-releases.githubusercontent.com"]
        guard isHTTPS(url), let host = url.host?.lowercased(), hosts.contains(host), url.fragment == nil else {
            throw GitHubUpdateTransportError.invalidAssetURL
        }
        // Build a new request, rather than copying any authorization/cookies across origins.
        var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 30)
        request.setValue("application/octet-stream", forHTTPHeaderField: "Accept")
        request.setValue("Nashi-Private-Updater", forHTTPHeaderField: "User-Agent")
        return request
    }

    private func perform(_ request: URLRequest, maximumBytes: Int, allowAssetRedirects: Bool,
                         sink: GitHubArchiveSink? = nil, expectedSize: Int64? = nil, filename: String = "") async throws -> Data {
        let transfer = GitHubHTTPTransfer(request: request, configuration: configuration,
            maximumBytes: maximumBytes, allowAssetRedirects: allowAssetRedirects,
            sink: sink, expectedSize: expectedSize, filename: filename)
        return try await withTaskCancellationHandler(operation: {
            try await withCheckedThrowingContinuation { continuation in
                transfer.start { continuation.resume(with: $0) }
            }
        }, onCancel: { transfer.cancel() })
    }
}

fileprivate protocol GitHubArchiveSink: AnyObject {
    func beginArchive(length: Int64, filename: String, completion: @escaping (Result<Void, Error>) -> Void)
    func writeArchive(_ data: Data, completion: @escaping (Result<Void, Error>) -> Void)
}

/// Each request has an isolated delegate/session. Only the bounded collector or
/// a bounded streaming queue owns response bytes; archives never become a Data blob.
private final class GitHubHTTPTransfer: NSObject, URLSessionDataDelegate, @unchecked Sendable {
    private let request: URLRequest
    private let configuration: URLSessionConfiguration
    private let maximumBytes: Int
    private let allowAssetRedirects: Bool
    private let sink: GitHubArchiveSink?
    private let expectedSize: Int64?
    private let filename: String
    private let work: OperationQueue
    private var session: URLSession?
    private var task: URLSessionDataTask?
    private var completion: ((Result<Data, Error>) -> Void)?
    private var finished = false
    private var cancelled = false
    private var collected = Data()
    private var received: Int64 = 0
    private var redirects = 0
    private var pending: [Data] = []
    private var pendingBytes = 0
    private var writing = false
    private var suspended = false
    private var upstreamFinished = false
    private let maximumQueuedBytes = 8 * 1024 * 1024

    init(request: URLRequest, configuration: URLSessionConfiguration, maximumBytes: Int,
         allowAssetRedirects: Bool, sink: GitHubArchiveSink?, expectedSize: Int64?, filename: String) {
        self.request = request; self.configuration = configuration; self.maximumBytes = maximumBytes
        self.allowAssetRedirects = allowAssetRedirects; self.sink = sink
        self.expectedSize = expectedSize; self.filename = filename
        work = OperationQueue(); work.maxConcurrentOperationCount = 1
        work.name = "Nashi.GitHubUpdateRequest"
    }

    func start(_ completion: @escaping (Result<Data, Error>) -> Void) {
        work.addOperation {
            self.completion = completion
            guard !self.cancelled else { self.finish(.failure(GitHubUpdateTransportError.cancelled)); return }
            self.session = URLSession(configuration: self.configuration, delegate: self, delegateQueue: self.work)
            self.task = self.session?.dataTask(with: self.request)
            self.task?.resume()
        }
    }
    func cancel() {
        work.addOperation {
            self.cancelled = true
            if self.completion != nil { self.finish(.failure(GitHubUpdateTransportError.cancelled)) }
        }
    }
    private func finish(_ result: Result<Data, Error>) {
        guard !finished else { return }
        finished = true
        let callback = completion; completion = nil
        task?.cancel(); task = nil
        session?.invalidateAndCancel(); session = nil
        pending.removeAll(); pendingBytes = 0
        callback?(result)
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        guard !finished, allowAssetRedirects, redirects < 5,
              let url = request.url, let redirected = try? GitHubUpdateClient.assetRedirectRequest(to: url) else {
            completionHandler(nil)
            finish(.failure(GitHubUpdateTransportError.invalidAssetURL)); return
        }
        redirects += 1
        completionHandler(redirected)
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse,
                    completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        guard !finished, let response = response as? HTTPURLResponse else {
            completionHandler(.cancel); finish(.failure(GitHubUpdateTransportError.invalidResponse)); return
        }
        let error: GitHubUpdateTransportError?
        switch response.statusCode {
        case 200: error = nil
        case 401: error = .unauthorized
        // A secondary rate limit is a 403 with Retry-After while requests remain.
        case 403: error = response.value(forHTTPHeaderField: "X-RateLimit-Remaining") == "0" ||
            response.value(forHTTPHeaderField: "Retry-After") != nil ? .rateLimited : .forbidden
        case 404: error = .notFound
        case 429: error = .rateLimited
        default: error = .invalidResponse
        }
        if let error { completionHandler(.cancel); finish(.failure(error)); return }
        if let sink, let expectedSize {
            sink.beginArchive(length: expectedSize, filename: filename) { result in
                self.work.addOperation {
                    guard !self.finished else { completionHandler(.cancel); return }
                    switch result {
                    case .success: completionHandler(.allow)
                    case .failure: completionHandler(.cancel); self.finish(.failure(GitHubUpdateTransportError.network))
                    }
                }
            }
        } else {
            guard response.expectedContentLength <= Int64(maximumBytes) else {
                completionHandler(.cancel); finish(.failure(GitHubUpdateTransportError.tooLarge)); return
            }
            completionHandler(.allow)
        }
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        guard !finished else { return }
        if let expectedSize {
            guard Int64(data.count) <= expectedSize - received else { finish(.failure(GitHubUpdateTransportError.invalidResponse)); return }
            received += Int64(data.count)
        }
        guard sink != nil else {
            guard data.count <= maximumBytes - collected.count else { finish(.failure(GitHubUpdateTransportError.tooLarge)); return }
            collected.append(data); return
        }
        guard data.count <= maximumQueuedBytes - pendingBytes else { finish(.failure(GitHubUpdateTransportError.tooLarge)); return }
        pending.append(data); pendingBytes += data.count
        if !suspended { dataTask.suspend(); suspended = true }
        drain()
    }

    private func drain() {
        guard !finished, !writing else { return }
        guard !pending.isEmpty else {
            if upstreamFinished {
                if expectedSize != received { finish(.failure(GitHubUpdateTransportError.invalidResponse)) }
                else { finish(.success(Data())) }
            } else if suspended { suspended = false; task?.resume() }
            return
        }
        writing = true
        let data = pending.removeFirst()
        sink?.writeArchive(data) { result in
            self.work.addOperation {
                guard !self.finished else { return }
                self.pendingBytes -= data.count
                self.writing = false
                switch result {
                case .success: self.drain()
                case .failure: self.finish(.failure(GitHubUpdateTransportError.network))
                }
            }
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        guard !finished else { return }
        if error != nil { finish(.failure(cancelled ? GitHubUpdateTransportError.cancelled : .network)); return }
        if sink == nil { finish(.success(collected)) }
        else { upstreamFinished = true; drain() }
    }
}

public final class GitHubUpdateBridge: @unchecked Sendable {
    private let client: GitHubUpdateClient
    private let appcast: Data
    private let nonce = UUID().uuidString + UUID().uuidString
    private let queue = DispatchQueue(label: "Nashi.PrivateUpdateBridge")
    private let lock = NSLock()
    private var listener: NWListener?
    private var feedURL: URL?
    private var waiters: [CheckedContinuation<URL, Error>] = []
    private var stopped = false
    private var routes: [String: GitHubArchiveReference] = [:]
    private var originalRoutes: [URL: URL] = [:]
    private var connections: [UUID: GitHubBridgeConnection] = [:]

    public init(client: GitHubUpdateClient, appcast: Data) { self.client = client; self.appcast = appcast }
    deinit { stop() }

    public func start() async throws -> URL {
        try await withCheckedThrowingContinuation { continuation in
            lock.lock()
            if stopped { lock.unlock(); continuation.resume(throwing: GitHubUpdateTransportError.bridgeUnavailable); return }
            if let feedURL { lock.unlock(); continuation.resume(returning: feedURL); return }
            guard !appcast.isEmpty, appcast.count <= GitHubUpdateClient.metadataLimit else {
                lock.unlock(); continuation.resume(throwing: GitHubUpdateTransportError.tooLarge); return
            }
            waiters.append(continuation)
            if listener != nil { lock.unlock(); return }
            do {
                let parameters = NWParameters.tcp
                parameters.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: .any)
                parameters.allowLocalEndpointReuse = false
                let listener = try NWListener(using: parameters, on: .any)
                self.listener = listener
                listener.stateUpdateHandler = { [weak self, weak listener] state in
                    guard let self else { return }
                    switch state {
                    case .ready:
                        guard let port = listener?.port else { self.stop(); return }
                        self.lock.lock()
                        guard !self.stopped else { self.lock.unlock(); return }
                        let url = URL(string: "http://127.0.0.1:\(port.rawValue)/\(self.nonce)/appcast.xml")!
                        self.feedURL = url
                        let waiters = self.waiters; self.waiters.removeAll()
                        self.lock.unlock()
                        waiters.forEach { $0.resume(returning: url) }
                    case .failed, .cancelled, .waiting: self.stop()
                    default: break
                    }
                }
                listener.newConnectionHandler = { [weak self] in self?.accept($0) }
                lock.unlock()
                listener.start(queue: queue)
            } catch {
                let waiters = self.waiters; self.waiters.removeAll(); stopped = true
                lock.unlock()
                waiters.forEach { $0.resume(throwing: GitHubUpdateTransportError.bridgeUnavailable) }
            }
        }
    }

    public func stop() {
        lock.lock()
        guard !stopped else { lock.unlock(); return }
        stopped = true
        let listener = self.listener; self.listener = nil; feedURL = nil
        let waiters = self.waiters; self.waiters.removeAll()
        let active = Array(connections.values); connections.removeAll()
        routes.removeAll(); originalRoutes.removeAll()
        lock.unlock()
        listener?.cancel()
        waiters.forEach { $0.resume(throwing: GitHubUpdateTransportError.bridgeUnavailable) }
        active.forEach { $0.cancel() }
    }

    public func archiveURL(for original: URL) throws -> URL {
        let reference = try client.archiveReference(for: original)
        lock.lock(); defer { lock.unlock() }
        guard !stopped, let feedURL else { throw GitHubUpdateTransportError.bridgeUnavailable }
        if let existing = originalRoutes[original] { return existing }
        guard routes.count < 64 else { throw GitHubUpdateTransportError.tooLarge }
        let path = "/\(nonce)/archive/\(UUID().uuidString)/update.zip"
        let url = URL(string: path, relativeTo: feedURL)!.absoluteURL
        routes[path] = reference; originalRoutes[original] = url
        return url
    }

    private func accept(_ connection: NWConnection) {
        let identifier = UUID()
        lock.lock()
        guard !stopped, let feedURL, connections.count < 16 else { lock.unlock(); connection.cancel(); return }
        let peer = GitHubBridgeConnection(connection: connection, host: "127.0.0.1:\(feedURL.port!)", client: client,
            route: { [weak self] path in
                guard let self else { return nil }
                self.lock.lock(); defer { self.lock.unlock() }
                guard !self.stopped else { return nil }
                if path == "/\(self.nonce)/appcast.xml" { return .feed(self.appcast) }
                return self.routes[path].map(GitHubBridgeRoute.archive)
            }, onClose: { [weak self] in
                guard let self else { return }
                self.lock.lock(); self.connections.removeValue(forKey: identifier); self.lock.unlock()
            })
        connections[identifier] = peer
        lock.unlock()
        peer.start(on: queue)
    }
}

private enum GitHubBridgeRoute { case feed(Data), archive(GitHubArchiveReference) }

private final class GitHubBridgeConnection: GitHubArchiveSink, @unchecked Sendable {
    private let connection: NWConnection
    private let host: String
    private let client: GitHubUpdateClient
    private let route: (String) -> GitHubBridgeRoute?
    private let onClose: () -> Void
    private let lock = NSLock()
    private var closed = false
    private var sentHeaders = false
    private var request = Data()
    private var operation: Task<Void, Never>?
    private var timeout: DispatchWorkItem?

    init(connection: NWConnection, host: String, client: GitHubUpdateClient,
         route: @escaping (String) -> GitHubBridgeRoute?, onClose: @escaping () -> Void) {
        self.connection = connection; self.host = host; self.client = client; self.route = route; self.onClose = onClose
    }

    func start(on queue: DispatchQueue) {
        connection.stateUpdateHandler = { [weak self] state in
            switch state { case .failed, .cancelled: self?.cancel(); default: break }
        }
        let timeout = DispatchWorkItem { [weak self] in self?.cancel() }
        self.timeout = timeout
        queue.asyncAfter(deadline: .now() + 15, execute: timeout)
        connection.start(queue: queue)
        receive()
    }

    func cancel() {
        lock.lock()
        guard !closed else { lock.unlock(); return }
        closed = true
        let operation = self.operation; self.operation = nil
        timeout?.cancel(); timeout = nil
        lock.unlock()
        operation?.cancel(); connection.cancel(); onClose()
    }

    private func receive() {
        connection.receive(minimumIncompleteLength: 1, maximumLength: max(1, 8192 - request.count)) { [weak self] data, _, complete, error in
            guard let self else { return }
            if let data { self.request.append(data) }
            if let end = self.request.range(of: Data("\r\n\r\n".utf8)) {
                self.timeout?.cancel(); self.timeout = nil
                self.handle(Data(self.request[..<end.lowerBound]))
            } else if self.request.count >= 8192 { self.reply(status: "431 Request Header Fields Too Large", body: Data()) }
            else if complete || error != nil { self.cancel() }
            else { self.receive() }
        }
    }

    private func handle(_ data: Data) {
        guard data.allSatisfy({ $0 == 9 || $0 == 10 || $0 == 13 || (32...126).contains($0) }),
              let string = String(data: data, encoding: .utf8) else { reply(status: "400 Bad Request", body: Data()); return }
        let lines = string.components(separatedBy: "\r\n")
        let words = lines[0].split(separator: " ", omittingEmptySubsequences: false)
        guard words.count == 3, words[2] == "HTTP/1.1" || words[2] == "HTTP/1.0" else {
            reply(status: "400 Bad Request", body: Data()); return
        }
        guard words[0] == "GET" else { reply(status: "405 Method Not Allowed", body: Data()); return }
        let target = String(words[1])
        guard target.hasPrefix("/"), !target.hasPrefix("//"), !target.contains("#") else { reply(status: "400 Bad Request", body: Data()); return }
        var hosts: [String] = []
        for line in lines.dropFirst() {
            guard let separator = line.firstIndex(of: ":"), separator != line.startIndex else { reply(status: "400 Bad Request", body: Data()); return }
            let name = line[..<separator].lowercased()
            let value = line[line.index(after: separator)...].trimmingCharacters(in: .whitespaces)
            if name == "host" { hosts.append(value) }
            if name == "transfer-encoding" || (name == "content-length" && value != "0") { reply(status: "400 Bad Request", body: Data()); return }
        }
        guard hosts == [host] else { reply(status: "400 Bad Request", body: Data()); return }
        let path = String(target.split(separator: "?", maxSplits: 1, omittingEmptySubsequences: false)[0])
        guard let route = route(path) else { reply(status: "404 Not Found", body: Data()); return }
        switch route {
        case .feed(let body): reply(status: "200 OK", body: body, contentType: "application/rss+xml")
        case .archive(let reference):
            lock.lock()
            guard !closed else { lock.unlock(); return }
            operation = Task { [weak self] in
                guard let self else { return }
                do {
                    try await self.client.streamArchive(reference, to: self)
                    self.endResponse()
                } catch {
                    if self.hasSentHeaders() { self.cancel() }
                    else { self.reply(status: "502 Bad Gateway", body: Data("The private update download failed. Check your update settings and retry.".utf8)) }
                }
            }
            lock.unlock()
        }
    }

    private func reply(status: String, body: Data, contentType: String = "text/plain") {
        let header = "HTTP/1.1 \(status)\r\nContent-Type: \(contentType)\r\nContent-Length: \(body.count)\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n"
        var response = Data(header.utf8); response.append(body)
        send(response) { [weak self] _ in self?.endResponse() }
    }

    private func hasSentHeaders() -> Bool {
        lock.lock(); defer { lock.unlock() }; return sentHeaders
    }

    func beginArchive(length: Int64, filename: String, completion: @escaping (Result<Void, Error>) -> Void) {
        let safeName = String(filename.unicodeScalars.map { scalar -> Character in
            if scalar.isASCII && (CharacterSet.alphanumerics.contains(scalar) || " ._-".unicodeScalars.contains(scalar)) { return Character(String(scalar)) }
            return "_"
        })
        lock.lock(); sentHeaders = true; lock.unlock()
        let header = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: \(length)\r\nContent-Disposition: attachment; filename=\"\(safeName)\"\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n"
        send(Data(header.utf8), completion: completion)
    }
    func writeArchive(_ data: Data, completion: @escaping (Result<Void, Error>) -> Void) { send(data, completion: completion) }
    private func send(_ data: Data, completion: @escaping (Result<Void, Error>) -> Void) {
        lock.lock(); let closed = self.closed; lock.unlock()
        guard !closed else { completion(.failure(GitHubUpdateTransportError.cancelled)); return }
        connection.send(content: data, completion: .contentProcessed { error in
            completion(error == nil ? .success(()) : .failure(GitHubUpdateTransportError.network))
        })
    }
    private func endResponse() {
        connection.send(content: nil, contentContext: .finalMessage, isComplete: true, completion: .contentProcessed { [weak self] _ in self?.cancel() })
    }
}
