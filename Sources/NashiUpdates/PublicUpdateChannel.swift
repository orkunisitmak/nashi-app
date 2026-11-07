import Foundation

/// The official anonymous channel is fixed in the signed bundle. A remote feed
/// cannot select a repository or ask this app to load a saved GitHub credential.
public struct PublicUpdateChannel:Equatable {
    public let repository:GitHubRepository
    public init?(info:[String:Any]) {
        guard let slug=info["NashiPublicUpdateRepository"] as? String,
              let repository=try? GitHubRepository(slug),
              info["SUFeedURL"] as? String == repository.websiteURL.appendingPathComponent("releases/latest/download/appcast.xml").absoluteString else { return nil }
        self.repository=repository
    }

    /// Migrate the project's two legacy private channels without reading or
    /// deleting their Keychain items. Unrelated custom channels are preserved.
    public func migrate(_ defaults:UserDefaults) {
        let key="TangerineUpdateGitHubRepository",marker="NashiPublicChannelMigration"
        // Run once per official channel, so a connection saved later is kept.
        guard defaults.string(forKey:marker) != repository.slug else { return }
        defaults.set(repository.slug,forKey:marker)
        guard let saved=defaults.string(forKey:key),
              ["orkunisitmak/tangerine","orkunisitmak/nashi",repository.slug].contains(saved.lowercased()) else { return }
        if saved.lowercased() != repository.slug {
            defaults.set(saved,forKey:"NashiPreviousUpdateRepository")
        }
        for name in [key,"TangerineUpdateGitHubRequiresToken","TangerineUpdateGitHubSavedToken"] { defaults.removeObject(forKey:name) }
        if let automatic=defaults.object(forKey:"TangerineUpdateGitHubAutomaticChecks") as? Bool {
            defaults.set(automatic,forKey:"SUEnableAutomaticChecks")
        }
    }
}
