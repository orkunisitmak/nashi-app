using System.Globalization;
using System.Text.Json;

namespace Nashi.Core;

public sealed record MediaStream(int Index, string Kind, string Codec, int? Width, int? Height, bool AttachedPicture,int Bits=0,string SampleFormat="",double FrameRate=30,int SampleRate=0);
public sealed record MediaInfo(double Duration, long Bytes, IReadOnlyList<MediaStream> Streams)
{
    public MediaStream? Video => Streams.FirstOrDefault(s => s.Kind == "video" && !s.AttachedPicture);
    public MediaStream? Audio => Streams.FirstOrDefault(s => s.Kind == "audio");
    public bool HasAudio => Streams.Any(s => s.Kind == "audio");
}
public sealed record MediaRegion(double X, double Y, double Width, double Height, double Start = 0, double? End = null);
public sealed record MediaRange(double Start, double End);
public sealed record MediaOptions
{
    public double Quality { get; init; } = .8;
    public int AudioBitrate { get; init; } = 256;
    public string SampleDepth{get;init;}="matchSource";public int GifFrameRate{get;init;}=15;public int GifWidth{get;init;}=960;
    public double Start { get; init; }
    public double? End { get; init; }
    public double Speed { get; init; } = 1;
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int Channels { get; init; } = 2;
    public double LeftVolume { get; init; } = 1;
    public double RightVolume { get; init; } = 1;
    public double TargetLoudness { get; init; } = -16;
    public double LoudnessRange { get; init; } = 11;
    public double TruePeak { get; init; } = -1.5;
    public long? TargetBytes { get; init; }
    public double? ReductionPercent { get; init; }
    public bool PreserveMetadata { get; init; } = true;
    public bool TrimSilence { get; init; }
    public double[] SnapshotTimes { get; init; } = [0];
    public double[] SplitPoints { get; init; } = [];
    public int? EqualClips { get; init; }
    public double SplitInterval { get; init; } = 30;
    public int[] DiscardedClips { get; init; } = [];
    public bool JoinSplitClips { get; init; }
    public string Visualization { get; init; } = "waveform";
    public string? VisualizationImage { get; init; }
    public string RedactionMode { get; init; } = "solid";
    public MediaRegion[] Regions { get; init; } = [];
    public MediaRange[] AudioRedactions { get; init; } = [];
    public bool RemoveAllMetadata{get;init;}=true;
    public Dictionary<string,string> Metadata { get; init; } = [];
}

/// File-only FFmpeg operations. The UI and CLI call the same transaction and
/// argument-building paths; no filename, option or metadata value enters a shell.
public sealed class MediaEngine(string ffmpeg, string ffprobe)
{
    public static readonly HashSet<string> AudioFormats = new(StringComparer.OrdinalIgnoreCase)
        { "mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma", "aac" };
    public static readonly HashSet<string> VideoFormats = new(StringComparer.OrdinalIgnoreCase)
        { "mp4", "mov", "mkv", "webm", "avi", "wmv", "gif", "m4v" };
    public static readonly HashSet<string> Tools = new(StringComparer.Ordinal)
        { "compress", "removeMetadata", "normalizeAudio", "muteVideo", "trimVideo", "cropVideo", "changeVideoSpeed", "joinVideos", "videoSnapshots", "splitVideo", "audioToVideo", "trimAudio", "audioChannels", "redactVideo", "redactAudio" };

    public async Task<MediaInfo> InspectAsync(string input, CancellationToken token = default)
    {
        if (!File.Exists(input)) throw new FileNotFoundException("Choose an existing media file.");
        var result = await ProcessRunner.RunAsync(ffprobe, ["-v", "error", "-show_format", "-show_streams", "-of", "json", Path.GetFullPath(input)], token);
        using var json = JsonDocument.Parse(result.Output); var root = json.RootElement;
        var streams = new List<MediaStream>();
        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            var attached = stream.TryGetProperty("disposition", out var disposition) && disposition.TryGetProperty("attached_pic", out var pic) && pic.GetInt32() == 1;
            var width=GetInt(stream,"width");var height=GetInt(stream,"height");double rotation=0;
            if(stream.TryGetProperty("side_data_list",out var sideData)&&sideData.ValueKind==JsonValueKind.Array)
                foreach(var entry in sideData.EnumerateArray())if(entry.TryGetProperty("rotation",out var angle)&&angle.TryGetDouble(out var value))rotation=value;
            if(rotation==0&&stream.TryGetProperty("tags",out var tags)&&tags.TryGetProperty("rotate",out var tag))double.TryParse(tag.ToString(),CultureInfo.InvariantCulture,out rotation);
            // FFmpeg auto-rotates decoded frames before crop/scale filters. The
            // editor and validation use those same displayed dimensions.
            if(double.IsFinite(rotation)&&Math.Abs(Math.Abs(rotation)%180-90)<.001)(width,height)=(height,width);
            streams.Add(new(stream.GetProperty("index").GetInt32(), GetString(stream,"codec_type"), GetString(stream,"codec_name"), width, height, attached,GetInt(stream,"bits_per_raw_sample")??GetInt(stream,"bits_per_sample")??0,GetString(stream,"sample_fmt"),ParseFrameRate(GetString(stream,"avg_frame_rate")),int.TryParse(GetString(stream,"sample_rate"),NumberStyles.Integer,CultureInfo.InvariantCulture,out var sampleRate)&&sampleRate>0?sampleRate:0));
        }
        var duration = root.TryGetProperty("format", out var format) && double.TryParse(GetString(format,"duration"), CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) ? Math.Max(0,seconds) : 0;
        return new(duration, new FileInfo(input).Length, streams);
    }

    private static double ParseFrameRate(string value){var parts=value.Split('/');if(parts.Length==2&&double.TryParse(parts[0],CultureInfo.InvariantCulture,out var numerator)&&double.TryParse(parts[1],CultureInfo.InvariantCulture,out var denominator)&&denominator>0&&double.IsFinite(numerator/denominator)&&numerator/denominator>=1)return numerator/denominator;return 30;}

    public async Task<IReadOnlyList<string>> ConvertAsync(IReadOnlyList<string> inputs, string format, string destination,
        MediaOptions options, IProgress<OperationProgress>? progress = null, CancellationToken token = default)
    {
        if (inputs.Count == 0 || (!AudioFormats.Contains(format) && !VideoFormats.Contains(format))) throw new ArgumentException("Choose files and a supported media format.");
        Validate(options);
        using var transaction = new ExportTransaction(destination); var outputs = new List<string>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = Path.GetFullPath(inputs[index]); var info = await InspectAsync(input,token);
            var output = Output(transaction.StagingDirectory,input,format,"");
            var arguments = new List<string> { "-i",input };
            if (AudioFormats.Contains(format))
            { RequireAudio(info); arguments.AddRange(["-map","0:a:0","-vn"]); arguments.AddRange(AudioEncoding(format,SourceDepth(options,info))); }
            else
            {
                RequireVideo(info);
                if (format == "gif") arguments.AddRange(["-filter_complex",$"[0:v]fps={options.GifFrameRate},scale=w='min({options.GifWidth},iw)':h=-2,split[p0][p1];[p0]palettegen[pal];[p1][pal]paletteuse=dither=sierra2_4a","-an","-loop","0"]);
                else
                {
                    arguments.AddRange(["-map","0:v:0","-map","0:a:0?"]);
                    if (options.Width is not null || options.Height is not null) arguments.AddRange(["-vf",Scale(options)]);
                    arguments.AddRange(VideoEncoding(format,options));
                }
            }
            arguments.AddRange(options.PreserveMetadata ? ["-map_metadata","0"] : ["-map_metadata","-1","-map_metadata:s","-1","-map_chapters","-1"]);
            await RenderAsync(arguments,output,info.Duration,Scoped(progress,index,inputs.Count),token);
            outputs.Add(output);
        }
        progress?.Report(new(.98,"Saving your files…"));
        var published = transaction.Publish(outputs,token); progress?.Report(new(1,"Done")); return published;
    }

    public async Task<IReadOnlyList<string>> ToolAsync(string tool,IReadOnlyList<string> inputs,string destination,
        MediaOptions options,IProgress<OperationProgress>? progress = null,CancellationToken token = default)
    {
        if (inputs.Count == 0 || !Tools.Contains(tool)) throw new ArgumentException("Choose media and a supported tool.");
        Validate(options);
        using var transaction = new ExportTransaction(destination); var outputs = new List<string>();
        if (tool == "joinVideos") outputs.Add(await JoinAsync(inputs,transaction.StagingDirectory,options,progress,token));
        else for (var index=0;index<inputs.Count;index++)
        {
            var input=Path.GetFullPath(inputs[index]);var info=await InspectAsync(input,token);var report=Scoped(progress,index,inputs.Count);
            outputs.AddRange(await ApplyAsync(tool,input,info,transaction.StagingDirectory,options,report,token));
        }
        progress?.Report(new(.98,"Saving your files…"));
        var published=transaction.Publish(outputs,token);progress?.Report(new(1,"Done"));return published;
    }

    private async Task<IReadOnlyList<string>> ApplyAsync(string tool,string input,MediaInfo info,string directory,MediaOptions options,Action<OperationProgress> progress,CancellationToken token)
    {
        options=SourceDepth(options,info);progress(new(null,"Preparing "+Path.GetFileName(input)+"…"));
        var ext=Path.GetExtension(input).TrimStart('.').ToLowerInvariant();if(ext=="aif")ext="aiff";
        var output=Output(directory,input,ext," "+ToolSuffix(tool));
        var args=new List<string>{"-i",input};double? duration=info.Duration;
        switch(tool)
        {
            case "muteVideo": RequireVideo(info);args.AddRange(["-map","0:v:0","-c:v","copy","-an"]);break;
            case "removeMetadata":
                if(options.RemoveAllMetadata)
                {
                    args.AddRange(["-map","0","-map","-0:d?","-c","copy","-map_metadata","-1","-map_metadata:s","-1","-map_chapters","-1","-fflags","+bitexact"]);
                    foreach(var stream in info.Streams.Where(s=>s.AttachedPicture))args.AddRange(["-map","-0:"+stream.Index]);
                }
                else args.AddRange(["-map","0","-c","copy","-map_metadata","0","-map_chapters","0"]);
                foreach(var pair in options.Metadata)
                {
                    if(string.IsNullOrWhiteSpace(pair.Key)||pair.Key.Contains('=')||pair.Key.Contains('\0')||pair.Value.Contains('\0'))throw new ArgumentException("Invalid metadata field.");
                    var key=pair.Key;var option="-metadata";var parts=key.Split(':');if(parts.Length==3&&parts[0] is "stream" or "chapter")
                    {if(!int.TryParse(parts[1],out var number)||number<0||number>10000||parts[0]=="stream"&&!info.Streams.Any(s=>s.Index==number))throw new ArgumentException("Invalid metadata stream/chapter.");option+=parts[0]=="stream"?":s:"+number:":c:"+number;key=parts[2];if(key is not ("title" or "language" or "handler_name" or "comment"))throw new ArgumentException("Unsupported stream/chapter tag.");}
                    args.AddRange([option,key+"="+pair.Value]);if(option=="-metadata"&&ext is "ogg" or "opus")args.AddRange(["-metadata:s:a:0",key+"="+pair.Value]);
                }
                break;
            case "trimVideo":case "trimAudio":
                if(tool=="trimAudio")RequireAudio(info);else RequireVideo(info);
                var range=TimeRange(options.Start,options.End,info.Duration);duration=range.End-range.Start;
                args.AddRange(["-ss",Number(range.Start),"-t",Number(duration.Value)]);
                if(tool=="trimAudio")
                {
                    args.AddRange(["-map","0:a:0","-vn"]);
                    if(options.TrimSilence){args.AddRange(["-af","silenceremove=start_periods=1:start_duration=0.02:start_threshold=-50dB,areverse,silenceremove=start_periods=1:start_duration=0.02:start_threshold=-50dB,areverse"]);duration=null;}
                    args.AddRange(AudioEncoding(ext,options));
                }
                else args.AddRange(["-map","0:v:0","-map","0:a:0?",..VideoEncoding(ext,options)]);
                break;
            case "cropVideo":
                RequireVideo(info);if(options.Regions.Length!=1)throw new ArgumentException("Select one crop rectangle.");
                var crop=PixelRect(options.Regions[0],info);
                args.AddRange(["-map","0:v:0","-map","0:a:0?","-vf",$"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}:exact=1,pad=ceil(iw/2)*2:ceil(ih/2)*2",..VideoEncoding(ext,options)]);break;
            case "changeVideoSpeed":
                RequireVideo(info);duration=info.Duration/options.Speed;
                args.AddRange(["-map","0:v:0","-map","0:a:0?","-vf","setpts=(PTS-STARTPTS)/"+Number(options.Speed)]);
                if(info.HasAudio)args.AddRange(["-af",Tempo(options.Speed)]);
                args.AddRange(VideoEncoding(ext,options));break;
            case "audioChannels":
                RequireAudio(info);var filter=options.Channels==1?$"aformat=channel_layouts=stereo,pan=mono|c0={Number(options.LeftVolume/2)}*c0+{Number(options.RightVolume/2)}*c1":$"aformat=channel_layouts=stereo,pan=stereo|c0={Number(options.LeftVolume)}*c0|c1={Number(options.RightVolume)}*c1";
                args.AddRange(["-map","0:a:0","-vn","-af",filter,"-ac",options.Channels.ToString(CultureInfo.InvariantCulture),..AudioEncoding(ext,options)]);break;
            case "normalizeAudio":return [await NormalizeAsync(input,info,directory,ext,options,progress,token)];
            case "compress":return [await CompressAsync(input,info,directory,options,progress,token)];
            case "videoSnapshots":
                RequireVideo(info);if(options.SnapshotTimes.Length==0)throw new ArgumentException("Choose at least one frame.");var frames=new List<string>();
                for(var n=0;n<options.SnapshotTimes.Length;n++)
                {
                    var time=options.SnapshotTimes[n];if(!double.IsFinite(time)||time<0||(info.Duration>0&&time>=info.Duration))throw new ArgumentException("A frame lies outside the video.");
                    var file=Output(directory,input,"png"," Frame "+(n+1));
                    await RenderAsync(["-ss",Number(time),"-i",input,"-map","0:v:0","-frames:v","1","-update","1","-c:v","png"],file,null,progress,token);frames.Add(file);
                    progress(new((double)(n+1)/options.SnapshotTimes.Length,"Saving frame "+(n+1)));
                }
                return frames;
            case "splitVideo":return await SplitAsync(input,info,directory,options,progress,token);
            case "audioToVideo":
                RequireAudio(info);output=Output(directory,input,"mp4"," Visualization");
                var w=options.Width??1280;var h=options.Height??720;
                if(options.Visualization=="image")
                {
                    if(options.VisualizationImage is null||!File.Exists(options.VisualizationImage))throw new ArgumentException("Choose an image.");
                    // Input options apply only to the next -i, so each input needs its own whitelist.
                    string[] localOnly=["-protocol_whitelist","file,pipe,crypto,data"];
                    args=[..localOnly,"-loop","1","-framerate","30","-i",options.VisualizationImage,..localOnly,"-i",input,"-map","0:v:0","-map","1:a:0","-vf",$"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1"];
                }
                else
                {
                    var visual=options.Visualization switch{"spectrum"=>$"showspectrum=s={w}x{h}:slide=scroll:color=intensity:fps=30","bars"=>$"showfreqs=s={w}x{h}:mode=bar:ascale=log:fscale=log:rate=30",_=>$"showwaves=s={w}x{h}:mode=cline:rate=30:colors=0xe4bd59"};
                    args.AddRange(["-filter_complex",$"[0:a]asplit[v][a];[v]{visual},format=yuv420p[video]","-map","[video]","-map","[a]"]);
                }
                args.AddRange(["-t",Number(info.Duration),"-shortest",..VideoEncoding("mp4",options)]);break;
            case "redactAudio":
                RequireAudio(info);if(options.AudioRedactions.Length==0)throw new ArgumentException("Add a bleep or silence range.");
                var ranges=options.AudioRedactions.Select(r=>TimeRange(r.Start,r.End,info.Duration)).ToList();
                var expression=string.Join('+',ranges.Select(r=>$"between(t,{Number(r.Start)},{Number(r.End)})"));
                // One channel-independent expression; c=same keeps the input layout,
                // otherwise aeval derives a mono layout from the single expression.
                var replacement=options.RedactionMode=="mute"?"0":"0.18*sin(2*PI*1000*t)";
                args.AddRange(["-map","0:a:0","-af",$"aeval=exprs='if({expression},{replacement},val(ch))':c=same",..AudioEncoding(ext,options)]);break;
            case "redactVideo":
                RequireVideo(info);if(options.Regions.Length==0)throw new ArgumentException("Draw a region to redact.");
                var graph=new List<string>();var previous="0:v:0";
                for(var n=0;n<options.Regions.Length;n++)
                {
                    var region=options.Regions[n];var rect=PixelRect(region,info);var active=TimeRange(region.Start,region.End,info.Duration);var enabled=$"between(t,{Number(active.Start)},{Number(active.End)})";var next="r"+n;
                    if(options.RedactionMode=="solid")graph.Add($"[{previous}]drawbox=x={rect.X}:y={rect.Y}:w={rect.Width}:h={rect.Height}:color=black:t=fill:enable='{enabled}'[{next}]");
                    else
                    {
                        var effect=options.RedactionMode=="blur"?"gblur=sigma=20:steps=2":$"scale={Math.Max(1,rect.Width/16)}:{Math.Max(1,rect.Height/16)}:flags=neighbor,scale={rect.Width}:{rect.Height}:flags=neighbor";
                        graph.Add($"[{previous}]split[b{n}][c{n}];[c{n}]crop={rect.Width}:{rect.Height}:{rect.X}:{rect.Y}:exact=1,{effect}[e{n}];[b{n}][e{n}]overlay={rect.X}:{rect.Y}:enable='{enabled}'[{next}]");
                    }
                    previous=next;
                }
                args.AddRange(["-filter_complex",string.Join(';',graph),"-map","["+previous+"]","-map","0:a:0?",..VideoEncoding(ext,options)]);break;
            default:throw new ArgumentException("Unsupported media tool.");
        }
        await RenderAsync(args,output,duration,progress,token);return [output];
    }

    private async Task<string> NormalizeAsync(string input,MediaInfo info,string directory,string ext,MediaOptions options,Action<OperationProgress> progress,CancellationToken token)
    {
        RequireAudio(info);var filter=$"loudnorm=I={Number(options.TargetLoudness)}:LRA={Number(options.LoudnessRange)}:TP={Number(options.TruePeak)}";
        var parser=new FFmpegProgress(info.Duration,p=>progress(p with{Fraction=p.Fraction*.45,Label="Measuring loudness…"}),"Measuring loudness…");
        var measured=await ProcessRunner.RunAsync(ffmpeg,["-hide_banner","-nostdin","-v","info","-nostats","-progress","pipe:1","-i",input,"-map","0:a:0","-af",filter+":print_format=json","-f","null",OperatingSystem.IsWindows()?"NUL":"/dev/null"],token,parser.ReadLine);
        var start=measured.Error.LastIndexOf('{');var end=measured.Error.LastIndexOf('}');
        if(start>=0&&end>start)
        {
            using var doc=JsonDocument.Parse(measured.Error[start..(end+1)]);var values=doc.RootElement;
            var fields=new[]{("input_i","measured_I"),("input_tp","measured_TP"),("input_lra","measured_LRA"),("input_thresh","measured_thresh"),("target_offset","offset")};
            var numbers=fields.Select(f=>(f.Item2,double.TryParse(GetString(values,f.Item1),CultureInfo.InvariantCulture,out var v)?v:double.NaN)).ToList();
            if(numbers.All(x=>double.IsFinite(x.Item2)))filter+=string.Concat(numbers.Select(x=>":"+x.Item1+"="+Number(x.Item2)))+":linear=true";
        }
        var output=Output(directory,input,ext," Normalized");
        await RenderAsync(["-i",input,"-map","0:a:0","-af",filter,..NormalizedRate(ext,info),..AudioEncoding(ext,options)],output,info.Duration,p=>progress(p with{Fraction=.45+p.Fraction*.55}),token);return output;
    }
    // loudnorm resamples to 192 kHz internally. Lossless and Vorbis outputs keep the source rate (as on macOS);
    // MP3/AAC/WMA encoders top out at 48 kHz, and Opus is always 48 kHz, so FFmpeg picks the rate for Opus.
    private static string[] NormalizedRate(string ext,MediaInfo info)
    {
        var source=info.Audio is {SampleRate:>0} audio?audio.SampleRate:48000;
        int? rate=ext switch{"wav" or "aiff" or "flac"=>source,"ogg"=>Math.Min(source,192000),"mp3" or "m4a" or "aac" or "wma"=>Math.Min(source,48000),_=>null};
        return rate is {} value?["-ar",value.ToString(CultureInfo.InvariantCulture)]:[];
    }

    private async Task<string> CompressAsync(string input,MediaInfo info,string directory,MediaOptions options,Action<OperationProgress> progress,CancellationToken token)
    {
        if(info.Duration<=0)throw new ArgumentException("Compression requires a known duration.");
        var target=options.TargetBytes??(long)(info.Bytes*(options.ReductionPercent is {} reduction?1-reduction/100:.35+options.Quality*.4));
        if(target<2048)throw new ArgumentException("Choose a target of at least 2 KB.");
        var video=info.Video is not null;var ext=video?"mp4":"m4a";var output=Output(directory,input,ext," Compressed");
        var budget=target*.9;
        for(var attempt=0;attempt<6;attempt++)
        {
            token.ThrowIfCancellationRequested();progress(new(null,$"Compression attempt {attempt+1}…"));
            var total=budget*8/info.Duration;var audioRate=info.HasAudio?Math.Clamp(total*(video?.18:1),8000,128000):0;
            var args=new List<string>{"-i",input,"-map_metadata","-1","-map_chapters","-1"};
            if(video)
            {
                var log=Path.Combine(directory,"pass-"+Guid.NewGuid().ToString("N"));
                args.AddRange(["-map","0:v:0","-map","0:a:0?","-c:v","libx264","-preset","medium","-b:v",((long)Math.Max(1000,total-audioRate)).ToString(CultureInfo.InvariantCulture),"-pix_fmt","yuv420p"]);
                var first=new FFmpegProgress(info.Duration,p=>progress(p with{Fraction=p.Fraction*.5,Label=$"Compression {attempt+1} · analyzing"}),"Analyzing");
                await ProcessRunner.RunAsync(ffmpeg,["-hide_banner","-nostdin","-v","error","-y","-nostats","-progress","pipe:1",..args,"-an","-pass","1","-passlogfile",log,"-f","null",OperatingSystem.IsWindows()?"NUL":"/dev/null"],token,first.ReadLine);
                args.AddRange(["-pass","2","-passlogfile",log,"-c:a","aac","-b:a",((int)audioRate).ToString(CultureInfo.InvariantCulture),"-movflags","+faststart"]);
            }
            else args.AddRange(["-map","0:a:0","-vn","-c:a","aac","-b:a",((int)audioRate).ToString(CultureInfo.InvariantCulture)]);
            await RenderAsync(args,output,info.Duration,p=>progress(p with{Fraction=(video?.5:0)+p.Fraction*(video?.5:1),Label=$"Compression {attempt+1} · encoding"}),token);
            var actual=new FileInfo(output).Length;if(actual<=target)return output;
            File.Delete(output);budget*=Math.Clamp((double)target/actual*.88,.2,.9);
        }
        throw new IOException("This size cannot be reached. Choose a larger target or smaller dimensions.");
    }

    private async Task<IReadOnlyList<string>> SplitAsync(string input,MediaInfo info,string directory,MediaOptions options,Action<OperationProgress> progress,CancellationToken token)
    {
        RequireVideo(info);if(info.Duration<=0)throw new ArgumentException("Splitting requires a known duration.");
        var points=options.SplitPoints.ToList();
        if(points.Count==0)
        {
            var count=options.EqualClips??(int)Math.Ceiling(info.Duration/options.SplitInterval);
            if(count<1||count>10000)throw new ArgumentException("Choose at most 10,000 clips.");
            points=Enumerable.Range(1,count-1).Select(n=>options.EqualClips is null?options.SplitInterval*n:info.Duration*n/count).ToList();
        }
        if(points.Any(p=>!double.IsFinite(p)||p<=0||p>=info.Duration))throw new ArgumentException("A split point lies outside the video.");
        var bounds=new[]{0d}.Concat(points.Distinct().Order()).Append(info.Duration).ToArray();
        if(bounds.Zip(bounds.Skip(1)).Any(p=>p.Second-p.First<.05))throw new ArgumentException("Keep split points at least 0.05 seconds apart.");
        if(options.DiscardedClips.Any(n=>n<0||n>=bounds.Length-1))throw new ArgumentException("A discarded clip is outside the range.");
        var retained=Enumerable.Range(0,bounds.Length-1).Except(options.DiscardedClips).ToList();if(retained.Count==0)throw new ArgumentException("Keep at least one clip.");
        var folder=Path.Combine(directory,"clips-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var clips=new List<string>();var ext=Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
        for(var n=0;n<retained.Count;n++)
        {
            var index=retained[n];var local=options with{Start=bounds[index],End=bounds[index+1]};
            var produced=await ApplyAsync("trimVideo",input,info,folder,local,p=>progress(p with{Fraction=(n+(p.Fraction??0))/retained.Count*.95,Label=$"Saving clip {n+1} of {retained.Count}"}),token);
            var clip=Path.Combine(folder,options.JoinSplitClips?$"segment-{n}.{ext}":Path.GetFileNameWithoutExtension(input)+$" Part {index+1}.{ext}");File.Move(produced[0],clip);clips.Add(clip);
        }
        if(!options.JoinSplitClips)return clips;
        var joined=Output(directory,input,ext," Split");
        await ConcatAsync(clips,joined,retained.Sum(i=>bounds[i+1]-bounds[i]),progress,token);Directory.Delete(folder,true);return [joined];
    }

    private async Task<string> JoinAsync(IReadOnlyList<string> inputs,string directory,MediaOptions options,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        if(inputs.Count<2)throw new ArgumentException("Choose at least two videos.");
        var infos=new List<MediaInfo>();foreach(var input in inputs){var info=await InspectAsync(input,token);RequireVideo(info);infos.Add(info);}
        var width=(infos[0].Video!.Width??1280)/2*2;var height=(infos[0].Video!.Height??720)/2*2;var audio=infos.Any(i=>i.HasAudio);
        var work=Path.Combine(directory,"join-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);var clips=new List<string>();
        for(var index=0;index<inputs.Count;index++)
        {
            var args=new List<string>{"-i",Path.GetFullPath(inputs[index])};if(audio&&!infos[index].HasAudio)args.AddRange(["-f","lavfi","-i","anullsrc=r=48000:cl=stereo"]);
            args.AddRange(["-map","0:v:0","-vf",$"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,setpts=PTS-STARTPTS"]);
            if(audio)args.AddRange(["-map",infos[index].HasAudio?"0:a:0":"1:a:0","-ac","2","-ar","48000","-af","asetpts=PTS-STARTPTS"]);else args.Add("-an");
            args.AddRange(["-t",Number(infos[index].Duration),..VideoEncoding("mp4",options)]);var file=Path.Combine(work,$"clip-{index}.mp4");
            await RenderAsync(args,file,infos[index].Duration,p=>progress?.Report(p with{Fraction=(index+(p.Fraction??0))/inputs.Count*.95,Label=$"Joining clip {index+1} of {inputs.Count}"}),token);clips.Add(file);
        }
        var output=Output(directory,inputs[0],"mp4"," Joined");await ConcatAsync(clips,output,infos.Sum(i=>i.Duration),p=>progress?.Report(p),token);Directory.Delete(work,true);return output;
    }

    private async Task ConcatAsync(IReadOnlyList<string> clips,string output,double duration,Action<OperationProgress> progress,CancellationToken token)
    {
        var directory=Path.GetDirectoryName(clips[0])!;var list=Path.Combine(directory,"clips.txt");
        await File.WriteAllLinesAsync(list,clips.Select(p=>"file '"+Path.GetFileName(p)+"'"),token);
        var args=new List<string>{"-f","concat","-safe","1","-i",list,"-map","0","-c","copy"};
        if(new[]{".mp4",".mov",".m4v"}.Contains(Path.GetExtension(output).ToLowerInvariant()))args.AddRange(["-movflags","+faststart"]);
        await RenderAsync(args,output,duration,p=>progress(p with{Fraction=.95+(p.Fraction??0)*.05,Label="Joining clips…"}),token);
    }

    private async Task RenderAsync(IEnumerable<string> arguments,string output,double? duration,Action<OperationProgress> progress,CancellationToken token)
    {
        var parser=new FFmpegProgress(duration,progress,"Processing media…");
        await ProcessRunner.RunAsync(ffmpeg,["-hide_banner","-nostdin","-v","error","-n","-nostats","-stats_period","0.2","-progress","pipe:1",..arguments,output],token,parser.ReadLine);
        token.ThrowIfCancellationRequested();if(!File.Exists(output)||new FileInfo(output).Length==0)throw new IOException("No media output was produced.");
    }
    private static Action<OperationProgress> Scoped(IProgress<OperationProgress>? progress,int index,int count)=>p=>progress?.Report(p with{Fraction=p.Fraction is null?null:(index+p.Fraction)/count*.98,Label=count>1?$"File {index+1} of {count} · {p.Label}":p.Label});
    private static string Output(string directory,string input,string ext,string suffix)=>SafePaths.Unique(directory,Path.GetFileNameWithoutExtension(input)+suffix+"."+ext);
    private static string ToolSuffix(string tool)=>tool switch{"muteVideo"=>"Muted","removeMetadata"=>"Metadata","trimAudio" or "trimVideo"=>"Trimmed","cropVideo"=>"Cropped","changeVideoSpeed"=>"Speed","audioChannels"=>"Channels","redactVideo" or "redactAudio"=>"Redacted",_=>tool};
    internal static MediaOptions SourceDepth(MediaOptions options,MediaInfo info)
    {
        if(options.SampleDepth!="matchSource")return options;var audio=info.Streams.FirstOrDefault(s=>s.Kind=="audio");
        var depth=audio?.Codec.Contains("pcm_f",StringComparison.Ordinal)==true?"float32":audio?.Bits>24?"pcm32":audio?.Bits>16?"pcm24":"pcm16";return options with{SampleDepth=depth};
    }
    private static string[] AudioEncoding(string ext,MediaOptions o)=>ext switch
    {
        "mp3"=>["-c:a","libmp3lame","-b:a",o.AudioBitrate+"k"],"m4a" or "aac"=>["-c:a","aac","-b:a",o.AudioBitrate+"k"],
        "wav"=>["-c:a",o.SampleDepth switch{"pcm24"=>"pcm_s24le","pcm32"=>"pcm_s32le","float32"=>"pcm_f32le",_=>"pcm_s16le"}],"aiff"=>["-c:a",o.SampleDepth switch{"pcm24"=>"pcm_s24be","pcm32"=>"pcm_s32be","float32"=>"pcm_f32be",_=>"pcm_s16be"}],"flac"=>["-c:a","flac","-compression_level","8"],
        "ogg"=>["-c:a","libvorbis","-q:a",Number(o.Quality*9)],"opus"=>["-c:a","libopus","-b:a",Math.Min(256,o.AudioBitrate)+"k"],
        "wma"=>["-c:a","wmav2","-b:a",Math.Min(320,o.AudioBitrate)+"k"],_=>throw new ArgumentException("Unsupported audio format.")
    };
    private static string[] VideoEncoding(string ext,MediaOptions o)=>ext switch
    {
        "webm"=>["-c:v","libvpx-vp9","-crf",Number(45-o.Quality*30),"-b:v","0","-c:a","libopus","-b:a",Math.Min(256,o.AudioBitrate)+"k"],
        "avi"=>["-c:v","mpeg4","-q:v",Number(25-o.Quality*22),"-c:a","libmp3lame"],
        "wmv"=>["-c:v","wmv2","-q:v",Number(25-o.Quality*22),"-c:a","wmav2"],
        _=>["-c:v","libx264","-preset","medium","-crf",Number(35-o.Quality*23),"-pix_fmt","yuv420p","-c:a","aac","-b:a",o.AudioBitrate+"k"]
    };
    private static string Scale(MediaOptions o)=>$"scale={o.Width?.ToString(CultureInfo.InvariantCulture)??"-2"}:{o.Height?.ToString(CultureInfo.InvariantCulture)??"-2"}:force_original_aspect_ratio=decrease,pad=ceil(iw/2)*2:ceil(ih/2)*2";
    private static string Tempo(double speed){var parts=new List<string>();while(speed>2){parts.Add("atempo=2");speed/=2;}while(speed<.5){parts.Add("atempo=0.5");speed*=2;}parts.Add("atempo="+Number(speed));return string.Join(',',parts);}
    private static MediaRange TimeRange(double start,double? end,double duration){var stop=end??duration;if(!double.IsFinite(start)||!double.IsFinite(stop)||start<0||Math.Round(stop-start,6)<.05||duration>0&&stop>duration+.001)throw new ArgumentException("Choose a valid time range.");return new(start,stop);}
    private static (int X,int Y,int Width,int Height) PixelRect(MediaRegion r,MediaInfo info){var w=info.Video?.Width??0;var h=info.Video?.Height??0;if(!new[]{r.X,r.Y,r.Width,r.Height}.All(double.IsFinite)||r.X<0||r.Y<0||r.Width<2||r.Height<2||r.X+r.Width>w||r.Y+r.Height>h)throw new ArgumentException("Choose a rectangle inside the video.");return((int)r.X,(int)r.Y,(int)r.Width,(int)r.Height);}
    private static void RequireVideo(MediaInfo info){if(info.Video is null)throw new ArgumentException("This file has no video stream.");}
    private static void RequireAudio(MediaInfo info){if(!info.HasAudio)throw new ArgumentException("This file has no audio stream.");}
    private static string Number(double value)=>double.IsFinite(value)?value.ToString("0.########",CultureInfo.InvariantCulture):throw new ArgumentException("Enter a finite number.");
    private static string GetString(JsonElement element,string property)=>element.TryGetProperty(property,out var value)?value.ToString():"";
    private static int? GetInt(JsonElement element,string property)=>element.TryGetProperty(property,out var value)&&int.TryParse(value.ToString(),NumberStyles.Integer,CultureInfo.InvariantCulture,out var number)?number:null;
    private static void Validate(MediaOptions o)
    {
        if(o.SampleDepth is not ("matchSource" or "pcm16" or "pcm24" or "pcm32" or "float32")||o.GifFrameRate<1||o.GifFrameRate>60||o.GifWidth<16||o.GifWidth>8192)throw new ArgumentException("Choose supported GIF and audio sample settings.");
        if(!double.IsFinite(o.Quality)||o.Quality<0||o.Quality>1||o.AudioBitrate<8||o.AudioBitrate>512||!double.IsFinite(o.Speed)||o.Speed<.05||o.Speed>100||o.Channels is not (1 or 2)||!double.IsFinite(o.LeftVolume)||!double.IsFinite(o.RightVolume)||o.LeftVolume<0||o.RightVolume<0||o.LeftVolume>4||o.RightVolume>4||o.Width is <2 or >8192||o.Height is <2 or >8192||o.TargetBytes is <=0||o.ReductionPercent is {} reduction&&(!double.IsFinite(reduction)||reduction<=0||reduction>=100)||!double.IsFinite(o.SplitInterval)||o.SplitInterval<=0)throw new ArgumentException("One or more media settings are outside their supported range.");
        if(!double.IsFinite(o.TargetLoudness)||o.TargetLoudness< -70||o.TargetLoudness> -5||!double.IsFinite(o.LoudnessRange)||o.LoudnessRange<1||o.LoudnessRange>50||!double.IsFinite(o.TruePeak)||o.TruePeak< -9||o.TruePeak>0)throw new ArgumentException("Choose supported loudness targets.");
    }
}
