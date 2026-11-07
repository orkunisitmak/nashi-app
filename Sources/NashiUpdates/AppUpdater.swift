import AppKit
import Combine
import Sparkle

/// Sparkle owns downloading, signature checks, bundle replacement, and relaunch.
/// Our delegate adds a quiet in-app reminder and protects work already in progress.
@MainActor public final class AppUpdater: NSObject, ObservableObject, SPUUpdaterDelegate, @preconcurrency SPUStandardUserDriverDelegate {
    @Published public private(set) var availableVersion: String?
    @Published public private(set) var configurationError: String?
    @Published public private(set) var lastError: String?
    @Published public private(set) var canCheckForUpdates = false
    @Published public private(set) var automaticallyChecksForUpdates = false
    @Published public private(set) var lastCheckedAt: Date?
    @Published public private(set) var sessionInProgress = false
    @Published public private(set) var waitingForCurrentTask = false
    @Published public private(set) var isBusy = false
    @Published public private(set) var repository: GitHubRepository?
    @Published public private(set) var requiresToken = true
    @Published public private(set) var hasSavedToken = false
    @Published public private(set) var isConnecting = false
    @Published public private(set) var isPreparingCheck = false

    private var controller: SPUStandardUpdaterController?
    private var configuration: UpdateConfiguration?
    private var observations: [NSKeyValueObservation] = []
    private var pendingInstall: (() -> Void)?
    private let credentials: UpdateCredentialStore
    private let defaults: UserDefaults
    private let bundleInfo:[String:Any]
    public var officialRepository:GitHubRepository? { PublicUpdateChannel(info:bundleInfo)?.repository }
    private let validateGitHubAccess: (GitHubRepository, String?) async throws -> Void
    private var bridge: GitHubUpdateBridge?
    private var scheduledCheck: Task<Void, Never>?
    private var sparkleCanCheck = false
    private var sparkleSessionInProgress = false
    private enum Preference {
        static let repository = "TangerineUpdateGitHubRepository"
        static let requiresToken = "TangerineUpdateGitHubRequiresToken"
        static let savedToken = "TangerineUpdateGitHubSavedToken"
        static let automaticChecks = "TangerineUpdateGitHubAutomaticChecks"
        static let previousAutomaticChecks = "TangerineUpdatePreviousAutomaticChecks"
    }
    public var isConfigured: Bool { controller != nil && configurationError == nil }
    public var canChangeConnection: Bool {
        !sessionInProgress && controller?.updater.sessionInProgress != true && !isConnecting && !isBusy && !waitingForCurrentTask
    }

    public override convenience init() {
        self.init(credentials: BrokerUpdateCredentialStore(), defaults: .standard)
    }

    public init(credentials: UpdateCredentialStore, defaults: UserDefaults, info:[String:Any]? = nil) {
        self.credentials = credentials
        self.defaults = defaults
        self.bundleInfo = info ?? Bundle.main.infoDictionary ?? [:]
        self.validateGitHubAccess = { repository, token in
            try await GitHubUpdateClient(repository: repository, token: token).validateAccess()
        }
        super.init()
        restoreConnection()
    }

    init(credentials: UpdateCredentialStore, defaults: UserDefaults,
         validateAccess: @escaping (GitHubRepository, String?) async throws -> Void, info:[String:Any]? = nil) {
        self.credentials = credentials
        self.defaults = defaults
        self.bundleInfo = info ?? Bundle.main.infoDictionary ?? [:]
        self.validateGitHubAccess = validateAccess
        super.init()
        restoreConnection()
    }

    private func restoreConnection() {
        PublicUpdateChannel(info:bundleInfo)?.migrate(defaults)
        if let slug = defaults.string(forKey: Preference.repository) {
            if let saved=try? GitHubRepository(slug) {
                let selected=UpdateBrandMigration.canonical(saved)
                repository=selected
                if saved != selected { defaults.set(selected.slug,forKey:Preference.repository) }
            }
        }
        requiresToken = defaults.object(forKey: Preference.requiresToken) as? Bool ?? (officialRepository == nil)
        hasSavedToken = repository != nil && requiresToken && defaults.bool(forKey: Preference.savedToken)
    }

    public func start() {
        guard controller == nil else { return }
        do {
            configuration = try UpdateConfiguration(info: bundleInfo, repository: repository)
            let controller = SPUStandardUpdaterController(startingUpdater: false, updaterDelegate: self, userDriverDelegate: self)
            if repository != nil { controller.updater.automaticallyChecksForUpdates = false }
            try controller.updater.start()
            self.controller = controller
            configurationError = nil
            let updater = controller.updater
            observations = [
                updater.observe(\.canCheckForUpdates, options: [.initial, .new]) { [weak self] observed, change in
                    Task { @MainActor in
                        guard let self, self.controller?.updater === observed else { return }
                        self.sparkleCanCheck = change.newValue ?? false; self.refreshActivity()
                    }
                },
                updater.observe(\.automaticallyChecksForUpdates, options: [.initial, .new]) { [weak self] observed, change in
                    Task { @MainActor in
                        guard let self, self.controller?.updater === observed, self.repository == nil else { return }
                        self.automaticallyChecksForUpdates = change.newValue ?? false
                    }
                },
                updater.observe(\.lastUpdateCheckDate, options: [.initial, .new]) { [weak self] updater, _ in
                    Task { @MainActor in
                        guard let self, self.controller?.updater === updater else { return }
                        self.lastCheckedAt = updater.lastUpdateCheckDate
                    }
                },
                updater.observe(\.sessionInProgress, options: [.initial, .new]) { [weak self] observed, change in
                    Task { @MainActor in
                        guard let self, self.controller?.updater === observed else { return }
                        self.sparkleSessionInProgress = change.newValue ?? false; self.refreshActivity()
                    }
                }
            ]
            configureScheduling()
        } catch {
            configurationError = error.localizedDescription
        }
    }

    public func checkForUpdates() {
        guard let controller, canCheckForUpdates, controller.updater.canCheckForUpdates, !isBusy, !isConnecting else { return }
        lastError = nil
        NSApp.activate(ignoringOtherApps: true)
        if repository != nil, !controller.updater.sessionInProgress {
            Task { await performGitHubCheck(userInitiated: true) }
        } else {
            // An existing gentle reminder already owns a verified feed/bridge.
            // Focus it even offline, so it can be installed or dismissed.
            controller.checkForUpdates(nil)
        }
    }

    public func setAutomaticChecks(_ enabled: Bool) {
        if repository != nil {
            defaults.set(enabled, forKey: Preference.automaticChecks)
            automaticallyChecksForUpdates = enabled
            scheduleGitHubCheck(after: 3_600)
        } else {
            controller?.updater.automaticallyChecksForUpdates = enabled
        }
    }

    public func feedURLString(for updater: SPUUpdater) -> String? {
        // Private feeds arrive byte-for-byte through an app-owned loopback adapter.
        // The GitHub token is never handed to Sparkle or put in a URL.
        if repository != nil { return bridgeFeedURL?.absoluteString }
        return configuration?.feedURL.absoluteString
    }

    private var bridgeFeedURL: URL?

    public func connectGitHub(repository input: String, token inputToken: String, requiresToken: Bool) async throws {
        guard canChangeConnection else { throw ConnectionError.inProgress }
        isConnecting = true
        refreshActivity()
        defer { isConnecting = false; refreshActivity() }
        let selected = UpdateBrandMigration.canonical(try GitHubRepository(input))
        let enteredToken = inputToken.trimmingCharacters(in: .whitespacesAndNewlines)
        let token: String?
        if requiresToken {
            token = enteredToken.isEmpty ? try await credentials.loadToken(for: selected, authorizingAccess: true) : try KeychainUpdateCredentialStore.validate(enteredToken)
        } else {
            token = nil
        }
        // Validate before replacing working credentials or persisting the selection.
        try await validateGitHubAccess(selected, token)
        if let token {
            do { try await credentials.storeToken(token, for: selected) }
            catch {
                // A broker that timed out can still finish the write later. Queue a removal
                // behind it so a repository that was never connected keeps no new token.
                if selected != repository, !enteredToken.isEmpty { try? await credentials.removeToken(for: selected) }
                throw error
            }
        }
        else { try await credentials.removeToken(for: selected) }
        if let previous = repository, previous != selected { try await credentials.removeToken(for: previous) }
        if repository == nil, let controller {
            defaults.set(controller.updater.automaticallyChecksForUpdates, forKey: Preference.previousAutomaticChecks)
        }
        stopBridge()
        defaults.set(selected.slug, forKey: Preference.repository)
        defaults.set(requiresToken, forKey: Preference.requiresToken)
        defaults.set(requiresToken, forKey: Preference.savedToken)
        repository = selected
        self.requiresToken = requiresToken
        hasSavedToken = requiresToken
        availableVersion = nil
        lastError = nil
        if controller == nil { start() } else { configureScheduling() }
    }

    public func disconnectGitHub() async throws {
        guard canChangeConnection else { throw ConnectionError.inProgress }
        isConnecting = true
        refreshActivity()
        defer { isConnecting = false; refreshActivity() }
        if let repository { try await credentials.removeToken(for: repository) }
        scheduledCheck?.cancel()
        stopBridge()
        for key in [Preference.repository, Preference.requiresToken, Preference.savedToken] { defaults.removeObject(forKey: key) }
        repository = nil
        requiresToken = officialRepository == nil
        hasSavedToken = false
        availableVersion = nil
        lastError = nil
        do {
            // Never retain a URL derived from the disconnected private repository.
            configuration = try UpdateConfiguration(info: bundleInfo)
            if let controller {
                controller.updater.automaticallyChecksForUpdates = defaults.object(forKey: Preference.previousAutomaticChecks) as? Bool ?? true
                configureScheduling()
            }
        } catch {
            controller?.updater.automaticallyChecksForUpdates = false
            observations.removeAll()
            controller = nil
            configuration = nil
            configurationError = error.localizedDescription
            sparkleCanCheck = false
            sparkleSessionInProgress = false
            automaticallyChecksForUpdates = false
            refreshActivity()
        }
    }

    private func refreshActivity() {
        sessionInProgress = (controller?.updater.sessionInProgress ?? sparkleSessionInProgress) || isPreparingCheck
        canCheckForUpdates = (controller?.updater.canCheckForUpdates ?? sparkleCanCheck) && !isPreparingCheck && !isConnecting
    }

    private func configureScheduling() {
        scheduledCheck?.cancel()
        guard let controller else { return }
        if repository != nil {
            controller.updater.automaticallyChecksForUpdates = false
            automaticallyChecksForUpdates = defaults.object(forKey: Preference.automaticChecks) as? Bool ?? true
            scheduleGitHubCheck(after: 4)
        } else {
            automaticallyChecksForUpdates = controller.updater.automaticallyChecksForUpdates
        }
    }

    private func scheduleGitHubCheck(after delay: TimeInterval) {
        scheduledCheck?.cancel()
        guard repository != nil, automaticallyChecksForUpdates, isConfigured else { return }
        scheduledCheck = Task { [weak self] in
            do { try await Task.sleep(nanoseconds: UInt64(delay * 1_000_000_000)) } catch { return }
            guard let self, !Task.isCancelled else { return }
            if self.isBusy || self.sessionInProgress || self.isConnecting || !self.canCheckForUpdates {
                self.scheduleGitHubCheck(after: 60)
            } else {
                await self.performGitHubCheck(userInitiated: false)
            }
        }
    }

    private func performGitHubCheck(userInitiated: Bool) async {
        guard let repository, let controller, canCheckForUpdates, controller.updater.canCheckForUpdates, !isBusy, !isConnecting else { return }
        isPreparingCheck = true
        refreshActivity()
        lastError = nil
        defer {
            isPreparingCheck = false
            refreshActivity()
            scheduleGitHubCheck(after: 3_600)
        }
        do {
            let token = requiresToken ? try await credentials.loadToken(for: repository) : nil
            let (client,bytes) = try await UpdateBrandMigration.latestFeed(repository:repository,token:token)
            try Task.checkCancellation()
            let bridge = GitHubUpdateBridge(client: client, appcast: bytes)
            let url = try await bridge.start()
            do { try Task.checkCancellation() }
            catch { bridge.stop(); throw error }
            stopBridge()
            self.bridge = bridge
            bridgeFeedURL = url
            // Sparkle alone verifies the signed feed, archive and replacement bundle.
            controller.updater.httpHeaders = nil
            if userInitiated { controller.checkForUpdates(nil) }
            else { controller.updater.checkForUpdatesInBackground() }
        } catch {
            if !Task.isCancelled {
                lastError = error.localizedDescription
                lastCheckedAt = Date()
            }
        }
    }

    private func stopBridge() {
        bridge?.stop()
        bridge = nil
        bridgeFeedURL = nil
    }

    public func updater(_ updater: SPUUpdater, mayPerform updateCheck: SPUUpdateCheck) throws {
        if repository != nil, bridgeFeedURL == nil { throw ConnectionError.reconnect }
    }

    public func updater(_ updater: SPUUpdater, shouldProceedWithUpdate item: SUAppcastItem, updateCheck: SPUUpdateCheck) throws {
        guard repository != nil else { return }
        guard let bridge, let url = item.fileURL else { throw ConnectionError.reconnect }
        _ = try bridge.archiveURL(for: url)
    }

    public func updater(_ updater: SPUUpdater, willDownloadUpdate item: SUAppcastItem, with request: NSMutableURLRequest) {
        guard repository != nil else { return }
        do {
            guard let bridge, let url = request.url else { throw ConnectionError.reconnect }
            request.url = try bridge.archiveURL(for: url)
            request.allHTTPHeaderFields = ["User-Agent": "Nashi updater"]
        } catch {
            // Fail closed even for Sparkle's resumed/delta download paths.
            request.url = URL(string: "http://127.0.0.1:1/unavailable")!
            request.allHTTPHeaderFields = [:]
            lastError = error.localizedDescription
        }
    }

    public func updater(_ updater: SPUUpdater, shouldDownloadReleaseNotesForUpdate item: SUAppcastItem) -> Bool {
        // Private releases use their signed inline description. Remote HTML never receives credentials.
        repository == nil
    }

    private enum ConnectionError: LocalizedError {
        case inProgress, reconnect
        var errorDescription: String? {
            switch self {
            case .inProgress: return "Finish the current task or update before changing the update connection."
            case .reconnect: return "Check for updates again to reconnect the private download."
            }
        }
    }

    public func setBusy(_ busy: Bool) {
        isBusy = busy
        if !busy, let install = pendingInstall {
            pendingInstall = nil
            waitingForCurrentTask = false
            install()
        }
    }

    public var supportsGentleScheduledUpdateReminders: Bool { true }

    public func standardUserDriverShouldHandleShowingScheduledUpdate(_ update: SUAppcastItem, andInImmediateFocus immediateFocus: Bool) -> Bool {
        // This menu bar app always surfaces scheduled releases without stealing focus.
        false
    }

    public func standardUserDriverWillHandleShowingUpdate(_ handleShowingUpdate: Bool, forUpdate update: SUAppcastItem, state: SPUUserUpdateState) {
        availableVersion = update.displayVersionString
    }

    public func standardUserDriverWillFinishUpdateSession() {
        availableVersion = nil
        pendingInstall = nil
        waitingForCurrentTask = false
        stopBridge()
    }

    public func updater(_ updater: SPUUpdater, didFindValidUpdate item: SUAppcastItem) {
        availableVersion = item.displayVersionString
        lastError = nil
    }

    public func updaterDidNotFindUpdate(_ updater: SPUUpdater) {
        availableVersion = nil
        lastError = nil
    }

    public func updater(_ updater: SPUUpdater, didAbortWithError error: Error) {
        let error = error as NSError
        guard error.domain != SUSparkleErrorDomain || error.code != Int(SUError.noUpdateError.rawValue) else { return }
        pendingInstall = nil
        waitingForCurrentTask = false
        lastError = error.localizedDescription
    }

    public func updater(_ updater: SPUUpdater, shouldPostponeRelaunchForUpdate item: SUAppcastItem, untilInvokingBlock installHandler: @escaping () -> Void) -> Bool {
        postponeRelaunchIfBusy(installHandler)
    }

    func postponeRelaunchIfBusy(_ installHandler: @escaping () -> Void) -> Bool {
        guard isBusy else { return false }
        pendingInstall = installHandler
        waitingForCurrentTask = true
        return true
    }
}
