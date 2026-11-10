import XCTest
@testable import NashiUpdates

private final class UpdateCredentialCalls: @unchecked Sendable {
    private let lock = NSLock()
    private var storage = 0
    var count: Int { lock.lock(); defer { lock.unlock() }; return storage }
    func call() { lock.lock(); storage += 1; lock.unlock() }
}

private final class SyntheticBrokerTransport: UpdateCredentialBrokerTransport, @unchecked Sendable {
    private let lock = NSLock()
    private var log: [String] = []
    var events: [String] { lock.lock(); defer { lock.unlock() }; return log }
    var onFailure: (@Sendable () -> Void)?
    let expectedRepository:String
    let handshakeAccepted: Bool
    let dropsReply: Bool
    let deniesCredential: Bool
    init(handshakeAccepted: Bool = true, dropsReply: Bool = false, deniesCredential: Bool = false,expectedRepository:String = "example/updater-test") {
        self.expectedRepository=expectedRepository
        self.handshakeAccepted = handshakeAccepted; self.dropsReply = dropsReply; self.deniesCredential = deniesCredential
    }
    private func event(_ value: String) { lock.lock(); log.append(value); lock.unlock() }
    func setRequirement(_ requirement: String) { event("requirement:\(requirement)") }
    func resume() { event("resume") }
    func handshake(_ reply: @escaping @Sendable (Bool) -> Void) { event("handshake"); if !dropsReply { reply(handshakeAccepted) } }
    func read(repository: String, allowInteraction: Bool, reply: @escaping @Sendable (String?, Bool) -> Void) { XCTAssertEqual(repository, expectedRepository); event(allowInteraction ? "authorize" : "read"); reply("synthetic-broker-token", !deniesCredential) }
    func write(_ token: String, repository: String, reply: @escaping @Sendable (Bool) -> Void) { XCTAssertEqual(repository, expectedRepository); event("write"); reply(!deniesCredential) }
    func remove(repository: String, _ reply: @escaping @Sendable (Bool) -> Void) { XCTAssertEqual(repository, expectedRepository); event("remove"); reply(!deniesCredential) }
    func invalidate() { event("invalidate") }
}

final class UpdateCredentialBrokerTests: XCTestCase {
    private func brokerInfo() -> [String: Any] {
        ["NashiCredentialBrokerService": UpdateCredentialBrokerConfiguration.service,
         "NashiCredentialBrokerCertificateSHA1": String(repeating: "a", count: 40),
         "NashiCredentialBrokerBinarySHA256": String(repeating: "b", count: 64),
         "NashiCredentialBrokerBundleSHA256": String(repeating: "c", count: 64)]
    }
    private func brokerConfiguration() throws -> UpdateCredentialBrokerConfiguration {
        try UpdateCredentialBrokerConfiguration(appURL: URL(fileURLWithPath: "/synthetic/Nashi.app"), info: brokerInfo())
    }
    func testBrokerConfigurationRequiresExactServiceCertificateAndFrozenHashes() throws {
        let configuration = try brokerConfiguration()
        XCTAssertEqual(configuration.requirement, "identifier \"local.tangerine.replica.credentials.v1\" and certificate leaf = H\"\(String(repeating: "a", count: 40))\"")
        for key in brokerInfo().keys {
            var missing = brokerInfo(); missing.removeValue(forKey: key)
            XCTAssertThrowsError(try UpdateCredentialBrokerConfiguration(appURL: URL(fileURLWithPath: "/synthetic"), info: missing))
            missing[key] = key == "NashiCredentialBrokerService" ? "untrusted.helper" : "bad\" or anchor trusted"
            XCTAssertThrowsError(try UpdateCredentialBrokerConfiguration(appURL: URL(fileURLWithPath: "/synthetic"), info: missing))
        }
    }

    func testNashiStoreUsesTheExistingAccountForReadWriteAndRemove() async throws {
        let config=try brokerConfiguration(),connection=SyntheticBrokerTransport(expectedRepository:"orkunisitmak/tangerine")
        let client=UpdateCredentialBrokerClient(configuration:{config},validate:{_ in},connect:{_ in connection})
        let store=BrokerUpdateCredentialStore(client:client)
        let token=try await store.loadToken(for:UpdateBrandMigration.current)
        XCTAssertEqual(token,"synthetic-broker-token")
        try await store.storeToken("synthetic-new-token",for:UpdateBrandMigration.current)
        try await store.removeToken(for:UpdateBrandMigration.current)
        XCTAssertTrue(connection.events.contains("read"));XCTAssertTrue(connection.events.contains("write"));XCTAssertTrue(connection.events.contains("remove"));XCTAssertFalse(connection.events.contains("authorize"))
    }

    func testBrokerHandshakeAndPinnedRequirementPrecedeEveryCredentialMessage() async throws {
        let config = try brokerConfiguration()
        for operation in ["read", "authorize", "write", "remove"] {
            let connection = SyntheticBrokerTransport(), validation = UpdateCredentialCalls()
            let client = UpdateCredentialBrokerClient(configuration: { config }, validate: { _ in validation.call() }, connect: { _ in
                XCTAssertEqual(validation.count, 1); return connection
            })
            try await Task.detached {
                switch operation {
                case "read": XCTAssertEqual(try client.readToken(repository: try GitHubRepository("example/updater-test")), "synthetic-broker-token")
                case "authorize": XCTAssertEqual(try client.readToken(repository: try GitHubRepository("example/updater-test"), allowInteraction: true), "synthetic-broker-token")
                case "write": try client.writeToken("synthetic-new-token", repository: try GitHubRepository("example/updater-test"))
                default: try client.deleteToken(repository: try GitHubRepository("example/updater-test"))
                }
            }.value
            XCTAssertEqual(connection.events, ["requirement:\(config.requirement)", "resume", "handshake", operation, "invalidate"])
        }
    }

    func testBrokerRejectsUnverifiedBundleAndFailedHandshakeBeforeSendingToken() async throws {
        let config = try brokerConfiguration(), factoryCalls = UpdateCredentialCalls()
        let invalid = UpdateCredentialBrokerClient(configuration: { config }, validate: { _ in throw UpdateCredentialBrokerError.identity },
            connect: { _ in factoryCalls.call(); return SyntheticBrokerTransport() })
        try await Task.detached { XCTAssertThrowsError(try invalid.writeToken("synthetic-must-not-leave-client", repository: try GitHubRepository("example/updater-test"))) }.value
        XCTAssertEqual(factoryCalls.count, 0)
        let connection = SyntheticBrokerTransport(handshakeAccepted: false)
        let rejected = UpdateCredentialBrokerClient(configuration: { config }, validate: { _ in }, connect: { _ in connection })
        try await Task.detached { XCTAssertThrowsError(try rejected.writeToken("synthetic-must-not-leave-client", repository: try GitHubRepository("example/updater-test"))) }.value
        XCTAssertFalse(connection.events.contains("write")); XCTAssertEqual(connection.events.last, "invalidate")
    }

    func testBrokerTimeoutDenialAndMainThreadCallsFailWithoutLeakingToken() async throws {
        let config = try brokerConfiguration()
        let silent = SyntheticBrokerTransport(dropsReply: true)
        let client = UpdateCredentialBrokerClient(timeout: 0.02, configuration: { config }, validate: { _ in }, connect: { _ in silent })
        try await Task.detached {
            XCTAssertThrowsError(try client.readToken(repository: try GitHubRepository("example/updater-test"))) { XCTAssertEqual($0 as? UpdateCredentialBrokerError, .timeout) }
        }.value
        XCTAssertEqual(silent.events.last, "invalidate"); XCTAssertFalse(silent.events.contains("read"))
        let denied = SyntheticBrokerTransport(deniesCredential: true)
        let deniedClient = UpdateCredentialBrokerClient(configuration: { config }, validate: { _ in }, connect: { _ in denied })
        try await Task.detached {
            XCTAssertThrowsError(try deniedClient.readToken(repository: try GitHubRepository("example/updater-test"))) { XCTAssertFalse($0.localizedDescription.contains("synthetic")) }
        }.value
        try await MainActor.run { XCTAssertThrowsError(try deniedClient.readToken(repository: try GitHubRepository("example/updater-test"))) }
    }

    func testFrozenBundleHashIncludesModesAndResourcesAndRejectsSymlinks() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("synthetic-broker-seal-\(UUID())")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let binary = folder.appendingPathComponent("binary"), resource = folder.appendingPathComponent("Info.plist")
        try Data("synthetic binary".utf8).write(to: binary); try Data("synthetic metadata".utf8).write(to: resource)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: binary.path)
        let initial = try UpdateCredentialBrokerClient.bundleHash(folder)
        try FileManager.default.setAttributes([.posixPermissions: 0o644], ofItemAtPath: binary.path)
        XCTAssertNotEqual(try UpdateCredentialBrokerClient.bundleHash(folder), initial)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: binary.path)
        XCTAssertEqual(try UpdateCredentialBrokerClient.bundleHash(folder), initial)
        try Data("altered metadata".utf8).write(to: resource)
        XCTAssertNotEqual(try UpdateCredentialBrokerClient.bundleHash(folder), initial)
        try FileManager.default.createSymbolicLink(at: folder.appendingPathComponent("alias"), withDestinationURL: binary)
        XCTAssertThrowsError(try UpdateCredentialBrokerClient.bundleHash(folder))
    }

    // F702: content inside a directory the seal cannot read must fail closed, not be skipped.
    func testFrozenBundleHashFailsClosedOnUnreadableDirectory() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("synthetic-broker-seal-\(UUID())")
        let hidden = folder.appendingPathComponent("Hidden", isDirectory: true)
        try FileManager.default.createDirectory(at: hidden, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: hidden.path)
            try? FileManager.default.removeItem(at: folder)
        }
        try Data("synthetic binary".utf8).write(to: folder.appendingPathComponent("binary"))
        try Data("unsealed payload".utf8).write(to: hidden.appendingPathComponent("payload"))
        XCTAssertNoThrow(try UpdateCredentialBrokerClient.bundleHash(folder))
        try FileManager.default.setAttributes([.posixPermissions: 0o000], ofItemAtPath: hidden.path)
        XCTAssertThrowsError(try UpdateCredentialBrokerClient.bundleHash(folder)) { XCTAssertEqual($0 as? UpdateCredentialBrokerError, .identity) }
    }

}
