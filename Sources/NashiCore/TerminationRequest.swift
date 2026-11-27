import Foundation

/// A request to quit that cannot be refused: SIGTERM from kill, launchctl or the shutdown command.
/// It stays pending until running work has stopped, and no new work may start in the meantime,
/// so a job that begins just as the previous one ends cannot swallow the request.
public struct TerminationRequest:Sendable {
    public private(set) var pending=false
    public init() {}
    /// False once a request is pending.
    public var allowsNewWork:Bool { !pending }
    /// Records the request. Returns true when nothing is working and the app can quit now.
    /// Otherwise it calls `cancel` to stop the work; quit once `canTerminate` says the work has stopped.
    public mutating func request(working:Bool,cancel:()->Void)->Bool {
        pending=true
        guard working else { return true }
        cancel();return false
    }
    /// True when a pending request can be honoured because the work has stopped.
    public func canTerminate(working:Bool)->Bool { pending && !working }
}
