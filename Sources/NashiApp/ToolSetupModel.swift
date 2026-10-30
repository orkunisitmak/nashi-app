import Foundation
import Combine
import NashiCore

@MainActor final class ToolSetupModel: ObservableObject {
    typealias Install = (ToolGroup, CancellationToken, @escaping @Sendable (Double?, String) -> Void) async throws -> Void
    @Published private(set) var status: ToolSetupStatus
    @Published private(set) var working = false
    @Published private(set) var fraction: Double?
    @Published private(set) var message: String
    private let probe: () -> ToolSetupStatus
    private let installer: Install
    private var task: Task<Void, Never>?
    private var token = CancellationToken()
    var onWorkingChange: (Bool) -> Void = { _ in }
    init(probe: @escaping () -> ToolSetupStatus = { ExternalToolSetup.status }, install: @escaping Install = { group, token, report in
        if group == .media { try await ExternalToolSetup.installMedia(token: token, progress: report) }
        else { try await ExternalToolSetup.installOffice(token: token, progress: report) }
    }) {
        self.probe = probe; self.installer = install; let initial = probe(); status = initial
        message = initial.ready ? "Ready. You can go back to your files." : "Download the missing tools to enable all conversions, previews and web downloads. Tools already found on your computer are kept."
    }
    func refresh() {
        guard !working else { return }
        let previous = status; status = probe()
        if status.ready { message = "Ready. You can go back to your files." }
        else if previous.ready || status != previous { message = "Download the missing tools to enable all conversions, previews and web downloads. Tools already found on your computer are kept." }
    }
    func install(_ group: ToolGroup? = nil) {
        guard !working else { return }
        refresh()
        let groups = status.missing.filter { group == nil || $0 == group }
        guard !groups.isEmpty else { return }
        working = true; fraction = nil; message = "Starting download…"; token = CancellationToken(); onWorkingChange(true)
        let currentToken = token
        task = Task {
            defer { status = probe(); working = false; onWorkingChange(false); task = nil; NotificationCenter.default.post(name:.toolSetupChanged,object:nil) }
            do {
                for (index, item) in groups.enumerated() {
                    try currentToken.check(); try Task.checkCancellation()
                    let report: @Sendable (Double?, String) -> Void = { [weak self] value, label in
                        Task { @MainActor in
                            guard let self, self.working, !currentToken.isCancelled else { return }
                            self.fraction = value.map { (Double(index) + $0) / Double(groups.count) }; self.message = label
                        }
                    }
                    try await installer(item, currentToken, report)
                    status = probe()
                }
                guard probe().missing.allSatisfy({ !groups.contains($0) }) else {
                    throw NashiError.failed("The downloaded tools are incomplete. Please try setup again.")
                }
                message = probe().ready ? "Ready. You can go back to your files." : "One tool group is ready. Download the remaining tools to finish setup."
                fraction = 1
            } catch {
                message = currentToken.isCancelled || error is CancellationError ? "Setup cancelled. Existing tools were kept." : error.localizedDescription
            }
        }
    }
    func cancel() { token.cancel(); task?.cancel() }
}
