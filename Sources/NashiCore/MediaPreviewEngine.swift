import Foundation
import AVFoundation

public struct MediaPreviewResult: Sendable, Equatable {
    public let originalURL: URL
    public let playbackURL: URL
    public let ownedTemporaryDirectory: URL?
    public var isProxy: Bool { playbackURL != originalURL }

    /// Pause/release the player before removing its temporary preview.
    public func removeTemporaryFiles() {
        if let ownedTemporaryDirectory { try? FileManager.default.removeItem(at: ownedTemporaryDirectory) }
    }
}

/// Playable previews and bounded-memory waveform analysis for native media editors.
public enum MediaPreviewEngine {
    public static func prepare(_ input: URL, forceProxy: Bool = false, token: CancellationToken = .init()) async throws -> MediaPreviewResult {
        try checkCancellation(token)
        guard input.isFileURL, FileManager.default.isReadableFile(atPath: input.path) else { throw NashiError.invalidInput("Choose a readable local media file.") }
        if !forceProxy, try await nativelyPlayable(input, token: token) {
            return MediaPreviewResult(originalURL: input, playbackURL: input, ownedTemporaryDirectory: nil)
        }
        let info = try MediaEngine.inspect(input, token: token)
        guard info.hasVideo || info.hasAudio else { throw NashiError.invalidInput("The file has no playable audio or video track.") }
        let directory = try OutputFiles.temporaryDirectory()
        do {
            let output = directory.appendingPathComponent(info.hasVideo ? "Preview.mp4" : "Preview.m4a")
            var arguments = ["-hide_banner", "-nostdin", "-v", "error", "-n", "-i", input.path, "-map_metadata", "-1", "-map_chapters", "-1"]
            if info.hasVideo {
                // Autorotation is enabled by FFmpeg. reset_sar also preserves anamorphic display geometry.
                let scale = "scale=w='min(1920,iw)':h='min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2:reset_sar=1"
                arguments += ["-map", "0:v:0", "-map", "0:a:0?", "-vf", scale, "-c:v", "libx264", "-preset", "veryfast", "-crf", "24", "-pix_fmt", "yuv420p"]
            } else { arguments += ["-map", "0:a:0", "-vn"] }
            if info.hasAudio { arguments += ["-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2"] }
            arguments += ["-movflags", "+faststart", output.path]
            try ProcessRunner.run(Dependencies.executable("ffmpeg"), arguments, token: token)
            try checkCancellation(token)
            try OutputFiles.validate([output])
            guard try await nativelyPlayable(output, token: token) else { throw NashiError.failed("macOS could not open the generated media preview.") }
            return MediaPreviewResult(originalURL: input, playbackURL: output, ownedTemporaryDirectory: directory)
        } catch {
            try? FileManager.default.removeItem(at: directory)
            throw error
        }
    }

    /// Linear 0...1 peaks over approximately uniform time bins. Output always has `bins` values.
    /// FFmpeg accumulates peaks in small audio frames; neither decoded PCM nor per-frame logs are retained.
    public static func waveformPeaks(_ input: URL, bins: Int = 600, channel: Int? = nil, token: CancellationToken = .init()) throws -> [Float] {
        try checkCancellation(token)
        guard (1...10000).contains(bins) else { throw NashiError.invalidInput("Choose between 1 and 10,000 waveform bins.") }
        let info = try MediaEngine.inspect(input, token: token)
        guard let audio = info.audio else { throw NashiError.invalidInput("The file has no audio track.") }
        if let channel,channel < 0 || channel >= (audio.channels ?? 1) { throw NashiError.invalidInput("The selected audio channel does not exist.") }
        let sampleRate = audio.sampleRate ?? 48000
        let estimated = ceil(info.duration * Double(sampleRate))
        guard sampleRate > 0, estimated.isFinite, estimated > 0, estimated < Double(Int64.max / 4) else { throw NashiError.invalidInput("A waveform requires a finite media duration.") }
        let samples = Int64(estimated)
        let desiredBlock = max(1, Int64(ceil(Double(samples) / Double(bins))))
        // Equal frames of at most 1024 samples per bin; a fixed 1024 can leave up to half the bins unused.
        let framesPerBlock = (desiredBlock + 1023) / 1024
        let frameSize = (desiredBlock + framesPerBlock - 1) / framesPerBlock
        let frameCount = (samples + frameSize - 1) / frameSize
        let framesPerBin = max(1, (frameCount + Int64(bins) - 1) / Int64(bins))
        guard framesPerBin <= Int64(Int32.max) else { throw NashiError.invalidInput("The media is too long for the requested waveform resolution.") }
        let groups = (frameCount + framesPerBin - 1) / framesPerBin
        let paddedSamples = groups * framesPerBin * frameSize
        // The final partial bin is padded with silence. aselect emits only completed bins and
        // caps logs at `bins` even if a damaged container reports an incorrect duration.
        let isolate=channel.map{"pan=mono|c0=c\($0),"} ?? ""
        let filter = isolate + "apad=whole_len=\(paddedSamples),asetnsamples=n=\(frameSize):p=1,astats=metadata=1:reset=\(framesPerBin):measure_perchannel=none:measure_overall=Peak_level,aselect='lt(selected_n,\(bins))*not(mod(n+1,\(framesPerBin)))',ametadata=print:key=lavfi.astats.Overall.Peak_level:file=-"
        let result = try ProcessRunner.run(Dependencies.executable("ffmpeg"), ["-hide_banner", "-nostdin", "-v", "error", "-i", input.path, "-map", "0:a:0", "-vn", "-sn", "-dn", "-af", filter, "-f", "null", "-"], token: token)
        try checkCancellation(token)
        let key = "lavfi.astats.Overall.Peak_level="
        let peaks: [Float] = result.text.split(whereSeparator: \.isNewline).compactMap { line in
            guard line.hasPrefix(key), let decibels = Double(line.dropFirst(key.count)) else { return nil }
            return decibels.isFinite ? Float(min(1, max(0, pow(10, decibels / 20)))) : 0
        }
        guard !peaks.isEmpty else { throw NashiError.failed("Could not decode the audio waveform.") }
        if peaks.count == bins { return peaks }
        // Very short files and integer frame grouping can yield fewer bins. Preserve peak
        // values while spreading these uniform groups over the requested display width.
        return (0..<bins).map { index in
            let lower = min(peaks.count - 1, index * peaks.count / bins)
            let upper = min(peaks.count, max(lower + 1, (index + 1) * peaks.count / bins))
            return peaks[lower..<upper].max() ?? 0
        }
    }

    /// Export into an owned temporary folder, then prepare a native player-compatible preview.
    /// Use this for Original / Processed comparison without creating exports beside the source.
    public static func processedPreview(_ kind: ToolKind, input: URL, options: OperationOptions = .init(), token: CancellationToken = .init()) async throws -> MediaPreviewResult {
        let supported: Set<ToolKind> = [.normalizeAudio, .muteVideo, .trimVideo, .cropVideo, .changeVideoSpeed, .splitVideo, .audioToVideo, .trimAudio, .audioChannels, .redactVideo, .redactAudio]
        guard supported.contains(kind) else { throw NashiError.unsupported("This operation does not produce a single media preview.") }
        if kind == .splitVideo, !options.joinSplitClips { throw NashiError.invalidInput("Choose Export One Video to preview the retained clips together.") }
        try checkCancellation(token)
        let directory = try OutputFiles.temporaryDirectory()
        do {
            var settings = options
            settings.outputDirectory = directory; settings.revealInFinder = false
            let outputs = try MediaEngine.tool(kind, inputs: [input], options: settings, token: token)
            guard outputs.count == 1, let output = outputs.first else { throw NashiError.failed("The preview did not produce a single media file.") }
            let prepared = try await prepare(output, token: token)
            if let proxyDirectory = prepared.ownedTemporaryDirectory {
                try? FileManager.default.removeItem(at: directory)
                return MediaPreviewResult(originalURL: input, playbackURL: prepared.playbackURL, ownedTemporaryDirectory: proxyDirectory)
            }
            return MediaPreviewResult(originalURL: input, playbackURL: output, ownedTemporaryDirectory: directory)
        } catch {
            try? FileManager.default.removeItem(at: directory)
            throw error
        }
    }

    public static func bleepPreview(_ input: URL, options: OperationOptions = .init(), token: CancellationToken = .init()) async throws -> MediaPreviewResult {
        try await processedPreview(.redactAudio, input: input, options: options, token: token)
    }

    /// The visualizer needs a responsive preview even for hours-long recordings.
    /// Its actual export still uses the complete input and the requested dimensions.
    public static func visualizationPreview(_ input:URL,options:OperationOptions = .init(),maximumDuration:Double = 12,token:CancellationToken = .init()) async throws -> MediaPreviewResult {
        try checkCancellation(token)
        guard maximumDuration.isFinite,maximumDuration>0,maximumDuration<=60 else { throw NashiError.invalidInput("Preview duration must be between 0 and 60 seconds.") }
        let width=options.width ?? 1920,height=options.height ?? 1080
        guard (16...8192).contains(width),(16...8192).contains(height) else { throw NashiError.invalidInput("Visualization dimensions must be between 16 and 8,192 pixels.") }
        let directory=try OutputFiles.temporaryDirectory()
        do {
            let audio=directory.appendingPathComponent("Preview Audio.wav")
            try ProcessRunner.run(Dependencies.executable("ffmpeg"),["-hide_banner","-nostdin","-v","error","-n","-i",input.path,"-t",String(maximumDuration),"-map","0:a:0","-vn","-ar","48000","-ac","2","-c:a","pcm_s16le",audio.path],token:token)
            var settings=options
            let scale=min(1,640/Double(max(width,height)))
            settings.width=max(16,Int(Double(width)*scale)/2*2);settings.height=max(16,Int(Double(height)*scale)/2*2)
            settings.outputDirectory=directory;settings.revealInFinder=false
            guard let output=try MediaEngine.tool(.audioToVideo,inputs:[audio],options:settings,token:token).first else { throw NashiError.failed("Could not create the visualization preview.") }
            try? FileManager.default.removeItem(at:audio)
            let prepared=try await prepare(output,token:token)
            if let proxy=prepared.ownedTemporaryDirectory { try? FileManager.default.removeItem(at:directory);return MediaPreviewResult(originalURL:input,playbackURL:prepared.playbackURL,ownedTemporaryDirectory:proxy) }
            return MediaPreviewResult(originalURL:input,playbackURL:output,ownedTemporaryDirectory:directory)
        } catch { try? FileManager.default.removeItem(at:directory);throw error }
    }

    private static func nativelyPlayable(_ input: URL, token: CancellationToken) async throws -> Bool {
        do {
            let asset = AVURLAsset(url: input)
            guard try await asset.load(.isPlayable) else { try checkCancellation(token); return false }
            let tracks = try await asset.load(.tracks)
            var hasMedia = false
            for track in tracks where track.mediaType == .video || track.mediaType == .audio {
                hasMedia = true
                guard try await track.load(.isDecodable) else { try checkCancellation(token); return false }
            }
            try checkCancellation(token)
            return hasMedia
        } catch { try checkCancellation(token); return false }
    }

    private static func checkCancellation(_ token: CancellationToken) throws {
        if Task.isCancelled { throw NashiError.cancelled }
        try token.check()
    }
}
