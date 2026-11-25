// Adapted from the Tendedero personal fork credential client; see Resources/licenses/Tendedero-MIT.txt.
import Foundation
import CryptoKit
import Security

enum UpdateCredentialBrokerError: Error, LocalizedError, Equatable {
    case unavailable, identity, connection, denied, timeout
    var errorDescription: String? {
        switch self {
        case .identity: return "The update connection could not be verified. Reinstall a verified Nashi build to repair it."
        case .denied: return "The saved update token is unavailable. Open Update Connection to authorize access."
        case .connection, .timeout, .unavailable: return "The saved update connection did not respond. Try again."
        }
    }
}

struct UpdateCredentialBrokerConfiguration: Equatable, Sendable {
    static let service = "local.tangerine.replica.credentials.v1"
    let appURL: URL
    let certificateSHA1: String
    let binarySHA256: String
    let bundleSHA256: String
    var requirement: String { "identifier \"\(Self.service)\" and certificate leaf = H\"\(certificateSHA1)\"" }
    var bundleURL: URL { appURL.appendingPathComponent("Contents/XPCServices/\(Self.service).xpc", isDirectory: true) }

    init(appURL: URL, info: [String: Any]) throws {
        func hash(_ key: String, count: Int) throws -> String {
            guard let value = info[key] as? String, value.count == count,
                  value.utf8.allSatisfy({ (48...57).contains($0) || (65...70).contains($0) || (97...102).contains($0) }) else { throw UpdateCredentialBrokerError.identity }
            return value.lowercased()
        }
        guard info["NashiCredentialBrokerService"] as? String == Self.service else { throw UpdateCredentialBrokerError.identity }
        self.appURL = appURL.standardizedFileURL
        certificateSHA1 = try hash("NashiCredentialBrokerCertificateSHA1", count: 40)
        binarySHA256 = try hash("NashiCredentialBrokerBinarySHA256", count: 64)
        bundleSHA256 = try hash("NashiCredentialBrokerBundleSHA256", count: 64)
    }
}

/// Narrow transport surface lets unit tests prove ordering and failure handling
/// without starting an XPC service or reading any real credential.
protocol UpdateCredentialBrokerTransport: AnyObject, Sendable {
    var onFailure: (@Sendable () -> Void)? { get set }
    func setRequirement(_ requirement: String)
    func resume()
    func handshake(_ reply: @escaping @Sendable (Bool) -> Void)
    func read(repository: String, allowInteraction: Bool, reply: @escaping @Sendable (String?, Bool) -> Void)
    func write(_ token: String, repository: String, reply: @escaping @Sendable (Bool) -> Void)
    func remove(repository: String, _ reply: @escaping @Sendable (Bool) -> Void)
    func invalidate()
}

private final class NativeUpdateCredentialBrokerTransport: UpdateCredentialBrokerTransport, @unchecked Sendable {
    private let connection: NSXPCConnection
    private let lock = NSLock()
    private var failure: (@Sendable () -> Void)?
    var onFailure: (@Sendable () -> Void)? {
        get { lock.lock(); defer { lock.unlock() }; return failure }
        set { lock.lock(); failure = newValue; lock.unlock() }
    }
    init(service: String) {
        connection = NSXPCConnection(serviceName: service)
        connection.remoteObjectInterface = NSXPCInterface(with: UpdateCredentialBrokerProtocol.self)
        connection.interruptionHandler = { [weak self] in self?.onFailure?() }
        connection.invalidationHandler = { [weak self] in self?.onFailure?() }
    }
    private var proxy: UpdateCredentialBrokerProtocol? {
        connection.remoteObjectProxyWithErrorHandler { [weak self] _ in self?.onFailure?() } as? UpdateCredentialBrokerProtocol
    }
    func setRequirement(_ requirement: String) { connection.setCodeSigningRequirement(requirement) }
    func resume() { connection.resume() }
    func handshake(_ reply: @escaping @Sendable (Bool) -> Void) {
        guard let proxy else { onFailure?(); return }
        proxy.handshake { reply($0 == nil) }
    }
    func read(repository: String, allowInteraction: Bool, reply: @escaping @Sendable (String?, Bool) -> Void) {
        guard let proxy else { onFailure?(); return }
        proxy.readToken(repository: repository, allowInteraction: allowInteraction) { reply($0, $1 == nil) }
    }
    func write(_ token: String, repository: String, reply: @escaping @Sendable (Bool) -> Void) {
        guard let proxy else { onFailure?(); return }
        proxy.writeToken(token, repository: repository) { reply($0 == nil) }
    }
    func remove(repository: String, _ reply: @escaping @Sendable (Bool) -> Void) {
        guard let proxy else { onFailure?(); return }
        proxy.deleteToken(repository: repository) { reply($0 == nil) }
    }
    func invalidate() { onFailure = nil; connection.invalidate() }
}

private final class UpdateCredentialBrokerReply<Value: Sendable>: @unchecked Sendable {
    private let lock = NSLock()
    private let ready = DispatchSemaphore(value: 0)
    private var result: Result<Value, Error>?
    private var closed = false
    func finish(_ value: Result<Value, Error>) {
        lock.lock()
        guard !closed, result == nil else { lock.unlock(); return }
        result = value; lock.unlock(); ready.signal()
    }
    func wait(until deadline: DispatchTime) throws -> Value {
        let arrived = ready.wait(timeout: deadline) == .success
        lock.lock(); closed = true; let received = result; result = nil; lock.unlock()
        guard arrived, let received else { throw UpdateCredentialBrokerError.timeout }
        return try received.get()
    }
}

/// Only the frozen broker touches Keychain. The main app checks the complete
/// bundled helper and its code signature, then pins the running peer and waits
/// for a nonsecret handshake before sending any credential-bearing message.
final class UpdateCredentialBrokerClient: @unchecked Sendable {
    static let live = UpdateCredentialBrokerClient()
    private let configuration: @Sendable () throws -> UpdateCredentialBrokerConfiguration
    private let validate: @Sendable (UpdateCredentialBrokerConfiguration) throws -> Void
    private let connect: @Sendable (String) -> UpdateCredentialBrokerTransport
    private let timeout: TimeInterval
    private let authorizationTimeout: TimeInterval

    init(timeout: TimeInterval = 12, authorizationTimeout: TimeInterval = 120,
         configuration: @escaping @Sendable () throws -> UpdateCredentialBrokerConfiguration = {
             try UpdateCredentialBrokerConfiguration(appURL: Bundle.main.bundleURL, info: Bundle.main.infoDictionary ?? [:])
         }, validate: @escaping @Sendable (UpdateCredentialBrokerConfiguration) throws -> Void = { try UpdateCredentialBrokerClient.validateBundle($0) },
         connect: @escaping @Sendable (String) -> UpdateCredentialBrokerTransport = { NativeUpdateCredentialBrokerTransport(service: $0) }) {
        self.timeout = timeout.isFinite ? max(0.01, min(30, timeout)) : 12
        self.authorizationTimeout = authorizationTimeout.isFinite ? max(0.01, min(180, authorizationTimeout)) : 120
        self.configuration = configuration; self.validate = validate; self.connect = connect
    }
    func readToken(repository: GitHubRepository, allowInteraction: Bool = false) throws -> String? {
        try exchange(timeout: allowInteraction ? authorizationTimeout : timeout) { connection, reply in
            connection.read(repository: repository.slug, allowInteraction: allowInteraction) { token, success in
                let valid = token.map { (try? KeychainUpdateCredentialStore.validate($0)) == $0 } ?? true
                reply.finish(success && valid ? .success(token) : .failure(UpdateCredentialBrokerError.denied))
            }
        }
    }
    func writeToken(_ token: String, repository: GitHubRepository) throws {
        try exchange(timeout: timeout) { connection, reply in
            connection.write(token, repository: repository.slug) { reply.finish($0 ? .success(()) : .failure(UpdateCredentialBrokerError.denied)) }
        }
    }
    func deleteToken(repository: GitHubRepository) throws {
        try exchange(timeout: timeout) { connection, reply in
            connection.remove(repository: repository.slug) { reply.finish($0 ? .success(()) : .failure(UpdateCredentialBrokerError.denied)) }
        }
    }
    private func exchange<Value: Sendable>(timeout: TimeInterval,
        send: @escaping (UpdateCredentialBrokerTransport, UpdateCredentialBrokerReply<Value>) -> Void) throws -> Value {
        guard !Thread.isMainThread else { throw UpdateCredentialBrokerError.unavailable }
        let config = try configuration()
        try validate(config)
        let connection = connect(UpdateCredentialBrokerConfiguration.service)
        defer { connection.invalidate() }
        let deadline = DispatchTime.now() + timeout
        let handshake = UpdateCredentialBrokerReply<Void>(), response = UpdateCredentialBrokerReply<Value>()
        connection.onFailure = { handshake.finish(.failure(UpdateCredentialBrokerError.connection)); response.finish(.failure(UpdateCredentialBrokerError.connection)) }
        connection.setRequirement(config.requirement)
        connection.resume()
        connection.handshake { handshake.finish($0 ? .success(()) : .failure(UpdateCredentialBrokerError.identity)) }
        try handshake.wait(until: deadline)
        send(connection, response)
        return try response.wait(until: deadline)
    }

    static func validateBundle(_ config: UpdateCredentialBrokerConfiguration) throws {
        let bundle = config.bundleURL
        guard bundle.resolvingSymlinksInPath().path == bundle.path,
              let metadata = NSDictionary(contentsOf: bundle.appendingPathComponent("Contents/Info.plist")) as? [String: Any],
              metadata["CFBundleIdentifier"] as? String == UpdateCredentialBrokerConfiguration.service,
              let executable = metadata["CFBundleExecutable"] as? String, !executable.isEmpty,
              executable == URL(fileURLWithPath: executable).lastPathComponent else { throw UpdateCredentialBrokerError.identity }
        let binary = bundle.appendingPathComponent("Contents/MacOS/\(executable)")
        guard try fileHash(binary) == config.binarySHA256, try bundleHash(bundle) == config.bundleSHA256 else { throw UpdateCredentialBrokerError.identity }
        var code: SecStaticCode?, requirement: SecRequirement?
        guard SecStaticCodeCreateWithPath(bundle as CFURL, SecCSFlags(), &code) == errSecSuccess, let code,
              SecRequirementCreateWithString(config.requirement as CFString, SecCSFlags(), &requirement) == errSecSuccess, let requirement,
              SecStaticCodeCheckValidity(code, SecCSFlags(rawValue: kSecCSStrictValidate | kSecCSCheckAllArchitectures | kSecCSCheckNestedCode), requirement) == errSecSuccess else { throw UpdateCredentialBrokerError.identity }
    }
    private static func fileHash(_ url: URL) throws -> String {
        let values = try url.resourceValues(forKeys: [.isRegularFileKey, .isSymbolicLinkKey, .fileSizeKey])
        guard values.isRegularFile == true, values.isSymbolicLink != true, let size = values.fileSize, (0...67_108_864).contains(size) else { throw UpdateCredentialBrokerError.identity }
        return SHA256.hash(data: try Data(contentsOf: url, options: .mappedIfSafe)).map { String(format: "%02x", $0) }.joined()
    }
    static func bundleHash(_ bundle: URL) throws -> String {
        // An unreadable directory must fail closed instead of being silently left out of the seal.
        var enumerationFailed = false
        guard let files = FileManager.default.enumerator(at: bundle, includingPropertiesForKeys: [.isRegularFileKey, .isDirectoryKey, .isSymbolicLinkKey],
                                                         options: [], errorHandler: { _, _ in enumerationFailed = true; return false }) else { throw UpdateCredentialBrokerError.identity }
        var entries: [(String, URL)] = []
        for case let file as URL in files {
            let values = try file.resourceValues(forKeys: [.isRegularFileKey, .isDirectoryKey, .isSymbolicLinkKey])
            guard values.isSymbolicLink != true else { throw UpdateCredentialBrokerError.identity }
            if values.isDirectory == true { continue }
            guard values.isRegularFile == true, entries.count < 256 else { throw UpdateCredentialBrokerError.identity }
            entries.append((String(file.path.dropFirst(bundle.path.count + 1)), file))
        }
        guard !enumerationFailed, !entries.isEmpty else { throw UpdateCredentialBrokerError.identity }
        var seal = Data()
        for (relative, file) in entries.sorted(by: { $0.0 < $1.0 }) {
            guard !relative.contains("\0"), !relative.contains("\n"), !relative.contains("\\") else { throw UpdateCredentialBrokerError.identity }
            let attributes = try FileManager.default.attributesOfItem(atPath: file.path)
            guard let mode = attributes[.posixPermissions] as? NSNumber else { throw UpdateCredentialBrokerError.identity }
            seal.append(Data("\(relative)\0\(String(format: "%04o", mode.intValue & 0o7777))\0\(try fileHash(file))\n".utf8))
        }
        return SHA256.hash(data: seal).map { String(format: "%02x", $0) }.joined()
    }
}

/// The frozen, authenticated helper is the only production Keychain reader.
/// No fallback reads the token in the changing main application's identity.
public final class BrokerUpdateCredentialStore: UpdateCredentialStore, @unchecked Sendable {
    private let client: UpdateCredentialBrokerClient
    public init() { client = .live }
    init(client: UpdateCredentialBrokerClient) { self.client = client }

    public func token(for repository: GitHubRepository) throws -> String {
        try read(repository, allowingInteraction: false)
    }
    public func authorizeToken(for repository: GitHubRepository) throws -> String {
        try read(repository, allowingInteraction: true)
    }
    private func read(_ repository: GitHubRepository, allowingInteraction: Bool) throws -> String {
        do {
            guard let token = try client.readToken(repository: UpdateBrandMigration.credentialAccount(repository), allowInteraction: allowingInteraction) else { throw UpdateCredentialError.missing }
            return try KeychainUpdateCredentialStore.validate(token)
        } catch UpdateCredentialBrokerError.denied { throw UpdateCredentialError.locked }
    }
    public func save(_ token: String, for repository: GitHubRepository) throws {
        do { try client.writeToken(KeychainUpdateCredentialStore.validate(token), repository: UpdateBrandMigration.credentialAccount(repository)) }
        catch UpdateCredentialBrokerError.denied { throw UpdateCredentialError.locked }
    }
    public func remove(for repository: GitHubRepository) throws {
        do { try client.deleteToken(repository: UpdateBrandMigration.credentialAccount(repository)) }
        catch UpdateCredentialBrokerError.denied { throw UpdateCredentialError.locked }
    }
}
