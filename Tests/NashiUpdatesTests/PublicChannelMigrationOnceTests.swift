import XCTest
@testable import NashiUpdates

final class PublicChannelMigrationOnceTests: XCTestCase {
    private let info: [String: Any] = ["NashiPublicUpdateRepository": "orkunisitmak/nashi-app",
                                       "SUFeedURL": "https://github.com/orkunisitmak/nashi-app/releases/latest/download/appcast.xml"]
    private final class MemoryCredentials: UpdateCredentialStore, @unchecked Sendable {
        private let lock = NSLock()
        private var values: [String: String] = [:]
        var slugs: [String] { lock.lock(); defer { lock.unlock() }; return values.keys.sorted() }
        func token(for repository: GitHubRepository) throws -> String {
            lock.lock(); defer { lock.unlock() }
            guard let value = values[repository.slug] else { throw UpdateCredentialError.missing }
            return value
        }
        func save(_ token: String, for repository: GitHubRepository) throws { lock.lock(); values[repository.slug] = token; lock.unlock() }
        func remove(for repository: GitHubRepository) throws { lock.lock(); values.removeValue(forKey: repository.slug); lock.unlock() }
    }

    private func launch(_ store: MemoryCredentials, _ defaults: UserDefaults) async -> AppUpdater {
        await MainActor.run { AppUpdater(credentials: store, defaults: defaults, validateAccess: { _, _ in }, info: info) }
    }

    func testConnectionSavedAfterTheMigrationSurvivesRelaunch() async throws {
        let suite = "NashiPublicChannelMigrationOnceTests.\(UUID())", defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = MemoryCredentials()
        let first = await launch(store, defaults)
        try await first.connectGitHub(repository: "orkunisitmak/nashi", token: "github_pat_TEST_ONLY_NOT_A_CREDENTIAL", requiresToken: true)
        for _ in 0..<2 {
            let relaunched = await launch(store, defaults)
            await MainActor.run {
                XCTAssertEqual(relaunched.repository?.slug, "orkunisitmak/nashi", "A connection saved later must not be migrated away")
                XCTAssertTrue(relaunched.hasSavedToken)
            }
        }
        XCTAssertEqual(store.slugs, ["orkunisitmak/nashi"])
    }

    func testLegacyChannelIsStillMigratedOnce() async throws {
        let suite = "NashiPublicChannelMigrationOnceTests.\(UUID())", defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        defaults.set("orkunisitmak/tangerine", forKey: "TangerineUpdateGitHubRepository")
        defaults.set(true, forKey: "TangerineUpdateGitHubRequiresToken")
        let store = MemoryCredentials()
        let migrated = await launch(store, defaults)
        await MainActor.run { XCTAssertNil(migrated.repository) }
        XCTAssertEqual(defaults.string(forKey: "NashiPreviousUpdateRepository"), "orkunisitmak/tangerine")
        try await migrated.connectGitHub(repository: "orkunisitmak/tangerine", token: "github_pat_TEST_ONLY_NOT_A_CREDENTIAL", requiresToken: true)
        let relaunched = await launch(store, defaults)
        await MainActor.run { XCTAssertEqual(relaunched.repository?.slug, "orkunisitmak/nashi") }
    }
}
