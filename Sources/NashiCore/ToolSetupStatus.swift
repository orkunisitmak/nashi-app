import Foundation

public enum ToolGroup: String, CaseIterable, Sendable {
    case media, office
}

/// Readiness follows the same executable resolution as conversions, including
/// tools already installed on the user's computer.
public struct ToolSetupStatus: Equatable, Sendable {
    public var mediaReady: Bool
    public var officeReady: Bool
    public init(mediaReady: Bool, officeReady: Bool) {
        self.mediaReady = mediaReady; self.officeReady = officeReady
    }
    public init(resolve: (String) -> URL?) {
        mediaReady = ["ffmpeg", "ffprobe", "yt-dlp", "deno"].allSatisfy { resolve($0) != nil }
        officeReady = resolve("soffice") != nil
    }
    public var ready: Bool { mediaReady && officeReady }
    public var missing: [ToolGroup] { ToolGroup.allCases.filter { $0 == .media ? !mediaReady : !officeReady } }
}

/// Replace an incomplete installation only after the new directory is complete.
/// A failed publish restores the original; a retry never asks users to delete it.
enum ToolInstallation {
    static func publish(_ stage: URL, to target: URL, complete: (URL) -> Bool) throws {
        let fm = FileManager.default
        guard complete(stage) else { throw NashiError.failed("The downloaded tools are incomplete. Please try setup again.") }
        if complete(target) { return }
        let backup = target.deletingLastPathComponent().appendingPathComponent(".previous-" + UUID().uuidString)
        // Does not follow links, so a dangling one is moved aside like any stale entry.
        let existing = (try? fm.attributesOfItem(atPath: target.path)) != nil
        if existing { try fm.moveItem(at: target, to: backup) }
        do { try fm.moveItem(at: stage, to: target) }
        catch {
            if existing { try fm.moveItem(at: backup, to: target) }
            throw error
        }
        if existing { try? fm.removeItem(at: backup) }
    }
}
