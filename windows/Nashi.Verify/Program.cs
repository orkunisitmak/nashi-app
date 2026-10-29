using Nashi.Core;
using System.Text.Json;

if(Environment.GetEnvironmentVariable("NASHI_VERIFY_DOWNLOAD_TOOLS")=="1")
{
    var toolPath=Environment.GetEnvironmentVariable("NASHI_VERIFY_TOOLS_DIRECTORY")??throw new Exception("Set a test tool directory.");
    var tools=new Toolchain(toolPath);if(!tools.Ready)await tools.InstallAsync();
    Environment.SetEnvironmentVariable("NASHI_VERIFY_FFMPEG",tools.Executable("ffmpeg"));Environment.SetEnvironmentVariable("NASHI_VERIFY_FFPROBE",tools.Executable("ffprobe"));
}
var checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
var root = Path.Combine(Path.GetTempPath(), "Nashi-Verify-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var languageRoot=Path.Combine(root,"Language checks");Directory.CreateDirectory(languageRoot);
    try{LocalizationChecks.Run(Check,languageRoot);}finally{Directory.Delete(languageRoot,true);}
    await DownloadChecks.Run(Check,root);
    ToolSetupChecks.Run(Check,root);
    var parityRoot=Path.Combine(root,"Parity");Directory.CreateDirectory(parityRoot);try{ParityChecks.Run(Check,parityRoot);}finally{Directory.Delete(parityRoot,true);}
    Check(FeatureCatalog.IsVideo("Clip.m4v")&&FeatureCatalog.IsAudio("Clip.aac"),"Media preview classification differs from Mac aliases");
    foreach(var area in new[]{new WindowFrame(0,24,1511,910),new WindowFrame(-1920,-1030,1920,1006),new WindowFrame(1512,40,1727,1090)})
    {
        var a=WindowPlacement.LeftThird.Frame(area);var b=WindowPlacement.MiddleThird.Frame(area);var c=WindowPlacement.RightThird.Frame(area);
        Check(a.X==area.X&&a.Right==b.X&&b.Right==c.X&&c.Right==area.Right,"Window thirds have gaps or cross display bounds");
        Check(WindowPlacement.LeftHalf.Frame(area).Right==WindowPlacement.RightHalf.Frame(area).X,"Window halves overlap or leave a gap");
        var constrained=1000;Check(WindowPlacement.LeftThird.Frame(area,constrained).X==area.X,"Constrained left placement lost its edge");Check(WindowPlacement.RightThird.Frame(area,constrained).Right==area.Right,"Constrained right placement lost its edge");Check(Math.Abs(WindowPlacement.MiddleThird.Frame(area,constrained).X+(double)WindowPlacement.MiddleThird.Frame(area,constrained).Width/2-(area.X+area.Width/2d))<=.5,"Constrained middle placement lost its center");Check(WindowPlacement.LeftThird.Frame(area,area.Width*2)==area,"An oversized minimum escaped the display");
        Check(WindowPlacement.LeftTwoThirds.Frame(area).Right==b.Right&&WindowPlacement.RightTwoThirds.Frame(area).X==b.X,"Two-thirds placement mismatch");
        foreach(var minimum in new[]{0,1000,area.Width*2})Check(WindowPlacement.Maximize.Frame(area,minimum)==area,"Maximize did not fill the usable work area");
        foreach(var placement in Enum.GetValues<WindowPlacement>())Check(placement.Frame(area).Height==area.Height&&placement.Frame(area).Y==area.Y,"Window layout changed the usable vertical frame");
    }
    foreach (var entry in new[] { "../outside", "/absolute", "C:/outside", @"..\outside", @"\\server\share", "file.txt:stream", "CON", "COM1.txt", "trailing.", "trailing ", "folder/../../outside", "a/./b" })
    {
        try { SafePaths.ArchiveEntry(root, entry); throw new Exception("Unsafe path accepted: " + entry); }
        catch (InvalidDataException) { checks++; }
    }
    Check(SafePaths.ArchiveEntry(root, "folder/file.txt") == Path.Combine(root, "folder", "file.txt"), "Valid nested path failed");
    File.WriteAllText(Path.Combine(root, "Output.txt"), "keep");
    using (var transaction = new ExportTransaction(root))
    {
        var output = Path.Combine(transaction.StagingDirectory, "Output.txt"); File.WriteAllText(output, "new");
        var result = transaction.Publish([output], CancellationToken.None);
        Check(result[0] == Path.Combine(root, "Output 2.txt"), "Existing output was not retained");
    }
    Check(File.ReadAllText(Path.Combine(root, "Output.txt")) == "keep", "Existing file changed");
    using (var transaction = new ExportTransaction(root))
    {
        var good = Path.Combine(transaction.StagingDirectory, "Good.txt"); File.WriteAllText(good, "temporary");
        try { transaction.Publish([good, Path.Combine(transaction.StagingDirectory, "Missing.txt")], CancellationToken.None); throw new Exception("Invalid batch succeeded"); }
        catch (InvalidDataException) { checks++; }
    }
    Check(!File.Exists(Path.Combine(root, "Good.txt")), "Failed batch left an output");
    using (var transaction = new ExportTransaction(root))
    {
        var output = Path.Combine(transaction.StagingDirectory, "Cancelled.txt"); File.WriteAllText(output, "temporary");
        using var token = new CancellationTokenSource(); token.Cancel();
        try { transaction.Publish([output], token.Token); throw new Exception("Cancelled batch succeeded"); }
        catch (OperationCanceledException) { checks++; }
    }
    Check(!Directory.EnumerateDirectories(root).Any(), "Staging files survived cancellation");
    var libraryDirectory=Path.Combine(root,"LibraryData");var library=new LibraryStore(libraryDirectory);
    var unicodeFile=Path.Combine(root,"Türkçe 🍐.txt");File.WriteAllText(unicodeFile,"original");library.Record("Pasted text",[unicodeFile]);library.SetAppearance("dark");
    library.SetWindowManagement(false);
    var reopened=new LibraryStore(libraryDirectory);Check(reopened.Entries.Single().Paths.Single()==unicodeFile&&reopened.Appearance=="dark"&&!reopened.WindowManagementEnabled,"Library restart lost paths or settings");
    Check(reopened.Search("Türkçe").Count()==1&&reopened.Search("PASTED").Count()==1&&reopened.Search("absent").Count()==0,"Library search failed");
    File.Delete(unicodeFile);Check(reopened.Entries.Single().AvailablePaths.Length==0,"Missing Library file was available");
    for(var i=0;i<55;i++)library.Record("ZIP "+i,[Path.Combine(root,"Output.txt")]);Check(library.Entries.Count==LibraryStore.Limit,"Library retention limit failed");
    Check(new LibraryStore(libraryDirectory).Entries.Count==LibraryStore.Limit,"Persisted Library limit failed");
    var album=Path.Combine(root,"Exported album");Directory.CreateDirectory(album);library.Record("Extract ZIP",[album]);Check(new LibraryStore(libraryDirectory).Entries.First().AvailablePaths.Single()==album,"Library lost an exported directory");Directory.Delete(album);
    var broken=Path.Combine(libraryDirectory,"library.json");File.WriteAllText(broken,"{broken");var recovered=new LibraryStore(libraryDirectory);
    Check(recovered.Entries.Count==0&&recovered.RecoveryNotice is not null,"Corrupt Library crashed or did not report recovery");
    Check(Directory.GetFiles(libraryDirectory,"*.recovered-*").Length==1&&File.ReadAllText(Directory.GetFiles(libraryDirectory,"*.recovered-*")[0])=="{broken","Corrupt Library recovery copy missing");
    recovered.Record("Recovered",[Path.Combine(root,"Output.txt")]);Check(new LibraryStore(libraryDirectory).Entries.Count==1,"Library did not recover after corrupt data");
    Directory.Delete(libraryDirectory,true);
    Check(PublicUpdates.Normalize(new Version("0.7.4"))==PublicUpdates.Normalize(new Version("0.7.4.0")),"Equivalent update versions compared differently");
    var updates = new List<double?>(); var parser = new FFmpegProgress(10, p => updates.Add(p.Fraction), "Encoding");
    foreach (var line in new[] { "out_time_us=N/A", "out_time_us=nan", "out_time_us=-1", "out_time_us=1000000", "out_time_us=5000000", "out_time_us=2000000", "out_time_us=20000000" }) parser.ReadLine(line);
    Check(updates.SequenceEqual(new double?[] { .1, .5, .995 }), "Encoder progress parser regressed or completed early");
    new FFmpegProgress(null, _ => throw new Exception("Unknown total invented a fraction"), "Processing").ReadLine("out_time_us=1234"); checks++;
    var zip=DocumentEngine.Zip([Path.Combine(root,"Output.txt")],root);
    var extracted=DocumentEngine.Unzip(zip,root);
    Check(File.ReadAllText(Path.Combine(extracted[0],"Output.txt"))=="keep","ZIP contents changed");
    var unsafeZip=Path.Combine(root,"Traversal.zip");
    using(var malicious=System.IO.Compression.ZipFile.Open(unsafeZip,System.IO.Compression.ZipArchiveMode.Create)){using var entry=malicious.CreateEntry("../outside.txt").Open();entry.WriteByte(1);}
    try{DocumentEngine.Unzip(unsafeZip,root);throw new Exception("Traversal archive succeeded");}catch(InvalidDataException){checks++;}
    Check(!File.Exists(Path.Combine(Path.GetDirectoryName(root)!,"outside.txt")),"Archive wrote outside destination");
    var fake=Path.Combine(root,"fake.zip");File.WriteAllText(fake,"untrusted");
    var update=new WindowsUpdate("99.0.0.0","https://github.com/orkunisitmak/nashi-app/releases/download/test/Nashi.zip",new FileInfo(fake).Length,Convert.ToBase64String(new byte[64]));
    try{await PublicUpdates.VerifyAsync(fake,update);throw new Exception("Unsigned update accepted");}catch(InvalidDataException){checks++;}
    foreach(var url in new[]{"https://evil.example/replace.zip","file:///temp/file.zip","https://github.com/another/repo/releases/download/test/file.zip"})
    {try{PublicUpdates.Validate(update with{Url=url});throw new Exception("Untrusted update URL accepted");}catch(InvalidDataException){checks++;}}
    Console.WriteLine(JsonSerializer.Serialize(new { passed = checks, scope = "Cross-platform Windows engine safety foundation; no Windows GUI claimed" }));
}
finally { Directory.Delete(root, recursive: true); }

// Optional real FFmpeg checks use explicitly supplied tools, never a global PATH.
if(Environment.GetEnvironmentVariable("NASHI_VERIFY_FFMPEG") is {} encoder && Environment.GetEnvironmentVariable("NASHI_VERIFY_FFPROBE") is {} probe)
{
    var directory=Path.Combine(Path.GetTempPath(),"Nashi-Media-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
    try
    {
        var source=Path.Combine(directory,"Source ' $().mp4");
        await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","testsrc2=size=128x72:rate=12:duration=2","-f","lavfi","-i","sine=duration=2:sample_rate=48000","-c:v","libx264","-c:a","aac",source]);
        var original=System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(source));
        var engine=new MediaEngine(encoder,probe);var progress=new List<OperationProgress>();
        var reporter=new InlineProgress(p=>progress.Add(p));
        Check(Math.Abs((await engine.InspectAsync(source)).Video!.FrameRate-12)<.001,"Frame-by-frame preview lost actual source FPS");
        var trimmed=await engine.ToolAsync("trimVideo",[source],directory,new(){Start=.25,End=1.25},reporter);
        Check(Math.Abs((await engine.InspectAsync(trimmed[0])).Duration-1)<.2,"Trim duration is wrong");
        Check((await engine.ToolAsync("trimVideo",[source],directory,new(){Start=1.1,End=1.15},reporter)).Count==1,"A 50 ms trim was refused by floating-point rounding");
        Check(progress.Any(p=>p.Fraction>0&&p.Fraction<.98),"No live media progress");
        var audio=await engine.ConvertAsync([source],"mp3",directory,new());Check((await engine.InspectAsync(audio[0])).HasAudio,"Audio conversion failed");
        var deep=await engine.ConvertAsync([source],"wav",directory,new(){SampleDepth="pcm24"});var matched=await engine.ConvertAsync(deep,"aiff",directory,new());Check((await engine.InspectAsync(matched[0])).Streams.First(s=>s.Kind=="audio").Bits==24,"Match source reduced 24-bit audio");
        var floating=await engine.ConvertAsync([source],"wav",directory,new(){SampleDepth="float32"});var floatMatch=await engine.ConvertAsync(floating,"aiff",directory,new());Check((await engine.InspectAsync(floatMatch[0])).Streams.First(s=>s.Kind=="audio").Codec=="pcm_f32be","Match source lost floating point audio");
        var taggedAudio=await engine.ToolAsync("removeMetadata",audio,directory,new(){RemoveAllMetadata=false,Metadata=new(){{"title","Fixture title"},{"artist","Fixture artist"}}});
        async Task<Dictionary<string,string>> Tags(string file){var result=await ProcessRunner.RunAsync(probe,["-v","error","-show_format","-show_streams","-of","json",file]);using var doc=JsonDocument.Parse(result.Output);var tags=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);if(doc.RootElement.GetProperty("format").TryGetProperty("tags",out var values))foreach(var p in values.EnumerateObject())tags[p.Name]=p.Value.ToString();if(doc.RootElement.TryGetProperty("streams",out var streams))foreach(var stream in streams.EnumerateArray())if(stream.TryGetProperty("tags",out values))foreach(var p in values.EnumerateObject())tags.TryAdd(p.Name,p.Value.ToString());return tags;}
        var writtenTags=await Tags(taggedAudio[0]);Check(writtenTags.GetValueOrDefault("title")=="Fixture title"&&writtenTags.GetValueOrDefault("artist")=="Fixture artist","MP3 metadata editing failed");var clearedTitle=await engine.ToolAsync("removeMetadata",taggedAudio,directory,new(){RemoveAllMetadata=false,Metadata=new(){{"title",""}}});Check(!(await Tags(clearedTitle[0])).ContainsKey("title")&&(await Tags(clearedTitle[0])).GetValueOrDefault("artist")=="Fixture artist","Clearing one media tag removed another");var clearedAll=await engine.ToolAsync("removeMetadata",taggedAudio,directory,new());Check(!(await Tags(clearedAll[0])).ContainsKey("title")&&!(await Tags(clearedAll[0])).ContainsKey("artist"),"Remove all media metadata retained user tags");
        async Task<string> AudioHash(string file)=>(await ProcessRunner.RunAsync(encoder,["-v","error","-i",file,"-map","0:a:0","-c:a","copy","-f","hash","-"])).Output;Check(await AudioHash(audio[0])==await AudioHash(taggedAudio[0]),"Media tag edit changed encoded audio");
        var muted=await engine.ToolAsync("muteVideo",[source],directory,new());Check(!(await engine.InspectAsync(muted[0])).HasAudio,"Mute retained audio");
        var speed=await engine.ToolAsync("changeVideoSpeed",[source],directory,new(){Speed=2});Check((await engine.InspectAsync(speed[0])).Duration<1.3,"Speed duration is wrong");
        var crop=await engine.ToolAsync("cropVideo",[source],directory,new(){Regions=[new(0,0,64,40)]});var cropped=await engine.InspectAsync(crop[0]);Check(cropped.Video?.Width==64&&cropped.Video.Height==40,"Crop dimensions wrong");
        var clips=await engine.ToolAsync("splitVideo",[source],directory,new(){EqualClips=2});Check(clips.Count==2,"Split count wrong");
        foreach(var clip in clips)Check((await engine.InspectAsync(clip)).Duration<1.25,"Split interval wrong");
        var sourceDuration=(await engine.InspectAsync(source)).Duration;var tail=await engine.ToolAsync("splitVideo",[source],directory,new(){SplitInterval=sourceDuration/2-.01});Check(tail.Count==2,"A sub-0.05 s interval remainder failed the split instead of joining the last clip");
        var joined=await engine.ToolAsync("joinVideos",clips,directory,new());Check((await engine.InspectAsync(joined[0])).Duration>1.8,"Join lost content");
        var normalized=await engine.ToolAsync("normalizeAudio",audio,directory,new());var normalizedInfo=await engine.InspectAsync(normalized[0]);Check(normalizedInfo.HasAudio,"Normalization failed");
        // loudnorm runs at 192 kHz; PCM outputs keep that rate unless the source rate is requested (MP3 cannot exceed 48 kHz, so test WAV).
        var wav=await engine.ConvertAsync([source],"wav",directory,new());var normalizedWav=await engine.ToolAsync("normalizeAudio",wav,directory,new());
        var sourceRate=(await engine.InspectAsync(wav[0])).Audio?.SampleRate??0;var normalizedRate=(await engine.InspectAsync(normalizedWav[0])).Audio?.SampleRate??0;Check(sourceRate>0&&normalizedRate==sourceRate,$"Normalization changed the sample rate ({sourceRate} Hz → {normalizedRate} Hz)");
        var snapshots=await engine.ToolAsync("videoSnapshots",[source],directory,new(){SnapshotTimes=[0,1]});Check(snapshots.Count==2&&snapshots.All(File.Exists),"Snapshot output missing");
        var bleeped=await engine.ToolAsync("redactAudio",audio,directory,new(){AudioRedactions=[new(.25,.75)]});Check((await engine.InspectAsync(bleeped[0])).HasAudio,"Bleep export failed");
        var stereo=Path.Combine(directory,"Stereo.wav");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","sine=duration=1:sample_rate=48000","-ac","2",stereo]);var stereoBleep=await engine.ToolAsync("redactAudio",[stereo],directory,new(){AudioRedactions=[new(.25,.75)]});
        Check((await ProcessRunner.RunAsync(probe,["-v","error","-select_streams","a:0","-show_entries","stream=channels","-of","csv=p=0",stereoBleep[0]])).Output.Trim()=="2","Audio redaction downmixed a stereo file");
        var redacted=await engine.ToolAsync("redactVideo",[source],directory,new(){Regions=[new(0,0,32,32,.25,.75)],RedactionMode="blur"});Check((await engine.InspectAsync(redacted[0])).Video is not null,"Video redaction failed");
        var channels=await engine.ToolAsync("audioChannels",audio,directory,new(){Channels=1});Check((await engine.InspectAsync(channels[0])).HasAudio,"Channel export failed");
        var visualized=await engine.ToolAsync("audioToVideo",audio,directory,new(){Width=128,Height=72});Check((await engine.InspectAsync(visualized[0])).Video?.Width==128,"Audio visualization failed");
        var compressed=await engine.ToolAsync("compress",[source],directory,new(){TargetBytes=100000});Check(new FileInfo(compressed[0]).Length<=100000,"Compression ignored target");
        var metadata=await engine.ToolAsync("removeMetadata",[source],directory,new());Check((await engine.InspectAsync(metadata[0])).Video is not null,"Metadata export failed");
        foreach(var ext in MediaEngine.AudioFormats)
        {
            var converted=await engine.ConvertAsync([source],ext,directory,new());Check((await engine.InspectAsync(converted[0])).HasAudio,"No audio in "+ext);if(FeatureCatalog.Conversions.ContainsKey(ext)){var edited=await engine.ToolAsync("removeMetadata",converted,directory,new(){RemoveAllMetadata=false,Metadata=new(){{"title","Nashi tag roundtrip"}}});Check((await Tags(edited[0])).GetValueOrDefault("title")=="Nashi tag roundtrip","Audio metadata failed: "+ext);}
        }
        foreach(var ext in MediaEngine.VideoFormats)
        {
            var converted=await engine.ConvertAsync([source],ext,directory,new());Check((await engine.InspectAsync(converted[0])).Video is not null,"No video in "+ext);
        }
        var portrait=Path.Combine(directory,"Portrait.mp4");
        await ProcessRunner.RunAsync(encoder,["-v","error","-display_rotation:v:0","90","-i",source,"-map","0","-c","copy",portrait]);
        var portraitInfo=await engine.InspectAsync(portrait);Check(portraitInfo.Video?.Width==72&&portraitInfo.Video.Height==128,"Portrait display dimensions ignored rotation");
        var portraitCrop=await engine.ToolAsync("cropVideo",[portrait],directory,new(){Regions=[new(0,0,32,100)]});var portraitCropped=await engine.InspectAsync(portraitCrop[0]);
        Check(portraitCropped.Video?.Width==32&&portraitCropped.Video.Height==100,"Portrait crop used encoded instead of displayed bounds");
        var finalHash=System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(source));
        Check(original.SequenceEqual(finalHash),"Source changed");
        Console.WriteLine(JsonSerializer.Serialize(new{passed=checks,scope="Engine safety plus real FFmpeg conversion, trim, mute, speed, crop, split, join, normalize, snapshots and redaction; Windows UI not claimed"}));
    }
    finally{Directory.Delete(directory,true);}
}

if(Environment.GetEnvironmentVariable("NASHI_VERIFY_UPDATE_MANIFEST") is {} manifestPath&&Environment.GetEnvironmentVariable("NASHI_VERIFY_UPDATE_ARCHIVE") is {} archivePath)
{
    var update=JsonSerializer.Deserialize<WindowsUpdate>(await File.ReadAllTextAsync(manifestPath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new Exception("Missing update manifest.");
    await PublicUpdates.VerifyAsync(archivePath,update);
    var extracted=Path.Combine(Path.GetTempPath(),"Nashi-Update-Verify-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(extracted);
    try{PublicUpdates.ExtractVerified(archivePath,extracted);Console.WriteLine("PASS signed public Windows archive and safe extraction");}finally{Directory.Delete(extracted,true);}
}

sealed class InlineProgress(Action<OperationProgress> report):IProgress<OperationProgress>
{
    public void Report(OperationProgress value)=>report(value);
}

