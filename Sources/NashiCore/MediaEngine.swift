import Foundation

public struct MediaStream: Sendable, Equatable {
    public let index: Int
    public let kind: String
    public let codec: String
    public let duration: Double?
    public let width: Int?
    public let height: Int?
    public let channels: Int?
    public let sampleRate: Int?
    public let bitsPerSample: Int?
    public let bitsPerRawSample: Int?
    public let sampleFormat: String?
    public let bitRate: Int?
    public let frameRate: Double?
    public let isAttachedPicture: Bool
    public let rotation: Int
    public let metadata: [String: String]
}

public struct MediaChapter: Sendable, Equatable {
    public let index: Int
    public let start: Double
    public let end: Double
    public let metadata: [String: String]
}

public struct MediaInfo: Sendable, Equatable {
    public let duration: Double
    public let size: Int64
    public let format: String
    public let streams: [MediaStream]
    public let chapters: [MediaChapter]
    public let metadata: [String: String]
    public var video: MediaStream? { streams.first { $0.kind == "video" && ($0.width ?? 0) > 0 } }
    public var audio: MediaStream? { streams.first { $0.kind == "audio" } }
    public var hasVideo: Bool { video != nil }
    public var hasAudio: Bool { audio != nil }
}

public struct AudioLoudness: Sendable, Equatable {
    public let integrated: Double
    public let truePeak: Double
    public let range: Double
    public let threshold: Double
    public let targetOffset: Double
}

/// Local media operations. Every export creates a new file; source files are never rewritten.
public enum MediaEngine {
    public static let audioFormats: Set<String> = ["mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "aif", "wma", "aac"]
    public static let videoFormats: Set<String> = ["mp4", "mov", "mkv", "webm", "avi", "wmv", "m4v", "gif"]

    public static func inspect(_ input: URL, token: CancellationToken = .init()) throws -> MediaInfo {
        try validateInput(input, token: token)
        let result = try ProcessRunner.run(Dependencies.executable("ffprobe"), ["-v", "error", "-show_format", "-show_streams", "-show_chapters", "-of", "json", input.path], token: token)
        guard let root = try JSONSerialization.jsonObject(with: result.output) as? [String: Any] else { throw NashiError.failed("Could not inspect media information.") }
        let format = root["format"] as? [String: Any] ?? [:]
        let streams = (root["streams"] as? [[String: Any]] ?? []).map { item in
            let metadata = tags(item["tags"])
            let sideData = item["side_data_list"] as? [[String: Any]] ?? []
            let disposition = item["disposition"] as? [String: Any] ?? [:]
            let rotation = sideData.compactMap { integer($0["rotation"]) }.first ?? Int(metadata["rotate"] ?? "0") ?? 0
            let swap = abs(rotation % 180) == 90
            // FFmpeg and AVPlayer apply the display matrix; editing coordinates use that canvas.
            let width = integer(item[swap ? "height" : "width"]), height = integer(item[swap ? "width" : "height"])
            return MediaStream(index: integer(item["index"]) ?? 0, kind: item["codec_type"] as? String ?? "unknown", codec: item["codec_name"] as? String ?? "unknown", duration: number(item["duration"]), width: width, height: height, channels: integer(item["channels"]), sampleRate: integer(item["sample_rate"]), bitsPerSample: integer(item["bits_per_sample"]), bitsPerRawSample: integer(item["bits_per_raw_sample"]), sampleFormat: item["sample_fmt"] as? String, bitRate: integer(item["bit_rate"]), frameRate: fraction(item["avg_frame_rate"] as? String) ?? fraction(item["r_frame_rate"] as? String), isAttachedPicture: integer(disposition["attached_pic"]) == 1, rotation: rotation, metadata: metadata)
        }
        let chapters = (root["chapters"] as? [[String: Any]] ?? []).enumerated().map { index, item in
            MediaChapter(index: index, start: number(item["start_time"]) ?? 0, end: number(item["end_time"]) ?? 0, metadata: tags(item["tags"]))
        }
        let duration = number(format["duration"]) ?? streams.compactMap(\.duration).max() ?? 0
        let size = (try input.resourceValues(forKeys: [.fileSizeKey]).fileSize).map(Int64.init) ?? 0
        return MediaInfo(duration: duration, size: size, format: format["format_name"] as? String ?? input.pathExtension, streams: streams, chapters: chapters, metadata: tags(format["tags"]))
    }

    /// Keys are `file:title`, `track[0]:language`, or `chapter[0]:title`.
    public static func readMetadata(_ input: URL, token: CancellationToken = .init()) throws -> [String: String] {
        let info = try inspect(input, token: token)
        var fields = Dictionary(uniqueKeysWithValues: info.metadata.map { ("file:\($0.key)", $0.value) })
        for stream in info.streams { for (key, value) in stream.metadata { fields["track[\(stream.index)]:\(key)"] = value } }
        for chapter in info.chapters { for (key, value) in chapter.metadata { fields["chapter[\(chapter.index)]:\(key)"] = value } }
        return fields
    }

    public static func editMetadata(_ input: URL, metadata: [String: String], removeAll: Bool = false, options: OperationOptions = .init(), token: CancellationToken = .init(), progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        let info = try inspect(input, token: token)
        let ext = input.pathExtension.lowercased()
        guard audioFormats.contains(ext) || videoFormats.contains(ext) else { throw NashiError.unsupported("This is not a supported audio or video file.") }
        let output = try outputURL(input, ext: ext, suffix: " Metadata", options: options)
        var arguments = ["-i", input.path, "-map", "0", "-c", "copy"]
        if removeAll {
            // Timed data tracks can retain chapter text, GPS, or camera telemetry after tag removal.
            arguments += ["-map", "-0:d?", "-map_metadata", "-1", "-map_metadata:s", "-1", "-map_chapters", "-1", "-fflags", "+bitexact", "-flags:v", "+bitexact", "-flags:a", "+bitexact"]
            // Album artwork is stored as an attached video stream rather than
            // a text tag. Remove that metadata image while retaining real video.
            for stream in info.streams where stream.isAttachedPicture {
                arguments += ["-map", "-0:\(stream.index)"]
            }
        }
        for key in metadata.keys.sorted() {
            guard let value = metadata[key], !key.isEmpty, !key.contains("\0"), !value.contains("\0") else { throw NashiError.invalidInput("Metadata fields cannot be empty or contain null characters.") }
            let (requested, field) = try metadataSpecifier(key)
            var specifier = requested
            guard !field.isEmpty, !field.contains("=") else { throw NashiError.invalidInput("Invalid metadata field: \(key)") }
            if removeAll, specifier.hasPrefix("-metadata:s:"), let index = Int(specifier.dropFirst("-metadata:s:".count)) {
                // Stream specifiers address output streams; unmapped tracks shift later ones down.
                let removed = Set(info.streams.filter { $0.kind == "data" || $0.isAttachedPicture }.map(\.index))
                guard !removed.contains(index) else { throw NashiError.invalidInput("Track \(index) is removed with all metadata: \(key)") }
                specifier = "-metadata:s:\(index - removed.filter { $0 < index }.count)"
            }
            arguments += [specifier, "\(field)=\(value)"]
        }
        if ["mp4", "mov", "m4a", "m4v"].contains(ext) { arguments += ["-movflags", "+use_metadata_tags"] }
        try render(arguments, to: output, token: token, duration: info.duration, progress: progress)
        return [output]
    }

    public static func convert(_ input: URL, to format: String, options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        let info = try inspect(input, token: token)
        let ext = format.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "."))
        guard audioFormats.contains(ext) || videoFormats.contains(ext) else { throw NashiError.unsupported("Unsupported media format: \(format)") }
        try validateQuality(options.quality)
        try validateMediaPreferences(options)
        let output = try outputURL(input, ext: ext, options: options)
        var arguments = ["-i", input.path]
        if audioFormats.contains(ext) {
            try requireAudio(info)
            arguments += ["-map", "0:a:0", "-vn"] + audioEncoding(ext, quality: options.quality, bitrate: options.audioBitrate * 1000, source: info.audio, pcmCodec: try pcmCodec(for: ext, source: info.audio, setting: options.audioSampleDepth), compressionPreset: options.compressionPreset)
        } else {
            try requireVideo(info)
            arguments += ["-map", "0:v:0"]
            if ext == "gif" {
                let scale = try scalingFilter(options)
                let first = (["fps=\(options.gifFrameRate)"] + (scale.map { [$0] } ?? []) + ["scale=w='min(\(options.gifMaxWidth),iw)':h=-2:force_original_aspect_ratio=decrease"]).joined(separator: ",")
                arguments += ["-filter_complex", "[0:v:0]\(first),split[p0][p1];[p0]palettegen=stats_mode=diff[pal];[p1][pal]paletteuse=dither=sierra2_4a", "-an", "-loop", "0"]
                // Complex filters map their final output automatically.
                arguments.removeSubrange(2..<4)
            } else {
                arguments += ["-map", "0:a:0?"]
                if let scale = try scalingFilter(options) { arguments += ["-vf", scale] }
                arguments += videoEncoding(ext, quality: options.quality, audioBitrate: options.audioBitrate * 1000, sourceAudio: info.audio)
            }
        }
        if options.preserveMetadata { arguments += ["-map_metadata", "0"] }
        else { arguments += ["-map_metadata", "-1", "-map_metadata:s", "-1", "-map_chapters", "-1"] }
        arguments += metadataArguments(options.metadata)
        try render(arguments, to: output, token: token, duration: info.duration, progress: progress)
        return [output]
    }

    public static func tool(_ kind: ToolKind, inputs: [URL], options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        guard !inputs.isEmpty else { throw NashiError.invalidInput("Choose at least one media file.") }
        try token.check()
        try validateQuality(options.quality)
        try validateMediaPreferences(options)
        if kind == .joinVideos { return try join(inputs, options: options, token: token, progress: progress) }
        var results: [URL] = []
        do {
            for (index, input) in inputs.enumerated() {
                let itemProgress: ProgressHandler = { value,label in
                    progress(OperationProgress.map(value, from: Double(index)/Double(inputs.count), to: Double(index+1)/Double(inputs.count)),label)
                }
                itemProgress(OperationProgress.indeterminate, "Inspecting \(input.lastPathComponent)…")
                try token.check()
                let info = try inspect(input, token: token)
                switch kind {
                case .compress: results += try compress(input, info: info, options: options, token: token, progress: itemProgress)
                case .removeMetadata: results += try editMetadata(input, metadata: options.metadata, removeAll: options.metadataRemoveAll || options.metadata.isEmpty, options: options, token: token, progress: itemProgress)
                case .normalizeAudio: results += try normalize(input, info: info, options: options, token: token, progress: itemProgress)
                case .muteVideo:
                    try requireVideo(info)
                    let out = try outputURL(input, ext: videoExtension(input), suffix: " Muted", options: options)
                    try render(["-i", input.path, "-map", "0:v:0", "-c:v", "copy", "-an"], to: out, token: token, duration: info.duration, progress: itemProgress)
                    results.append(out)
                case .trimVideo, .trimAudio: results += try trim(input, info: info, audio: kind == .trimAudio, options: options, token: token, progress: itemProgress)
                case .cropVideo: results += try crop(input, info: info, options: options, token: token, progress: itemProgress)
                case .changeVideoSpeed: results += try changeSpeed(input, info: info, options: options, token: token, progress: itemProgress)
                case .videoSnapshots: results += try snapshots(input, info: info, options: options, token: token, progress: itemProgress)
                case .splitVideo: results += try split(input, info: info, options: options, token: token, progress: itemProgress)
                case .audioToVideo: results += try visualization(input, info: info, options: options, token: token, progress: itemProgress)
                case .audioChannels: results += try channels(input, info: info, options: options, token: token, progress: itemProgress)
                case .redactVideo: results += try redactVideo(input, info: info, options: options, token: token, progress: itemProgress)
                case .redactAudio: results += try redactAudio(input, info: info, options: options, token: token, progress: itemProgress)
                default: throw NashiError.unsupported("\(kind.title) does not operate on audio or video files.")
                }
            }
            try OutputFiles.validate(results)
            return results
        } catch {
            for output in results { try? FileManager.default.removeItem(at: output) }
            if kind == .splitVideo && !options.joinSplitClips { for folder in Set(results.map{$0.deletingLastPathComponent()}) { if (try? FileManager.default.contentsOfDirectory(atPath:folder.path).isEmpty) == true { try? FileManager.default.removeItem(at:folder) } } }
            throw error
        }
    }

    public static func measureLoudness(_ input: URL, target: Double = -16, range: Double = 11, peak: Double = -1.5, token: CancellationToken = .init(), progress: @escaping ProgressHandler = {_,_ in}) throws -> AudioLoudness {
        try validateNormalization(target: target, range: range, peak: peak)
        let result = try ffmpeg(["-i", input.path, "-map", "0:a:0", "-af", "loudnorm=I=\(decimal(target)):LRA=\(decimal(range)):TP=\(decimal(peak)):print_format=json", "-f", "null", "-"], token: token, verbosity: "info", duration: try inspect(input, token: token).duration, progress: { value,_ in progress(value,"Measuring audio loudness…") })
        let text = String(decoding: result.errorOutput, as: UTF8.self)
        guard let opening = text.range(of: "{", options: .backwards), let closing = text.range(of: "}", options: .backwards), opening.lowerBound < closing.upperBound,
              let data = String(text[opening.lowerBound..<closing.upperBound]).data(using: .utf8), let json = try JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw NashiError.failed("Could not measure the audio loudness.") }
        return AudioLoudness(integrated: number(json["input_i"]) ?? -.infinity, truePeak: number(json["input_tp"]) ?? -.infinity, range: number(json["input_lra"]) ?? 0, threshold: number(json["input_thresh"]) ?? -70, targetOffset: number(json["target_offset"]) ?? 0)
    }

    /// The caller owns this temporary preview and may delete its parent directory after playback.
    public static func previewChannel(_ input: URL, channel: Int, options: OperationOptions = .init(), token: CancellationToken = .init()) throws -> URL {
        guard channel == 0 || channel == 1 else { throw NashiError.invalidInput("Choose the left or right channel.") }
        let info = try inspect(input, token: token)
        try requireAudio(info)
        let filter = try channelFilter(info: info, options: options) + ",pan=mono|c0=c\(options.channels == 1 ? 0 : channel)"
        let directory = try OutputFiles.temporaryDirectory()
        let out = directory.appendingPathComponent(channel == 0 ? "Left.wav" : "Right.wav")
        do {
            try render(["-i", input.path, "-map", "0:a:0", "-af", filter, "-c:a", "pcm_s16le"], to: out, token: token)
            return out
        } catch { try? FileManager.default.removeItem(at: directory); throw error }
    }

    private static func normalize(_ input: URL, info: MediaInfo, options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        try requireAudio(info)
        let measurement = try measureLoudness(input, target: options.targetLoudness, range: options.loudnessRange, peak: options.truePeak, token: token, progress: { value,label in progress(OperationProgress.map(value, from: 0, to: 0.45),label) })
        var filter = "loudnorm=I=\(decimal(options.targetLoudness)):LRA=\(decimal(options.loudnessRange)):TP=\(decimal(options.truePeak))"
        if [measurement.integrated, measurement.truePeak, measurement.targetOffset, measurement.range, measurement.threshold].allSatisfy(\.isFinite) {
            filter += ":measured_I=\(decimal(measurement.integrated)):measured_TP=\(decimal(measurement.truePeak)):measured_LRA=\(decimal(measurement.range)):measured_thresh=\(decimal(measurement.threshold)):offset=\(decimal(measurement.targetOffset)):linear=true"
        }
        let ext = audioExtension(input)
        let out = try outputURL(input, ext: ext, suffix: " Normalized", options: options)
        try render(["-i", input.path, "-map", "0:a:0", "-af", filter, "-ar", String(info.audio?.sampleRate ?? 48000)] + audioEncoding(ext, quality: options.quality, source: info.audio, pcmCodec: try pcmCodec(for: ext, source: info.audio, setting: options.audioSampleDepth), compressionPreset: options.compressionPreset), to: out, token: token, duration: info.duration, progress: { value,label in progress(OperationProgress.map(value, from: 0.45, to: 1),label) })
        return [out]
    }

    private static func trim(_ input: URL, info: MediaInfo, audio: Bool, options: OperationOptions, token: CancellationToken, suffix: String = " Trimmed", progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        if audio { try requireAudio(info) } else { try requireVideo(info) }
        let interval = try timeRange(options.start, options.end, duration: info.duration)
        let ext = audio ? audioExtension(input) : videoExtension(input)
        let out = try outputURL(input, ext: ext, suffix: suffix, options: options)
        var arguments = ["-i", input.path, "-ss", decimal(interval.0), "-t", decimal(interval.1 - interval.0)]
        if audio {
            arguments += ["-map", "0:a:0", "-vn"]
            if options.trimSilence { arguments += ["-af", "silenceremove=start_periods=1:start_duration=0.02:start_threshold=\(decimal(options.silenceThresholdDb))dB,areverse,silenceremove=start_periods=1:start_duration=0.02:start_threshold=\(decimal(options.silenceThresholdDb))dB,areverse"] }
            arguments += audioEncoding(ext, quality: options.quality, source: info.audio, pcmCodec: try pcmCodec(for: ext, source: info.audio, setting: options.audioSampleDepth), compressionPreset: options.compressionPreset)
        } else { arguments += ["-map", "0:v:0", "-map", "0:a:0?"] + videoEncoding(ext, quality: options.quality, sourceAudio: info.audio) }
        try render(arguments, to: out, token: token, duration: audio && options.trimSilence ? nil : interval.1 - interval.0, progress: progress)
        return [out]
    }

    private static func crop(_ input: URL, info: MediaInfo, options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        try requireVideo(info)
        guard let video = info.video, let width = video.width, let height = video.height else { throw NashiError.invalidInput("The video has no readable dimensions.") }
        let requested = options.crop ?? EditRect(x: 0, y: 0, width: Double(options.width ?? width), height: Double(options.height ?? height))
        let rect = try pixelRect(requested, width: width, height: height)
        let out = try outputURL(input, ext: videoExtension(input), suffix: " Cropped", options: options)
        let filter = "crop=\(rect.w):\(rect.h):\(rect.x):\(rect.y):exact=1,pad=ceil(iw/2)*2:ceil(ih/2)*2"
        try render(["-i", input.path, "-map", "0:v:0", "-map", "0:a:0?", "-vf", filter] + videoEncoding(out.pathExtension, quality: options.quality, copyAudio: true), to: out, token: token, duration: info.duration, progress: progress)
        return [out]
    }

    private static func changeSpeed(_ input: URL, info: MediaInfo, options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        try requireVideo(info)
        guard options.speed.isFinite, options.speed >= 0.05, options.speed <= 100 else { throw NashiError.invalidInput("Playback speed must be between 0.05× and 100×.") }
        let out = try outputURL(input, ext: videoExtension(input), suffix: " \(decimal(options.speed))x", options: options)
        var arguments = ["-i", input.path, "-map", "0:v:0", "-map", "0:a:0?", "-vf", "setpts=(PTS-STARTPTS)/\(decimal(options.speed))", "-r", decimal((info.video?.frameRate ?? 30) * options.speed), "-fps_mode", "cfr"]
        if info.hasAudio { arguments += ["-af", tempoFilter(options.speed)] }
        arguments += videoEncoding(out.pathExtension, quality: options.quality, sourceAudio: info.audio)
        try render(arguments, to: out, token: token, duration: info.duration / options.speed, progress: progress)
        return [out]
    }

    private static func join(_ inputs: [URL], options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        guard inputs.count >= 2 else { throw NashiError.invalidInput("Choose two or more videos to join.") }
        let infos = try inputs.map { try inspect($0, token: token) }
        try infos.forEach(requireVideo)
        guard let first = infos.first?.video else { throw NashiError.invalidInput("The first clip has no video.") }
        let width = max(2, (first.width ?? 1280) / 2 * 2), height = max(2, (first.height ?? 720) / 2 * 2)
        let fps = 30.0
        let audio = infos.contains { $0.hasAudio }
        let temp = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: temp) }
        var clips: [URL] = []
        for (index, input) in inputs.enumerated() {
            try token.check()
            let clip = temp.appendingPathComponent("clip-\(index).mp4")
            var arguments = ["-i", input.path]
            if audio && !infos[index].hasAudio { arguments += ["-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo"] }
            arguments += ["-map", "0:v:0", "-vf", "scale=\(width):\(height):force_original_aspect_ratio=decrease,pad=\(width):\(height):(ow-iw)/2:(oh-ih)/2,setsar=1,fps=\(decimal(fps)),setpts=PTS-STARTPTS"]
            if audio { arguments += ["-map", infos[index].hasAudio ? "0:a:0" : "1:a:0", "-af", "aresample=48000,asetpts=PTS-STARTPTS", "-ac", "2", "-ar", "48000"] } else { arguments += ["-an"] }
            arguments += ["-t", decimal(infos[index].duration)] + videoEncoding("mp4", quality: options.quality)
            try render(arguments, to: clip, token: token, duration: infos[index].duration, progress: { value,_ in progress(OperationProgress.map(value, from: Double(index)/Double(inputs.count)*0.95, to: Double(index+1)/Double(inputs.count)*0.95),"Joining clip \(index+1) of \(inputs.count)…") })
            clips.append(clip)
        }
        let list = temp.appendingPathComponent("clips.txt")
        // Generated names are relative to this private directory, never user-controlled shell text.
        try clips.map { "file '\($0.lastPathComponent)'\n" }.joined().write(to: list, atomically: true, encoding: .utf8)
        let out = try outputURL(inputs[0], ext: "mp4", suffix: " Joined", options: options)
        try render(["-f", "concat", "-safe", "1", "-i", list.path, "-map", "0", "-c", "copy", "-movflags", "+faststart"], to: out, token: token, duration: infos.map(\.duration).reduce(0, +), progress: { value,_ in progress(OperationProgress.map(value, from: 0.95, to: 1),"Joining clips…") })
        return [out]
    }

    private static func snapshots(_ input: URL, info: MediaInfo, options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        try requireVideo(info)
        guard !options.snapshotTimes.isEmpty else { throw NashiError.invalidInput("Choose at least one frame to save.") }
        var outputs: [URL] = []
        do {
            for (index, time) in options.snapshotTimes.enumerated() {
                guard time.isFinite, time >= 0, info.duration <= 0 || time < info.duration else { throw NashiError.invalidInput("A snapshot time is outside the video.") }
                let out = try outputURL(input, ext: "png", suffix: " Frame \(index + 1)", options: options)
                try render(["-ss", decimal(time), "-i", input.path, "-map", "0:v:0", "-frames:v", "1", "-update", "1", "-c:v", "png"], to: out, token: token)
                outputs.append(out)
                progress(Double(index+1)/Double(options.snapshotTimes.count),"Saving frame \(index+1) of \(options.snapshotTimes.count)…")
            }
            return outputs
        } catch { outputs.forEach { try? FileManager.default.removeItem(at: $0) }; throw error }
    }

    private static func split(_ input: URL, info: MediaInfo, options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        try requireVideo(info)
        guard info.duration.isFinite, info.duration > 0 else { throw NashiError.invalidInput("Cannot split a video with unknown duration.") }
        var points: [Double]
        if !options.splitPoints.isEmpty {
            guard options.splitPoints.allSatisfy({ $0.isFinite && $0 > 0 && $0 < info.duration }) else { throw NashiError.invalidInput("Split points must fall inside the video.") }
            points = Array(Set(options.splitPoints)).sorted()
        } else if let count = options.equalClips {
            guard count >= 2, count <= 10000 else { throw NashiError.invalidInput("Choose between 2 and 10,000 equal clips.") }
            points = (1..<count).map { info.duration * Double($0) / Double(count) }
        } else {
            guard options.splitInterval.isFinite, options.splitInterval > 0, info.duration / options.splitInterval <= 10000 else { throw NashiError.invalidInput("Enter a positive split interval producing at most 10,000 clips.") }
            points = stride(from: options.splitInterval, to: info.duration - 0.05 + 0.000001, by: options.splitInterval).map { $0 }
        }
        let boundaries = [0] + points + [info.duration]
        guard zip(boundaries, boundaries.dropFirst()).allSatisfy({ $0.1 - $0.0 >= 0.05 - 0.000001 }) else { throw NashiError.invalidInput("Split points must be at least 0.05 seconds apart and from either end.") }
        let discarded = Set(options.discardedClips)
        guard discarded.allSatisfy({ (0..<(boundaries.count - 1)).contains($0) }) else { throw NashiError.invalidInput("A removed clip is outside the current split segments. Restore the clips and try again.") }
        let retained = (0..<(boundaries.count - 1)).filter { !discarded.contains($0) }
        guard !retained.isEmpty else { throw NashiError.invalidInput("Keep at least one clip to export.") }
        if options.joinSplitClips {
            // All retained intervals come from one source and share encoding settings. The
            // concat demuxer preserves their order without an additional lossy encode.
            let temporary = try OutputFiles.temporaryDirectory()
            defer { try? FileManager.default.removeItem(at: temporary) }
            let ext = videoExtension(input)
            var clips: [URL] = []
            for (position, index) in retained.enumerated() {
                var clipOptions = options
                clipOptions.outputDirectory = temporary
                clipOptions.start = boundaries[index]; clipOptions.end = boundaries[index + 1]
                let rendered = try trim(input, info: info, audio: false, options: clipOptions, token: token, suffix: " Part \(index + 1)", progress: { value,_ in progress(OperationProgress.map(value, from: Double(position)/Double(retained.count)*0.95, to: Double(position+1)/Double(retained.count)*0.95),"Saving clip \(position+1) of \(retained.count)…") })[0]
                // Generated names keep concat-file syntax independent of the source name.
                let clip = temporary.appendingPathComponent("segment-\(index)").appendingPathExtension(ext)
                try FileManager.default.moveItem(at: rendered, to: clip)
                clips.append(clip)
            }
            let list = temporary.appendingPathComponent("clips.txt")
            let durations = retained.map { boundaries[$0 + 1] - boundaries[$0] }
            // Container-reported clip durations can include audio encoder padding (notably
            // ASF/WMA). Explicit intervals keep that padding from accumulating at each cut.
            try zip(clips, durations).map { "file '\($0.0.lastPathComponent)'\nduration \(decimal($0.1))\n" }.joined().write(to: list, atomically: true, encoding: .utf8)
            let out = try outputURL(input, ext: ext, suffix: " Split", options: options)
            var arguments = ["-f", "concat", "-safe", "1", "-i", list.path, "-map", "0", "-t", decimal(durations.reduce(0, +)), "-c", "copy"]
            if ["mp4", "mov", "m4v"].contains(ext) { arguments += ["-movflags", "+faststart"] }
            try render(arguments, to: out, token: token, duration: durations.reduce(0, +), progress: { value,_ in progress(OperationProgress.map(value, from: 0.95, to: 1),"Joining selected clips…") })
            return [out]
        }
        let destination=options.outputDirectory ?? input.deletingLastPathComponent()
        try FileManager.default.createDirectory(at:destination,withIntermediateDirectories:true)
        let folder=OutputFiles.unique(for:input,extension:"",suffix:" Clips",directory:destination)
        try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:false)
        var outputs: [URL] = []
        do {
            for (position, index) in retained.enumerated() {
                var clipOptions = options
                clipOptions.outputDirectory=folder
                clipOptions.start = boundaries[index]; clipOptions.end = boundaries[index + 1]
                outputs += try trim(input, info: info, audio: false, options: clipOptions, token: token, suffix: " Part \(index + 1)", progress: { value,_ in progress(OperationProgress.map(value, from: Double(position)/Double(retained.count)*0.95, to: Double(position+1)/Double(retained.count)*0.95),"Saving clip \(position+1) of \(retained.count)…") })
            }
            return outputs
        } catch { try? FileManager.default.removeItem(at:folder);throw error }
    }

    private static func visualization(_ input: URL, info: MediaInfo, options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        try requireAudio(info)
        let width = options.width ?? 1280, height = options.height ?? 720
        guard width >= 16, height >= 16, width <= 8192, height <= 8192 else { throw NashiError.invalidInput("Visualization dimensions must be between 16 and 8,192 pixels.") }
        let w = width / 2 * 2, h = height / 2 * 2
        if ["image", "still", "still image"].contains(options.visualizationStyle.lowercased()) {
            guard let image = options.visualizationImage else { throw NashiError.invalidInput("Choose a still image for the video.") }
            try validateInput(image, token: token)
            let color = try ffmpegColor(options.backgroundColor)
            let out = try outputURL(input, ext: "mp4", suffix: " Visualization", options: options)
            let filter = "scale=\(w):\(h):force_original_aspect_ratio=decrease,pad=\(w):\(h):(ow-iw)/2:(oh-ih)/2:color=\(color),setsar=1"
            // Input options apply only to the next -i, so each input needs its own whitelist.
            let localOnly = ["-protocol_whitelist", "file,pipe,crypto,data"]
            try render(localOnly + ["-loop", "1", "-framerate", "30", "-i", image.path] + localOnly + ["-i", input.path, "-map", "0:v:0", "-map", "1:a:0", "-vf", filter, "-t", decimal(info.duration), "-shortest"] + videoEncoding("mp4", quality: options.quality, sourceAudio: info.audio), to: out, token: token, duration: info.duration, progress: progress)
            return [out]
        }
        let visual: String
        switch options.visualizationStyle.lowercased() {
        case "waveform", "wave": visual = "showwaves=s=\(w)x\(h):mode=cline:rate=30:colors=0xff5200:scale=sqrt:draw=full"
        case "spectrum", "spectrogram": visual = "showspectrum=s=\(w)x\(h):mode=combined:slide=scroll:color=intensity:scale=log:fps=30"
        case "bars", "frequency": visual = "showfreqs=s=\(w)x\(h):mode=bar:ascale=log:fscale=log:colors=0xff830a:rate=30"
        default: throw NashiError.invalidInput("Choose Waveform, Spectrum, Bars, or Still Image visualization.")
        }
        let out = try outputURL(input, ext: "mp4", suffix: " Visualization", options: options)
        let graph = "[0:a:0]asplit[visual][audio];[visual]\(visual),format=yuv420p[video]"
        try render(["-i", input.path, "-filter_complex", graph, "-map", "[video]", "-map", "[audio]", "-t", decimal(info.duration), "-shortest"] + videoEncoding("mp4", quality: options.quality, sourceAudio: info.audio), to: out, token: token, duration: info.duration, progress: progress)
        return [out]
    }

    private static func channels(_ input: URL, info: MediaInfo, options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        try requireAudio(info)
        let filter = try channelFilter(info: info, options: options)
        let ext = audioExtension(input), out = try outputURL(input, ext: audioExtension(input), suffix: options.channels == 1 ? " Mono" : " Stereo", options: options)
        try render(["-i", input.path, "-map", "0:a:0", "-af", filter, "-ac", String(options.channels)] + audioEncoding(ext, quality: options.quality, source: info.audio, channels: options.channels, pcmCodec: try pcmCodec(for: ext, source: info.audio, setting: options.audioSampleDepth), compressionPreset: options.compressionPreset), to: out, token: token, duration: info.duration, progress: progress)
        return [out]
    }

    private static func channelFilter(info: MediaInfo, options: OperationOptions) throws -> String {
        guard options.channels == 1 || options.channels == 2, options.leftVolume.isFinite, options.rightVolume.isFinite, (0...4).contains(options.leftVolume), (0...4).contains(options.rightVolume) else { throw NashiError.invalidInput("Choose mono or stereo and channel levels between 0% and 400%.") }
        let left = decimal(options.leftVolume), right = decimal(options.rightVolume)
        let filter: String
        if info.audio?.channels == 1 {
            filter = options.channels == 1 ? "volume=\(decimal((options.leftVolume + options.rightVolume) / 2))" : "pan=stereo|c0=\(left)*c0|c1=\(right)*c0"
        } else if options.channels == 1 { filter = "aformat=channel_layouts=stereo,pan=mono|c0=\(decimal(options.leftVolume / 2))*c0+\(decimal(options.rightVolume / 2))*c1" }
        else { filter = "aformat=channel_layouts=stereo,pan=stereo|c0=\(left)*c0|c1=\(right)*c1" }
        return filter
    }

    private static func redactVideo(_ input: URL, info: MediaInfo, options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        try requireVideo(info)
        guard let video = info.video, let width = video.width, let height = video.height, !options.redactions.isEmpty || !options.videoRedactions.isEmpty else { throw NashiError.invalidInput("Draw at least one area to redact.") }
        let selections = options.videoRedactions.isEmpty ? options.redactions.map { VideoRedaction(rect: $0, start: options.start, end: options.end) } : options.videoRedactions
        let mode = options.redactionMode.lowercased() == "black" ? "solid" : options.redactionMode.lowercased()
        guard ["blur", "solid", "pixelate", "pixelated"].contains(mode) else { throw NashiError.invalidInput("Choose Solid, Blur, or Pixelate redaction.") }
        var graph: [String] = []
        var previous = "0:v:0"
        for (index, selection) in selections.enumerated() {
            let rect = try redactionPixelRect(selection.rect, width: width, height: height)
            let range = try timeRange(selection.start, selection.end, duration: info.duration)
            let enable = "between(t,\(decimal(range.0)),\(decimal(range.1)))"
            let next = "redacted\(index)"
            if mode == "solid" {
                graph.append("[\(previous)]drawbox=x=\(rect.x):y=\(rect.y):w=\(rect.w):h=\(rect.h):color=\(try ffmpegColor(options.backgroundColor,opaque:true)):t=fill:enable='\(enable)'[\(next)]")
            } else {
                let effect = mode == "blur" ? "gblur=sigma=20:steps=2" : "scale=\(max(1, rect.w / 16)):\(max(1, rect.h / 16)):flags=neighbor,scale=\(rect.w):\(rect.h):flags=neighbor"
                graph.append("[\(previous)]split[base\(index)][crop\(index)];[crop\(index)]crop=\(rect.w):\(rect.h):\(rect.x):\(rect.y):exact=1,\(effect)[effect\(index)];[base\(index)][effect\(index)]overlay=\(rect.x):\(rect.y):enable='\(enable)'[\(next)]")
            }
            previous = next
        }
        let out = try outputURL(input, ext: videoExtension(input), suffix: " Redacted", options: options)
        try render(["-i", input.path, "-filter_complex", graph.joined(separator: ";"), "-map", "[\(previous)]", "-map", "0:a:0?"] + videoEncoding(out.pathExtension, quality: options.quality, copyAudio: true), to: out, token: token, duration: info.duration, progress: progress)
        return [out]
    }

    private static func redactAudio(_ input: URL, info: MediaInfo, options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        try requireAudio(info)
        guard !options.audioRedactions.isEmpty else { throw NashiError.invalidInput("Add at least one bleep range.") }
        let intervals = try options.audioRedactions.map { try timeRange($0.start, $0.end, duration: info.duration) }
        let expression = intervals.map { "between(t,\(decimal($0.0)),\(decimal($0.1)))" }.joined(separator: "+")
        let mode = options.audioMode.lowercased()
        guard ["bleep", "beep", "mute", "silence"].contains(mode) else { throw NashiError.invalidInput("Choose Bleep or Mute audio redaction.") }
        let out = try outputURL(input, ext: audioExtension(input), suffix: mode == "mute" || mode == "silence" ? " Redacted" : " Bleeped", options: options)
        let replacement = mode == "mute" || mode == "silence" ? "0" : "0.18*sin(2*PI*1000*t)"
        let expressions = (0..<(info.audio?.channels ?? 2)).map { "if(\(expression),\(replacement),val(\($0)))" }.joined(separator: "|")
        var arguments = ["-i", input.path, "-map", "0:a:0", "-af", "aeval=exprs='\(expressions)':c=same"]
        arguments += ["-ac", String(info.audio?.channels ?? 2), "-ar", String(info.audio?.sampleRate ?? 48000)] + audioEncoding(out.pathExtension, quality: options.quality, source: info.audio, pcmCodec: try pcmCodec(for: out.pathExtension, source: info.audio, setting: options.audioSampleDepth), compressionPreset: options.compressionPreset)
        try render(arguments, to: out, token: token, duration: info.duration, progress: progress)
        return [out]
    }

    private static func compress(_ input: URL, info: MediaInfo, options: OperationOptions, token: CancellationToken, progress: @escaping ProgressHandler = {_,_ in}) throws -> [URL] {
        guard info.duration.isFinite, info.duration > 0, info.hasVideo || info.hasAudio else { throw NashiError.invalidInput("Cannot compress media without a known duration.") }
        try validateQuality(options.quality)
        let target: Int64
        if let bytes = options.targetBytes {
            guard bytes > 0 else { throw NashiError.invalidInput("Target file size must be positive.") }; target = bytes
        } else if let percent = options.percentage {
            guard percent.isFinite, percent > 0, percent < 100 else { throw NashiError.invalidInput("Percentage reduction must be greater than 0 and less than 100.") }
            target = Int64(Double(info.size) * (1 - percent / 100))
        } else { target = Int64(Double(info.size) * (0.35 + options.quality * 0.4)) }
        guard target >= 2048 else { throw NashiError.invalidInput("The requested size is too small for a media container. Choose at least 2 KB.") }
        let video = info.hasVideo && !audioFormats.contains(input.pathExtension.lowercased())
        let ext = video ? videoExtension(input) : (["mp3", "m4a", "ogg", "opus", "wma", "aac"].contains(input.pathExtension.lowercased()) ? input.pathExtension.lowercased() : "m4a")
        let out = try outputURL(input, ext: ext, suffix: " Compressed", options: options)
        let temp = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: temp) }
        var budget = max(1000, Double(target) * 0.93 - (video ? 2048 : 1024))
        var ownsOutput = false
        do {
            for attempt in 0..<6 {
                progress(OperationProgress.indeterminate,"Compression attempt \(attempt+1)…")
                let attemptProgress: ProgressHandler = { value,label in progress(value,"Compression attempt \(attempt+1) · \(label)") }
                try token.check()
                let totalRate = min(500_000_000, budget * 8 / info.duration)
                let audioRate = info.hasAudio ? min(128000.0, max(8000, totalRate * (video ? 0.18 : 1))) : 0
                var arguments = ["-i", input.path, "-map_metadata", "-1", "-map_chapters", "-1"]
                if video {
                    let videoRate = max(1000, Int(totalRate - audioRate))
                    arguments += ["-map", "0:v:0", "-map", "0:a:0?"]
                    if let scale = try scalingFilter(options) { arguments += ["-vf", scale] }
                    let codec = ext == "webm" ? "libvpx-vp9" : ext == "wmv" ? "wmv2" : ext == "avi" ? "mpeg4" : "libx264"
                    arguments += ["-c:v", codec, "-b:v", String(videoRate), "-pix_fmt", "yuv420p"]
                    if codec == "libx264" { arguments += ["-preset", "medium"] }
                    let log = temp.appendingPathComponent("pass-\(attempt)").path
                    let firstPass = arguments + ["-an", "-pass", "1", "-passlogfile", log, "-f", "null", "/dev/null"]
                    try ffmpeg(firstPass, token: token, overwrite: true, duration: info.duration, progress: { value,_ in attemptProgress(OperationProgress.map(value, from: 0, to: 0.5),"Analyzing video…") })
                    arguments += ["-pass", "2", "-passlogfile", log]
                    if info.hasAudio { arguments += audioEncoding(ext == "webm" ? "opus" : ext == "wmv" ? "wma" : ext == "avi" ? "mp3" : "m4a", quality: options.quality, bitrate: Int(audioRate), source: info.audio) }
                    if ["mp4", "mov", "m4v"].contains(ext) { arguments += ["-movflags", "+faststart"] }
                } else {
                    arguments += ["-map", "0:a:0", "-vn"] + audioEncoding(ext, quality: options.quality, bitrate: Int(audioRate), source: info.audio)
                    // libopus cannot encode at 22.05 kHz; 24 kHz is its nearest supported rate.
                    if audioRate < 24000 { arguments += ["-ac", "1", "-ar", ext == "opus" ? "24000" : "22050"] }
                }
                try render(arguments, to: out, token: token, duration: info.duration, progress: { value,_ in attemptProgress(OperationProgress.map(value, from: video ? 0.5 : 0, to: 1),"Encoding…") })
                ownsOutput = true
                let actual = Int64(try out.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0)
                if actual <= target { return [out] }
                try FileManager.default.removeItem(at: out)
                ownsOutput = false
                budget *= max(0.2, min(0.9, Double(target) / Double(actual) * 0.88))
            }
            throw NashiError.failed("The requested size cannot be reached with this format. Choose a larger target size or a smaller video resolution.")
        } catch { if ownsOutput { try? FileManager.default.removeItem(at: out) }; throw error }
    }

    @discardableResult private static func ffmpeg(_ arguments: [String], token: CancellationToken, verbosity: String = "error", overwrite: Bool = false, duration: Double? = nil, progress: @escaping ProgressHandler = {_,_ in}) throws -> ProcessResult {
        let parser = FFmpegProgressParser(duration: duration, progress: progress)
        return try ProcessRunner.run(Dependencies.executable("ffmpeg"), ["-hide_banner", "-nostdin", "-v", verbosity, overwrite ? "-y" : "-n", "-nostats", "-stats_period", "0.2", "-progress", "pipe:1"] + arguments, token: token, onOutput: { parser.append($0) })
    }

    private static func render(_ arguments: [String], to output: URL, token: CancellationToken, duration: Double? = nil, progress: @escaping ProgressHandler = {_,_ in}) throws {
        guard !FileManager.default.fileExists(atPath: output.path) else { throw NashiError.failed("An output with this name already exists.") }
        let temporary = output.deletingLastPathComponent().appendingPathComponent(".nashi-\(UUID().uuidString)").appendingPathExtension(output.pathExtension)
        defer { try? FileManager.default.removeItem(at: temporary) }
        do {
            do {
                try ffmpeg(arguments + [temporary.path], token: token, duration: duration, progress: progress)
            } catch NashiError.failed(let diagnostic) {
                guard diagnostic.contains("libvorbis"), diagnostic.contains("encoder setup failed"), let fallback = vorbisQualityFallback(arguments) else { throw NashiError.failed(diagnostic) }
                try token.check()
                // Managed Vorbis bitrate ranges depend on both channel count and sample
                // rate. Retry only this initialization failure, preserving that geometry.
                try? FileManager.default.removeItem(at: temporary)
                try ffmpeg(fallback + [temporary.path], token: token, duration: duration, progress: progress)
            }
            try token.check()
            try OutputFiles.validate([temporary])
            // moveItem never replaces an existing file, including one created by another job.
            try FileManager.default.moveItem(at: temporary, to: output)
        } catch { throw error }
    }

    private static func outputURL(_ input: URL, ext: String, suffix: String = "", options: OperationOptions) throws -> URL {
        if let directory = options.outputDirectory { try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true) }
        return OutputFiles.unique(for: input, extension: ext, suffix: suffix, directory: options.outputDirectory)
    }

    private static func audioEncoding(_ ext: String, quality: Double, bitrate: Int? = nil, source: MediaStream? = nil, channels: Int? = nil, pcmCodec: String? = nil, compressionPreset: String = "balanced") -> [String] {
        let rate = max(8000, bitrate ?? 192000)
        let channelCount = max(1, channels ?? source?.channels ?? 2)
        let sampleRate = max(1, source?.sampleRate ?? 48000)
        switch ext {
        case "mp3":
            let maximum = sampleRate <= 12000 ? 64000 : sampleRate < 32000 ? 160000 : 320000
            return ["-c:a", "libmp3lame", "-b:a", String(max(8000, min(maximum, rate / 8000 * 8000)))] + (channelCount > 2 ? ["-ac", "2"] : [])
        case "m4a", "aac": return ["-c:a", "aac", "-b:a", String(min(512000, min(rate, 6 * sampleRate * channelCount)))]
        case "wav": return ["-c:a", pcmCodec ?? "pcm_s16le"]
        case "aiff", "aif": return ["-c:a", pcmCodec ?? "pcm_s16be"]
        case "flac": return ["-c:a", "flac", "-compression_level", compressionPreset == "fast" ? "0" : compressionPreset == "maximum" ? "12" : "8"]
        case "ogg": return ["-c:a", "libvorbis", "-b:a", String(max(32000, min(320000, rate)))]
        case "opus":
            let encoderRate = [48000, 24000, 16000, 12000, 8000].contains(sampleRate) ? sampleRate : 48000
            return ["-c:a", "libopus", "-b:a", String(min(512000, min(rate, 256000 * channelCount))), "-ar", String(encoderRate)]
        case "wma": return ["-c:a", "wmav2", "-b:a", String(max(24000, min(320000, rate))), "-ar", String(max(8000, min(48000, sampleRate)))] + (channelCount > 2 ? ["-ac", "2"] : [])
        default: return ["-c:a", "aac", "-b:a", String(rate)]
        }
    }

    private static func vorbisQualityFallback(_ arguments: [String]) -> [String]? {
        guard let codec = arguments.firstIndex(of: "-c:a"), arguments.indices.contains(codec + 1), arguments[codec + 1] == "libvorbis",
              let bitrate = arguments.firstIndex(of: "-b:a"), arguments.indices.contains(bitrate + 1), let rate = Double(arguments[bitrate + 1]) else { return nil }
        let levels: [(Double, Double)] = [(32000, -1), (64000, 0), (80000, 1), (96000, 2), (112000, 3), (128000, 4), (160000, 5), (192000, 6), (224000, 7), (256000, 8), (320000, 9)]
        var quality = levels.last!.1
        if rate <= levels[0].0 { quality = levels[0].1 }
        else {
            for (lower, upper) in zip(levels, levels.dropFirst()) where rate <= upper.0 {
                quality = lower.1 + (upper.1 - lower.1) * (rate - lower.0) / (upper.0 - lower.0)
                break
            }
        }
        var fallback = arguments
        fallback[bitrate] = "-q:a"; fallback[bitrate + 1] = decimal(quality)
        return fallback
    }

    private static func videoEncoding(_ ext: String, quality: Double, copyAudio: Bool = false, audioBitrate: Int = 192000, sourceAudio: MediaStream? = nil) -> [String] {
        let crf = String(Int((1 - min(1, max(0, quality))) * 25 + 15))
        var arguments: [String]
        let audioFormat: String
        switch ext {
        case "webm": arguments = ["-c:v", "libvpx-vp9", "-crf", crf, "-b:v", "0", "-deadline", "good", "-cpu-used", "2"]; audioFormat = "opus"
        case "avi": arguments = ["-c:v", "mpeg4", "-q:v", String(max(2, Int((1 - quality) * 20 + 2)))]; audioFormat = "mp3"
        case "wmv": arguments = ["-c:v", "wmv2", "-q:v", String(max(2, Int((1 - quality) * 20 + 2)))]; audioFormat = "wma"
        default: arguments = ["-c:v", "libx264", "-preset", "medium", "-crf", crf]; audioFormat = "m4a"
        }
        arguments += ["-pix_fmt", "yuv420p"]
        arguments += copyAudio ? ["-c:a", "copy"] : audioEncoding(audioFormat, quality: quality, bitrate: audioBitrate, source: sourceAudio)
        if ["mp4", "mov", "m4v"].contains(ext) { arguments += ["-movflags", "+faststart"] }
        return arguments
    }

    private static func scalingFilter(_ options: OperationOptions) throws -> String? {
        if let width = options.width, width < 2 || width > 16384 { throw NashiError.invalidInput("Width must be between 2 and 16,384 pixels.") }
        if let height = options.height, height < 2 || height > 16384 { throw NashiError.invalidInput("Height must be between 2 and 16,384 pixels.") }
        var filter: String
        if let width = options.width, let height = options.height {
            let w = width / 2 * 2, h = height / 2 * 2
            filter = "scale=\(w):\(h):force_original_aspect_ratio=decrease,pad=\(w):\(h):(ow-iw)/2:(oh-ih)/2,setsar=1"
        }
        else if let width = options.width { filter = "scale=\(width / 2 * 2):-2,setsar=1" }
        else if let height = options.height { filter = "scale=-2:\(height / 2 * 2),setsar=1" }
        else { filter = "pad=ceil(iw/2)*2:ceil(ih/2)*2" }
        if options.videoMaxHeight > 0 { filter += ",scale=-2:'trunc(min(\(options.videoMaxHeight),ih)/2)*2',setsar=1" }
        return filter
    }

    private static func pcmCodec(for ext: String, source: MediaStream?, setting: String) throws -> String? {
        guard ["wav", "aiff", "aif"].contains(ext) else { return nil }
        let endian = ext == "wav" ? "le" : "be"
        switch setting {
        case "pcm16", "bit16": return "pcm_s16\(endian)"
        case "pcm24", "bit24": return "pcm_s24\(endian)"
        case "pcm32", "bit32": return "pcm_s32\(endian)"
        case "float32": return "pcm_f32\(endian)"
        case "matchSource":
            let codec = source?.codec ?? ""
            if codec.hasPrefix("pcm_f") {
                let depth = Int(codec.dropFirst(5).prefix(while: \.isNumber)) ?? 32
                return "pcm_f\(depth > 32 ? 64 : 32)\(endian)"
            }
            let rawDepth = [source?.bitsPerRawSample, source?.bitsPerSample].compactMap { $0 }.first { $0 > 0 }
            let codecDepth = codec.hasPrefix("pcm_s") || codec.hasPrefix("pcm_u") ? Int(codec.dropFirst(5).prefix(while: \.isNumber)) : nil
            let depth = codec == "pcm_alaw" || codec == "pcm_mulaw" ? 16 : (rawDepth ?? codecDepth ?? 16)
            guard depth <= 32 else { throw NashiError.invalidInput("This source uses more than 32-bit integer audio. Choose an output bit depth in settings.") }
            if depth <= 8 { return ext == "wav" ? "pcm_u8" : "pcm_s8" }
            return "pcm_s\(depth <= 16 ? 16 : depth <= 24 ? 24 : 32)\(endian)"
        default: throw NashiError.invalidInput("Choose Match Source, 16-bit, 24-bit, 32-bit integer, or 32-bit float audio.")
        }
    }

    private static func tempoFilter(_ speed: Double) -> String {
        var rate = speed, factors: [Double] = []
        while rate > 2 { factors.append(2); rate /= 2 }
        while rate < 0.5 { factors.append(0.5); rate /= 0.5 }
        factors.append(rate)
        return factors.map { "atempo=\(decimal($0))" }.joined(separator: ",")
    }

    private static func validateInput(_ url: URL, token: CancellationToken) throws {
        try token.check()
        guard url.isFileURL, FileManager.default.isReadableFile(atPath: url.path), (try? url.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true else { throw NashiError.invalidInput("Choose a readable local media file.") }
    }
    private static func requireAudio(_ info: MediaInfo) throws { guard info.hasAudio else { throw NashiError.invalidInput("The file has no audio track.") } }
    private static func requireVideo(_ info: MediaInfo) throws { guard info.hasVideo else { throw NashiError.invalidInput("The file has no video track.") } }
    private static func validateQuality(_ quality: Double) throws { guard quality.isFinite, (0...1).contains(quality) else { throw NashiError.invalidInput("Quality must be between 0 and 1.") } }
    private static func validateMediaPreferences(_ options: OperationOptions) throws {
        guard [128, 192, 256, 320].contains(options.audioBitrate) else { throw NashiError.invalidInput("Choose an audio bitrate of 128, 192, 256, or 320 kbps.") }
        guard ["matchSource", "pcm16", "pcm24", "pcm32", "float32", "bit16", "bit24", "bit32"].contains(options.audioSampleDepth) else { throw NashiError.invalidInput("Choose a supported audio sample depth.") }
        guard options.videoMaxHeight == 0 || (2...16384).contains(options.videoMaxHeight) else { throw NashiError.invalidInput("Maximum video height must be Original or between 2 and 16,384 pixels.") }
        guard (1...60).contains(options.gifFrameRate), (2...16384).contains(options.gifMaxWidth) else { throw NashiError.invalidInput("GIF frame rate must be 1–60 and maximum width 2–16,384 pixels.") }
        guard ["fast", "balanced", "maximum"].contains(options.compressionPreset) else { throw NashiError.invalidInput("Choose Fast, Balanced, or Maximum compression.") }
        guard options.silenceThresholdDb.isFinite, (-80 ... -10).contains(options.silenceThresholdDb) else { throw NashiError.invalidInput("Choose a silence threshold between −80 and −10 dB.") }
    }
    private static func validateNormalization(target: Double, range: Double, peak: Double) throws {
        guard target.isFinite, (-70 ... -5).contains(target), range.isFinite, (1...50).contains(range), peak.isFinite, (-9...0).contains(peak) else { throw NashiError.invalidInput("Loudness must be −70 to −5 LUFS, range 1 to 50 LU, and true peak −9 to 0 dBTP.") }
    }
    private static func timeRange(_ start: Double, _ end: Double?, duration: Double) throws -> (Double, Double) {
        let finish = end ?? duration
        guard start.isFinite, finish.isFinite, start >= 0, finish > start, duration <= 0 || (start < duration && finish <= duration + 0.001) else { throw NashiError.invalidInput("The start and end times must describe a nonempty range inside the media.") }
        return (start, min(finish, duration > 0 ? duration : finish))
    }
    private static func pixelRect(_ rect: EditRect, width: Int, height: Int) throws -> (x: Int, y: Int, w: Int, h: Int) {
        guard [rect.x, rect.y, rect.width, rect.height].allSatisfy(\.isFinite), rect.x >= 0, rect.y >= 0, rect.width >= 2, rect.height >= 2, rect.x + rect.width <= Double(width) + 0.01, rect.y + rect.height <= Double(height) + 0.01 else { throw NashiError.invalidInput("The selected rectangle must be inside the video and at least 2 × 2 pixels.") }
        return (Int(rect.x), Int(rect.y), Int(rect.width), Int(rect.height))
    }
    private static func redactionPixelRect(_ rect: EditRect, width: Int, height: Int) throws -> (x: Int, y: Int, w: Int, h: Int) {
        // Redaction areas only need a positive size: the canvas keeps thin drags and
        // 1 px resizes, and rejecting one area would fail the whole export.
        guard [rect.x, rect.y, rect.width, rect.height].allSatisfy(\.isFinite), rect.x >= 0, rect.y >= 0, rect.width > 0, rect.height > 0, rect.x + rect.width <= Double(width) + 0.01, rect.y + rect.height <= Double(height) + 0.01 else { throw NashiError.invalidInput("Choose a rectangle inside the video.") }
        // Cover every pixel touched by the selection, matching image redaction.
        // Truncating origin and size separately can leave the far edge visible.
        let x=Int(floor(rect.x)),y=Int(floor(rect.y))
        let right=min(width,Int(ceil(rect.x+rect.width))),bottom=min(height,Int(ceil(rect.y+rect.height)))
        guard right>x,bottom>y else { throw NashiError.invalidInput("Choose a rectangle inside the video.") }
        return (x,y,right-x,bottom-y)
    }
    private static func audioExtension(_ input: URL) -> String { let ext = input.pathExtension.lowercased(); return audioFormats.contains(ext) ? ext : "m4a" }
    private static func videoExtension(_ input: URL) -> String { let ext = input.pathExtension.lowercased(); return videoFormats.contains(ext) && ext != "gif" ? ext : "mp4" }
    private static func ffmpegColor(_ color: String, opaque: Bool = false) throws -> String {
        let hex = color.hasPrefix("#") ? String(color.dropFirst()) : color
        guard [6, 8].contains(hex.count), hex.allSatisfy(\.isHexDigit) else { throw NashiError.invalidInput("Use a six- or eight-digit hexadecimal color.") }
        return "0x" + (opaque ? String(hex.prefix(6)) : hex)
    }
    private static func metadataArguments(_ metadata: [String: String]) -> [String] {
        metadata.sorted { $0.key < $1.key }.flatMap { ["-metadata", "\($0.key)=\($0.value)"] }
    }
    private static func metadataSpecifier(_ key: String) throws -> (String, String) {
        if key.hasPrefix("file:") { return ("-metadata", String(key.dropFirst(5))) }
        for (prefix, scope) in [("track[", "s"), ("chapter[", "c")] where key.hasPrefix(prefix) {
            guard let close = key.range(of: "]:"), let index = Int(key[key.index(key.startIndex, offsetBy: prefix.count)..<close.lowerBound]), index >= 0 else { throw NashiError.invalidInput("Invalid metadata location: \(key)") }
            return ("-metadata:\(scope):\(index)", String(key[close.upperBound...]))
        }
        return ("-metadata", key)
    }
    private static func decimal(_ value: Double) -> String { String(format: "%.8f", locale: Locale(identifier: "en_US_POSIX"), value).replacingOccurrences(of: #"\.?0+$"#, with: "", options: .regularExpression) }
    private static func number(_ value: Any?) -> Double? {
        if let n = value as? NSNumber { return n.doubleValue }
        if let s = value as? String { return Double(s) }
        return nil
    }
    private static func integer(_ value: Any?) -> Int? { number(value).flatMap { $0.isFinite && $0 >= Double(Int.min) && $0 < Double(Int.max) ? Int($0) : nil } }
    private static func fraction(_ value: String?) -> Double? {
        guard let value else { return nil }; let parts = value.split(separator: "/").compactMap { Double($0) }
        let result = parts.count == 2 && parts[1] != 0 ? parts[0] / parts[1] : Double(value)
        return result.flatMap { $0.isFinite && $0 > 0 ? $0 : nil }
    }
    private static func tags(_ value: Any?) -> [String: String] {
        guard let raw = value as? [String: Any] else { return [:] }; return raw.mapValues { String(describing: $0) }
    }
}
