import Foundation

/// Reads and writes the media editor's start/end times. People type what the
/// timeline shows ("12:33.900"), not a raw second count ("753.9"), so both are accepted.
public enum MediaTimecode {
    /// "mm:ss.mmm", or "h:mm:ss.mmm" from one hour. Matches the playhead label.
    public static func format(_ seconds: Double) -> String {
        guard seconds.isFinite, seconds >= 0, seconds < 1e12 else { return "00:00.000" }
        // Round to the shown millisecond first so 59.9996 carries into the minute.
        let milliseconds = Int64((seconds * 1000).rounded())
        let hours = milliseconds / 3_600_000, minutes = milliseconds / 60_000 % 60
        let rest = Double(milliseconds % 60_000) / 1000
        return hours > 0 ? String(format: "%ld:%02ld:%06.3f", hours, minutes, rest)
                         : String(format: "%02ld:%06.3f", minutes, rest)
    }

    /// Accepts "753.9", "12:33.9", "1:02:33.5" and a decimal comma ("12:33,9").
    /// Returns nil for anything else, including negative values and seconds/minutes of 60 or more
    /// after the first field.
    public static func parse(_ text: String) -> Double? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: ",", with: ".")
        let parts = trimmed.split(separator: ":", omittingEmptySubsequences: false).map(String.init)
        guard !trimmed.isEmpty, (1...3).contains(parts.count) else { return nil }
        var total = 0.0
        for (index, part) in parts.enumerated() {
            let isLast = index == parts.count - 1
            // Only the seconds field may carry a fraction; every field must be plain digits.
            let allowed = CharacterSet(charactersIn: isLast ? "0123456789." : "0123456789")
            guard !part.isEmpty, part.unicodeScalars.allSatisfy(allowed.contains),
                  part.filter({ $0 == "." }).count <= 1, part != ".",
                  let value = Double(part), value.isFinite else { return nil }
            if index > 0, value >= 60 { return nil }
            total = total * 60 + value
        }
        return total.isFinite ? total : nil
    }
}
