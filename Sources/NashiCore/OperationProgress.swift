import Foundation

public enum OperationProgress {
    public static let indeterminate = -1.0

    /// Preserve unknown totals through nested file, pass and transaction scopes.
    public static func map(_ value: Double, from start: Double, to end: Double) -> Double {
        guard value.isFinite, value >= 0 else { return indeterminate }
        return start + min(1, value) * (end - start)
    }
}

/// One parser per process, called only by its stdout drain queue. FFmpeg's
/// machine-readable out_time_us is microseconds, independent of wall-clock speed.
final class FFmpegProgressParser: @unchecked Sendable {
    private var buffer = Data()
    private var last = 0.0
    private var discardingLine = false
    private let duration: Double?
    private let progress: ProgressHandler

    init(duration: Double?, progress: @escaping ProgressHandler) {
        self.duration = duration.flatMap { $0.isFinite && $0 > 0 ? $0 : nil }
        self.progress = progress
    }

    func append(_ data: Data) {
        buffer.append(data)
        while let newline = buffer.firstIndex(of: 10) {
            let line = String(decoding: buffer[..<newline], as: UTF8.self)
            buffer.removeSubrange(...newline)
            // The remainder of a discarded overlong line is not a line of its own.
            if discardingLine { discardingLine = false; continue }
            guard let duration, line.hasPrefix("out_time_us="),
                  let microseconds = Double(line.dropFirst("out_time_us=".count)),
                  microseconds.isFinite, microseconds >= 0 else { continue }
            // Encoding can reach its final timestamp before muxing or publication.
            let fraction = min(0.995, microseconds / 1_000_000 / duration)
            guard fraction > last else { continue }
            last = fraction
            progress(fraction, "Processing media…")
        }
        // A malformed helper cannot grow an unterminated progress line indefinitely.
        if buffer.count > 16_384 { buffer.removeAll(keepingCapacity: true); discardingLine = true }
    }
}
