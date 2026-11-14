import XCTest
@testable import NashiCore

final class MediaConversionProfileTests: XCTestCase {
    private struct Fixtures {
        let directory: URL
        let audio: URL
        let video: URL
        init() throws {
            directory = try OutputFiles.temporaryDirectory()
            audio = directory.appendingPathComponent("Precise24.wav")
            video = directory.appendingPathComponent("Source.mp4")
            try ProcessRunner.run(Dependencies.executable("ffmpeg"), ["-v", "error", "-f", "lavfi", "-i", "aevalsrc=exprs='0.2*sin(2*PI*440*t)+0.003*sin(2*PI*733*t)':s=48000:d=1.5", "-ac", "2", "-c:a", "pcm_s24le", "-metadata", "title=Profile title", "-metadata", "artist=Profile artist", audio.path])
            try ProcessRunner.run(Dependencies.executable("ffmpeg"), ["-v", "error", "-f", "lavfi", "-i", "testsrc2=size=384x216:rate=24:duration=1.5", "-i", audio.path, "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-c:a", "aac", "-metadata", "title=Video profile title", video.path])
        }
        func remove() { try? FileManager.default.removeItem(at: directory) }
    }

    func testPCMDepthSelectionsAndMatchSourcePreserve24BitSamples() throws {
        let f = try Fixtures(); defer { f.remove() }
        let source = try pcm(f.audio)
        let cases = [("matchSource", "s24"), ("pcm16", "s16"), ("pcm24", "s24"), ("pcm32", "s32"), ("float32", "f32")]
        for format in ["wav", "aiff"] {
            for (setting, codec) in cases {
                var options = OperationOptions(); options.audioSampleDepth = setting
                let out = try converted(f.audio, to: format, options: options)
                let info = try MediaEngine.inspect(out)
                XCTAssertEqual(info.audio?.codec, "pcm_\(codec)\(format == "wav" ? "le" : "be")", "\(format):\(setting)")
                if setting == "matchSource" || setting == "pcm24" { XCTAssertEqual(try pcm(out), source, "\(format):\(setting)") }
            }
        }
        let flac = try converted(f.audio, to: "flac")
        XCTAssertEqual(try MediaEngine.inspect(flac).audio?.bitsPerRawSample, 24)
        let restored = try converted(flac, to: "wav")
        XCTAssertEqual(try MediaEngine.inspect(restored).audio?.codec, "pcm_s24le")
        XCTAssertEqual(try pcm(restored), source)
    }

    func testMatchSourcePreservesFloatAndExplicitIntegerOverride() throws {
        let f = try Fixtures(); defer { f.remove() }
        var options = OperationOptions(); options.audioSampleDepth = "float32"
        let floating = try converted(f.audio, to: "wav", options: options)
        options.audioSampleDepth = "matchSource"
        let matched = try converted(floating, to: "aiff", options: options)
        XCTAssertEqual(try MediaEngine.inspect(matched).audio?.codec, "pcm_f32be")
        XCTAssertEqual(try raw(floating, format: "f32le"), try raw(matched, format: "f32le"))
        options.audioSampleDepth = "pcm16"
        let integer = try converted(floating, to: "wav", options: options)
        XCTAssertEqual(try MediaEngine.inspect(integer).audio?.bitsPerSample, 16)
    }

    func testBitrateSelectionIsIndependentOfVisualQuality() throws {
        let f = try Fixtures(); defer { f.remove() }
        for bitrate in [128, 192, 256, 320] {
            var options = OperationOptions(); options.audioBitrate = bitrate; options.quality = 0.1
            let mp3 = try converted(f.audio, to: "mp3", options: options)
            XCTAssertEqual(try MediaEngine.inspect(mp3).audio?.bitRate, bitrate * 1000)
        }
        var options = OperationOptions(); options.audioBitrate = 320
        let opus = try converted(f.audio, to: "opus", options: options)
        XCTAssertEqual(try MediaEngine.inspect(opus).audio?.codec, "opus")
        XCTAssertEqual(try MediaEngine.inspect(opus).duration, 1.5, accuracy: 0.1)
    }

    func testVideoMaximumHeightAndGIFProfileDoNotUpscale() throws {
        let f = try Fixtures(); defer { f.remove() }
        var options = OperationOptions(); options.videoMaxHeight = 72
        let small = try converted(f.video, to: "mkv", options: options)
        let info = try MediaEngine.inspect(small)
        XCTAssertEqual(info.video?.width, 128); XCTAssertEqual(info.video?.height, 72)
        options.videoMaxHeight = 720
        let originalSize = try converted(f.video, to: "mp4", options: options)
        XCTAssertEqual(try MediaEngine.inspect(originalSize).video?.height, 216)
        options.videoMaxHeight = 0; options.gifFrameRate = 10; options.gifMaxWidth = 96
        let gif = try converted(f.video, to: "gif", options: options)
        let gifInfo = try MediaEngine.inspect(gif)
        XCTAssertEqual(gifInfo.video?.width, 96); XCTAssertEqual(gifInfo.video?.height, 54)
        XCTAssertEqual(gifInfo.video?.frameRate ?? 0, 10, accuracy: 0.1)
        options.gifMaxWidth = 960
        let unscaled = try converted(f.video, to: "gif", options: options)
        XCTAssertEqual(try MediaEngine.inspect(unscaled).video?.width, 384)
    }

    func testMetadataProfileRetainsOrRemovesSourceTags() throws {
        let f = try Fixtures(); defer { f.remove() }
        var options = OperationOptions()
        let kept = try converted(f.audio, to: "m4a", options: options)
        XCTAssertEqual(try MediaEngine.inspect(kept).metadata["title"], "Profile title")
        XCTAssertEqual(try MediaEngine.inspect(kept).metadata["artist"], "Profile artist")
        options.preserveMetadata = false
        let cleaned = try converted(f.audio, to: "m4a", options: options)
        XCTAssertNil(try MediaEngine.inspect(cleaned).metadata["title"])
        XCTAssertNil(try MediaEngine.inspect(cleaned).metadata["artist"])
        let cleanVideo = try converted(f.video, to: "mp4", options: options)
        XCTAssertNil(try MediaEngine.inspect(cleanVideo).metadata["title"])
        XCTAssertEqual(try MediaEngine.inspect(f.audio).metadata["title"], "Profile title")
    }

    func testFLACPresetsRemainLosslessAndAffectCompression() throws {
        let f = try Fixtures(); defer { f.remove() }
        let original = try pcm(f.audio)
        var sizes: [String: Int] = [:]
        for preset in ["fast", "balanced", "maximum"] {
            var options = OperationOptions(); options.compressionPreset = preset
            let flac = try converted(f.audio, to: "flac", options: options)
            XCTAssertEqual(try pcm(flac), original, preset)
            sizes[preset] = try flac.resourceValues(forKeys: [.fileSizeKey]).fileSize
        }
        XCTAssertGreaterThan(sizes["fast"] ?? 0, sizes["maximum"] ?? 0)
    }

    func testConfigurableSilenceThresholdOnlyTrimsQuietEnds() throws {
        let f = try Fixtures(); defer { f.remove() }
        let input = f.directory.appendingPathComponent("Quiet ends.wav")
        let envelope = "aevalsrc=exprs='if(lt(t,0.5),0.001*sin(2*PI*220*t),if(lt(t,1.5),0.2*sin(2*PI*220*t),0.001*sin(2*PI*220*t)))':s=48000:d=2"
        try ProcessRunner.run(Dependencies.executable("ffmpeg"), ["-v", "error", "-f", "lavfi", "-i", envelope, input.path])
        var options = OperationOptions(); options.trimSilence = true; options.silenceThresholdDb = -50
        let trimmed = try tool(.trimAudio, input: input, options: options)
        XCTAssertEqual(try MediaEngine.inspect(trimmed).duration, 1, accuracy: 0.08)
        options.silenceThresholdDb = -75
        let retained = try tool(.trimAudio, input: input, options: options)
        XCTAssertEqual(try MediaEngine.inspect(retained).duration, 2, accuracy: 0.05)
    }

    func testTrimSilenceLooksForSilenceInsideTheSelection() throws {
        // Output -ss/-t cut after -af, so the silence filters would scan the file from its start to its end.
        let root = try OutputFiles.temporaryDirectory(); addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        let log = root.appendingPathComponent("argv.log"), ffprobe = root.appendingPathComponent("ffprobe"), ffmpeg = root.appendingPathComponent("ffmpeg")
        try Data("#!/bin/sh\necho '{\"format\":{\"duration\":\"60\"},\"streams\":[{\"index\":0,\"codec_type\":\"audio\",\"codec_name\":\"pcm_s16le\",\"channels\":2,\"sample_rate\":\"48000\"}]}'\n".utf8).write(to: ffprobe)
        try Data("#!/bin/sh\nprintf '%s\\n' \"$@\" > '\(log.path)'\nfor last; do :; done\nprintf x > \"$last\"\n".utf8).write(to: ffmpeg)
        for (variable, executable) in [("NASHI_FFPROBE", ffprobe), ("NASHI_FFMPEG", ffmpeg)] {
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: executable.path)
            let previous = ProcessInfo.processInfo.environment[variable]; setenv(variable, executable.path, 1)
            addTeardownBlock { if let previous { setenv(variable, previous, 1) } else { unsetenv(variable) } }
        }
        let input = root.appendingPathComponent("Interview.wav"); try Data([1]).write(to: input)
        for trimSilence in [true, false] {
            var options = OperationOptions(); options.outputDirectory = root.appendingPathComponent("out \(trimSilence)"); options.trimSilence = trimSilence; options.start = 10; options.end = 20
            _ = try MediaEngine.tool(.trimAudio, inputs: [input], options: options, token: .init())
            let argv = try String(contentsOf: log, encoding: .utf8).split(separator: "\n").map(String.init)
            let i = try XCTUnwrap(argv.firstIndex(of: "-i")), ss = try XCTUnwrap(argv.firstIndex(of: "-ss")), t = try XCTUnwrap(argv.firstIndex(of: "-t"))
            XCTAssertEqual([argv[ss + 1], argv[t + 1]], ["10", "10"])
            XCTAssertEqual(ss < i && t < i, trimSilence, "range must be read as input options only when silence is removed")
        }
    }

    func testNativeBleepAmplitudeAndValidationLimits() throws {
        let f = try Fixtures(); defer { f.remove() }
        XCTAssertThrowsError(try MediaEngine.tool(.redactAudio, inputs: [f.audio], options: .init(), token: .init()))
        var options = OperationOptions(); options.audioRedactions = [TimeRange(start: 0.5, end: 1)]
        let bleep = try tool(.redactAudio, input: f.audio, options: options)
        let data = try ProcessRunner.run(Dependencies.executable("ffmpeg"), ["-v", "error", "-ss", "0.6", "-i", bleep.path, "-t", "0.2", "-af", "pan=mono|c0=c0", "-f", "f32le", "-"]).output
        let values = data.withUnsafeBytes { bytes in stride(from: 0, to: bytes.count - 3, by: 4).map { bytes.loadUnaligned(fromByteOffset: $0, as: Float.self) } }
        let rms = sqrt(values.reduce(0) { $0 + Double($1 * $1) } / Double(values.count))
        XCTAssertEqual(rms, 0.18 / sqrt(2), accuracy: 0.001)
        XCTAssertEqual(try MediaEngine.inspect(bleep).audio?.codec, "pcm_s24le")
        options.splitPoints = [0.01]
        XCTAssertThrowsError(try MediaEngine.tool(.splitVideo, inputs: [f.video], options: options, token: .init()))
        options.splitPoints = [0.5, 0.52]
        XCTAssertThrowsError(try MediaEngine.tool(.splitVideo, inputs: [f.video], options: options, token: .init()))
        let joined = try XCTUnwrap(MediaEngine.tool(.joinVideos, inputs: [f.video, f.video], options: .init(), token: .init()).first)
        XCTAssertEqual(try MediaEngine.inspect(joined).video?.frameRate, 30)
    }

    private func converted(_ input: URL, to format: String, options: OperationOptions = .init()) throws -> URL {
        let outputs = try MediaEngine.convert(input, to: format, options: options, token: .init())
        return try XCTUnwrap(outputs.first)
    }
    private func tool(_ kind: ToolKind, input: URL, options: OperationOptions) throws -> URL {
        let outputs = try MediaEngine.tool(kind, inputs: [input], options: options, token: .init())
        return try XCTUnwrap(outputs.first)
    }
    private func pcm(_ url: URL) throws -> Data { try raw(url, format: "s32le") }
    private func raw(_ url: URL, format: String) throws -> Data {
        try ProcessRunner.run(Dependencies.executable("ffmpeg"), ["-v", "error", "-i", url.path, "-map", "0:a:0", "-f", format, "-"]).output
    }
}
