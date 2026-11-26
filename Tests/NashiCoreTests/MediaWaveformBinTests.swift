import XCTest
@testable import NashiCore

/// The fake ffmpeg emits one distinct peak per completed astats bin of the filter it receives.
final class MediaWaveformBinTests: XCTestCase {
    func testWaveformUsesEveryRequestedBinJustAboveFullFrames() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("nashi-waveform-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let probe = root.appendingPathComponent("probe.json"), ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\ncat '\(probe.path)'\n".utf8).write(to: ffprobe)
        let awk = #"{ match($0,/whole_len=[0-9]+/); w=substr($0,RSTART+10,RLENGTH-10); match($0,/asetnsamples=n=[0-9]+/); n=substr($0,RSTART+15,RLENGTH-15); match($0,/reset=[0-9]+/); r=substr($0,RSTART+6,RLENGTH-6); match($0,/lt\(selected_n,[0-9]+/); b=substr($0,RSTART+14,RLENGTH-14); e=int(w/(n*r)); if(e>b+0)e=b+0; if(n>1024)e=0; for(i=1;i<=e;i++) printf "lavfi.astats.Overall.Peak_level=%.4f\n", -i*0.01 }"#
        try Data("#!/bin/sh\nprev=''; f=''\nfor a in \"$@\"; do [ \"$prev\" = '-af' ] && f=\"$a\"; prev=\"$a\"; done\nprintf '%s\\n' \"$f\" | awk '\(awk)'\n".utf8).write(to: ffmpeg)
        for (variable, tool) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: tool.path)
            let previous = ProcessInfo.processInfo.environment[variable]
            setenv(variable, tool.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent("Clip.wav"); try Data([1]).write(to: input)
        // 13 s and 30 s need more than 1024 samples per bin; 10 s and one hour are controls.
        for (duration, rate) in [(13.0, 48000), (30.0, 44100), (10.0, 48000), (3600.0, 48000)] {
            try Data(#"{"format":{"duration":"\#(duration)"},"streams":[{"index":0,"codec_type":"audio","codec_name":"pcm_s16le","channels":2,"sample_rate":"\#(rate)"}]}"#.utf8).write(to: probe)
            let peaks = try MediaPreviewEngine.waveformPeaks(input, bins: 600)
            XCTAssertEqual(peaks.count, 600)
            XCTAssertGreaterThanOrEqual(Set(peaks).count, 594, "\(duration) s at \(rate) Hz")
        }
    }
}
