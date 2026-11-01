using Nashi.Core;
using System.Text.Json;

if(Environment.GetEnvironmentVariable("NASHI_VERIFY_DOWNLOAD_TOOLS")=="1")
{
    var toolPath=Environment.GetEnvironmentVariable("NASHI_VERIFY_TOOLS_DIRECTORY")??throw new Exception("Set a test tool directory.");
    var tools=new Toolchain(toolPath);if(!tools.Ready)await tools.InstallAsync();
    // Starting each download proves it is this platform's binary and that it kept its executable bit.
    await ProcessRunner.RunAsync(tools.Executable("yt-dlp"),["--version"]);await ProcessRunner.RunAsync(tools.Executable("deno"),["--version"]);
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
    var srt="1\n00:00:01,000 --> 00:00:02,000\nI <3 you & him <i>really</i>\n\n2\n00:00:03,000 --> 00:00:04,000\n5 < 6 and 7 > 3\n";
    var vtt=SubtitleCodec.Convert(srt,"vtt");
    Check(vtt.Contains("I &lt;3 you &amp; him <i>really</i>")&&vtt.Contains("5 &lt; 6 and 7 > 3"),"SRT to VTT left a bare < or & that WebVTT reads as markup");
    Check(SubtitleCodec.Convert("WEBVTT\n\n00:01.000 --> 00:02.000\nTom &amp; Jerry &lt;3 <c.red>hi</c> <00:01.500>late\n","vtt").Contains("Tom &amp; Jerry &lt;3 <c.red>hi</c> <00:01.500>late"),"VTT to VTT re-escaped entities or cue tags");
    Check(SubtitleCodec.Convert(srt,"txt")=="I <3 you & him really\n\n5 < 6 and 7 > 3\n","TXT export stripped text that only looks like a tag");
    var edge="1\n100:00:01,000 --> 100:00:03,000\n<I>Hello</I> <i love you <100:00:02.000>late\n";
    Check(SubtitleCodec.Convert(edge,"vtt").Contains("<i>Hello</i> &lt;i love you <100:00:02.000>late"),"VTT escaping mishandled an uppercase tag, an unterminated tag start or a three-digit hour timestamp");
    Check(SubtitleCodec.Convert(edge,"txt")=="Hello <i love you late\n","TXT export mishandled an uppercase tag, an unterminated tag start or a three-digit hour timestamp");
    var math="1\n00:00:01,000 --> 00:00:02,000\nx <b + 1 and y > 3 <v Ann Lee>hi</v> <c.red.big>ok</c>\n";
    Check(SubtitleCodec.Convert(math,"vtt").Contains("x &lt;b + 1 and y > 3 <v Ann Lee>hi</v> <c.red.big>ok</c>"),"VTT escaping treated text after a tag name as an annotation or broke voice/class tags");
    Check(SubtitleCodec.Convert(math,"txt")=="x <b + 1 and y > 3 hi ok\n","TXT export removed text that only starts like a tag");
    var tabbed="1\n00:00:01,000 --> 00:00:02,000\n<font\tcolor=\"red\">Hi</font> <v\tAnn>yo</v>\n";
    Check(SubtitleCodec.Convert(tabbed,"vtt").Contains("<font\tcolor=\"red\">Hi</font> <v\tAnn>yo</v>")&&SubtitleCodec.Convert(tabbed,"txt")=="Hi yo\n","Tab-separated tag attributes were not treated as markup");
    Check(SubtitleCodec.Convert("WEBVTT\n\n00:01.000 --> 00:02.000\n<v\tAnn>Tom</v> <V Bob>Al</V> <c.red>x</c>\n","srt").Contains("Tom Al x"),"SRT export kept a WebVTT-only tag separated by a tab or written in upper case");
    Check(SubtitleCodec.Convert("1\n00:00:01,000 --> 00:00:02,000\nx <c + 1 and y > 3\n<v Ann\nnext line> ok\n","srt").Contains("x <c + 1 and y > 3\n<v Ann\nnext line> ok"),"SRT export removed text that only starts like a WebVTT tag or crossed a line break");
    Check(SubtitleCodec.Convert(srt,"srt").Contains("5 < 6 and 7 > 3")&&SubtitleCodec.Convert("WEBVTT\n\n00:01.000 --> 00:02.000\n<v Ann>Tom &amp; <b>Jerry</b></v>\n","srt").Contains("Tom & <b>Jerry</b>"),"SRT export changed plain comparison text or SRT formatting tags");
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
    var namedLibrary=new LibraryStore(Path.Combine(root,"Named library"));var izmir=Path.Combine(root,"İzmir Café.txt");File.WriteAllText(izmir,"x");namedLibrary.Record("Resize Image",[izmir]);
    Check(new[]{"izmir","İZMİR","cafe"," İzmir "}.All(q=>namedLibrary.Search(q).Count()==1&&namedLibrary.Search(q,"tr").Count()==1)&&namedLibrary.Search("görseli boyut","tr").Count()==1&&namedLibrary.Search("Ankara","tr").Count()==0,"Library search missed a file name that differs only in case or accents, as macOS finds it");
    File.Delete(izmir);
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
    var datedZip=Path.Combine(root,"Dated.zip");var dated=new DateTime(2020,1,2,3,4,6);
    using(var archive=System.IO.Compression.ZipFile.Open(datedZip,System.IO.Compression.ZipArchiveMode.Create)){var entry=archive.CreateEntry("Dated.txt");entry.LastWriteTime=dated;using var stream=entry.Open();stream.WriteByte(1);}
    Check(File.GetLastWriteTime(Path.Combine(ArchiveEngine.Extract(datedZip,root)[0],"Dated.txt"))==dated,"Extracted files lost their modification dates");
    var rarFixtures=AppContext.BaseDirectory;while(!Directory.Exists(Path.Combine(rarFixtures,"Reference","archive-fixtures")))rarFixtures=Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(rarFixtures))??throw new DirectoryNotFoundException("Reference/archive-fixtures");rarFixtures=Path.Combine(rarFixtures,"Reference","archive-fixtures");
    Check(File.GetLastWriteTime(Path.Combine(ArchiveEngine.Extract(Path.Combine(rarFixtures,"test_read_format_rar.rar"),root)[0],"test.txt"))==new DateTime(2011,6,26,14,53,47)&&File.GetLastWriteTimeUtc(Path.Combine(ArchiveEngine.Extract(Path.Combine(rarFixtures,"test_read_format_rar5_stored.rar"),root)[0],"helloworld.txt"))==new DateTime(2018,9,26,4,43,42,DateTimeKind.Utc),"Extracted RAR files lost their modification dates");
    using(var converted=System.IO.Compression.ZipFile.OpenRead(ArchiveEngine.Convert(datedZip,"zip",root)[0]))Check(converted.GetEntry("Dated.txt")!.LastWriteTime.DateTime==dated,"Converted ZIP lost its file dates");
    using(var converted=File.OpenRead(ArchiveEngine.Convert(datedZip,"tar",root)[0]))Check(new System.Formats.Tar.TarReader(converted).GetNextEntry()!.ModificationTime.LocalDateTime==dated,"Converted TAR lost its file dates");
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
        // A failing tool's message keeps its last 6,000 characters from a whole line, as the Mac trims yt-dlp details:
        // ashowinfo writes ~8.7 KB of "[Parsed_ashowinfo_0 @ …]" lines before the empty output aborts.
        try{await ProcessRunner.RunAsync(encoder,["-hide_banner","-nostdin","-f","lavfi","-i","sine=d=1","-af","ashowinfo,aselect=0","-abort_on","empty_output","-f","null","-"]);throw new Exception("An empty FFmpeg output did not fail");}
        catch(IOException error){Check(error.Message.StartsWith("[Parsed_ashowinfo_0 @",StringComparison.Ordinal)&&error.Message.Length<=6000&&error.Message.EndsWith("Conversion failed!",StringComparison.Ordinal),"A tool error message started in the middle of a line: "+error.Message[..Math.Min(80,error.Message.Length)]);}
        var source=Path.Combine(directory,"Source ' $().mp4");
        await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","testsrc2=size=128x72:rate=12:duration=2","-f","lavfi","-i","sine=duration=2:sample_rate=48000","-c:v","libx264","-c:a","aac",source]);
        var original=System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(source));
        var engine=new MediaEngine(encoder,probe);var progress=new List<OperationProgress>();
        var odd=Path.Combine(directory,"Odd dimensions.mkv");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","testsrc=size=97x65:duration=0.2","-c:v","ffv1",odd]);
        var oddInfo=await engine.InspectAsync(odd);Check(oddInfo.Video is {Width:97,Height:65},"Odd-dimension fixture was rounded before conversion");
        foreach(var format in new[]{"mp4","mov","mkv","m4v"})
        {
            var padded=await engine.ConvertAsync([odd],format,directory,new());var paddedInfo=await engine.InspectAsync(padded[0]);
            Check(paddedInfo.Video is {Width:98,Height:66}&&paddedInfo.Duration>0,"Odd video dimensions could not convert to "+format);
        }
        Check((await engine.InspectAsync(odd)).Video is {Width:97,Height:65},"Odd-dimension conversion changed the source");
        // iPhones store location, make, model and capture date as QuickTime keys, which the MP4 and MOV muxer writes only when asked to.
        var iphone=Path.Combine(directory,"IMG_0001.MOV");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","testsrc2=size=128x72:rate=12:duration=0.5","-c:v","libx264","-movflags","use_metadata_tags","-metadata","com.apple.quicktime.location.ISO6709=+35.6895+139.6917+040.000/","-metadata","com.apple.quicktime.make=Apple","-metadata","com.apple.quicktime.model=iPhone 15","-metadata","com.apple.quicktime.creationdate=2026-10-31T12:00:00+0900",iphone]);Check((await FileTags(iphone)).GetValueOrDefault("com.apple.quicktime.model")=="iPhone 15","iPhone fixture lacks its QuickTime keys");
        foreach(var (iphoneFormat,iphoneKeep) in new[]{("mp4",true),("mov",true),("m4v",true),("mkv",true),("mp4",false),("mov",false)})
        {var iphoneTags=await FileTags((await engine.ConvertAsync([iphone],iphoneFormat,directory,new(){PreserveMetadata=iphoneKeep}))[0]);Check(new[]{("location.ISO6709","+35.6895+139.6917+040.000/"),("make","Apple"),("model","iPhone 15"),("creationdate","2026-10-31T12:00:00+0900")}.All(t=>iphoneTags.GetValueOrDefault("com.apple.quicktime."+t.Item1)==(iphoneKeep?t.Item2:null)),$"An iPhone video converted to {iphoneFormat} with metadata {(iphoneKeep?"kept":"off")} has {string.Join(", ",iphoneTags.Select(t=>t.Key+"="+t.Value))}");}
        var reporter=new InlineProgress(p=>progress.Add(p));
        Check(Math.Abs((await engine.InspectAsync(source)).Video!.FrameRate-12)<.001,"Frame-by-frame preview lost actual source FPS");
        var trimmed=await engine.ToolAsync("trimVideo",[source],directory,new(){Start=.25,End=1.25},reporter);
        Check(Math.Abs((await engine.InspectAsync(trimmed[0])).Duration-1)<.2,"Trim duration is wrong");
        Check((await engine.ToolAsync("trimVideo",[source],directory,new(){Start=1.1,End=1.15},reporter)).Count==1,"A 50 ms trim was refused by floating-point rounding");
        Check(progress.Any(p=>p.Fraction>0&&p.Fraction<.98),"No live media progress");
        var audio=await engine.ConvertAsync([source],"mp3",directory,new());Check((await engine.InspectAsync(audio[0])).HasAudio,"Audio conversion failed");
        // FileInfo sizes a symbolic link itself (its path's length here); percentage targets come from the media it points to (as on macOS).
        var linkedSizeFolder=Path.Combine(directory,"Linked size");Directory.CreateDirectory(linkedSizeFolder);var linkedSizeSong=Path.Combine(linkedSizeFolder,"Linked song.mp3");var linkedSizeLinked=true;
        // Windows accounts without Developer Mode or elevation cannot create symbolic links.
        try{File.CreateSymbolicLink(linkedSizeSong,audio[0]);}catch(Exception e)when(OperatingSystem.IsWindows()&&e is UnauthorizedAccessException or IOException){linkedSizeLinked=false;Console.Error.WriteLine("Skipping symbolic link media checks: this account cannot create symbolic links.");}
        if(linkedSizeLinked)
        {
            var linkedSizeMissing=Path.Combine(linkedSizeFolder,"Missing song.mp3");File.CreateSymbolicLink(linkedSizeMissing,Path.Combine(directory,"No such song.mp3"));
            var linkedSizeInfo=await engine.InspectAsync(linkedSizeSong);Check(linkedSizeInfo.Bytes==new FileInfo(audio[0]).Length,$"Inspect sized a symbolic link as {linkedSizeInfo.Bytes} bytes instead of the linked file's {new FileInfo(audio[0]).Length}");
            var linkedSizeCompressed=await engine.ToolAsync("compress",[linkedSizeSong],directory,new(){ReductionPercent=30});Check(Path.GetFileName(linkedSizeCompressed[0])=="Linked song Compressed.m4a"&&new FileInfo(linkedSizeCompressed[0]).Length<=new FileInfo(audio[0]).Length*.7,"Compress by percentage failed for a symbolic link");
            var linkedSizeFolderLink=Path.Combine(linkedSizeFolder,"Folder.mp3");Directory.CreateSymbolicLink(linkedSizeFolderLink,directory);
            foreach(var linkedSizeRefused in new[]{linkedSizeMissing,linkedSizeFolderLink}){try{await engine.InspectAsync(linkedSizeRefused);throw new Exception("A dangling or folder symbolic link was inspected: "+linkedSizeRefused);}catch(IOException){checks++;}}
        }
        var deep=await engine.ConvertAsync([source],"wav",directory,new(){SampleDepth="pcm24"});var matched=await engine.ConvertAsync(deep,"aiff",directory,new());Check((await engine.InspectAsync(matched[0])).Streams.First(s=>s.Kind=="audio").Bits==24,"Match source reduced 24-bit audio");
        var floating=await engine.ConvertAsync([source],"wav",directory,new(){SampleDepth="float32"});var floatMatch=await engine.ConvertAsync(floating,"aiff",directory,new());Check((await engine.InspectAsync(floatMatch[0])).Streams.First(s=>s.Kind=="audio").Codec=="pcm_f32be","Match source lost floating point audio");
        // Album art is an attached picture stream, not a tag; MP3, M4A and FLAC store JPEG and PNG art.
        foreach(var (artName,artCodec,artEncoder) in new[]{("Art mp3.mp3","mjpeg","libmp3lame"),("Art flac.flac","png","flac"),("Art m4a.m4a","mjpeg","aac")})
        {
            var artSource=Path.Combine(directory,artName);var artImage=Path.Combine(directory,"Art."+(artCodec=="png"?"png":"jpg"));if(!File.Exists(artImage))await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","color=c=red:s=64x64","-frames:v","1",artImage]);
            await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","sine=duration=1:sample_rate=48000","-i",artImage,"-map","0","-map","1","-c:a",artEncoder,"-c:v","copy","-disposition:v","attached_pic",artSource]);
            Check((await engine.InspectAsync(artSource)).Streams.Count(s=>s.AttachedPicture&&s.Codec==artCodec)==1,artName+" fixture lacks its picture");
            foreach(var artTarget in new[]{"mp3","m4a","flac"})
            {
                var kept=await engine.InspectAsync((await engine.ConvertAsync([artSource],artTarget,directory,new()))[0]);Check(kept.HasAudio&&kept.Streams.Count(s=>s.AttachedPicture&&s.Codec==artCodec)==1,artName+" lost its album art converting to "+artTarget);
                var stripped=await engine.InspectAsync((await engine.ConvertAsync([artSource],artTarget,directory,new(){PreserveMetadata=false}))[0]);Check(stripped.HasAudio&&stripped.Streams.All(s=>s.Kind=="audio"),artName+" kept album art converting to "+artTarget+" with metadata off");
            }
            var artWave=await engine.InspectAsync((await engine.ConvertAsync([artSource],"wav",directory,new()))[0]);Check(artWave.Streams.All(s=>s.Kind=="audio"),"WAV conversion wrote a picture stream");
        }
        var taggedAudio=await engine.ToolAsync("removeMetadata",audio,directory,new(){RemoveAllMetadata=false,Metadata=new(){{"title","Fixture title"},{"artist","Fixture artist"}}});
        async Task<Dictionary<string,string>> Tags(string file){var result=await ProcessRunner.RunAsync(probe,["-v","error","-show_format","-show_streams","-of","json",file]);using var doc=JsonDocument.Parse(result.Output);var tags=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);if(doc.RootElement.GetProperty("format").TryGetProperty("tags",out var values))foreach(var p in values.EnumerateObject())tags[p.Name]=p.Value.ToString();if(doc.RootElement.TryGetProperty("streams",out var streams))foreach(var stream in streams.EnumerateArray())if(stream.TryGetProperty("tags",out values))foreach(var p in values.EnumerateObject())tags.TryAdd(p.Name,p.Value.ToString());return tags;}
        var writtenTags=await Tags(taggedAudio[0]);Check(writtenTags.GetValueOrDefault("title")=="Fixture title"&&writtenTags.GetValueOrDefault("artist")=="Fixture artist","MP3 metadata editing failed");var clearedTitle=await engine.ToolAsync("removeMetadata",taggedAudio,directory,new(){RemoveAllMetadata=false,Metadata=new(){{"title",""}}});Check(!(await Tags(clearedTitle[0])).ContainsKey("title")&&(await Tags(clearedTitle[0])).GetValueOrDefault("artist")=="Fixture artist","Clearing one media tag removed another");var clearedAll=await engine.ToolAsync("removeMetadata",taggedAudio,directory,new());Check(!(await Tags(clearedAll[0])).ContainsKey("title")&&!(await Tags(clearedAll[0])).ContainsKey("artist"),"Remove all media metadata retained user tags");
        async Task<string> AudioHash(string file)=>(await ProcessRunner.RunAsync(encoder,["-v","error","-i",file,"-map","0:a:0","-c:a","copy","-f","hash","-"])).Output;Check(await AudioHash(audio[0])==await AudioHash(taggedAudio[0]),"Media tag edit changed encoded audio");
        var muted=await engine.ToolAsync("muteVideo",[source],directory,new());Check(!(await engine.InspectAsync(muted[0])).HasAudio,"Mute retained audio");
        // Black with white from column 62 and row 62. overlay rounds x and y down to even pixels in 4:2:0 video, so a patch cut at an odd position
        // landed one pixel up and left and the selection's last column and row (62) stayed unredacted; column 63 lies outside the selection.
        var oddOrigin=Path.Combine(directory,"Odd origin.mkv");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","color=black:s=128x96:r=12:d=1,drawbox=x=62:y=0:w=66:h=96:color=white:t=fill,drawbox=x=0:y=62:w=128:h=34:color=white:t=fill","-c:v","ffv1",oddOrigin]);
        foreach(var oddMode in new[]{"blur","pixelate"})
        {
            var oddRedacted=(await engine.ToolAsync("redactVideo",[oddOrigin],directory,new(){Regions=[new(33,33,30,30)],RedactionMode=oddMode}))[0];
            foreach(var (oddX,oddY,oddInside) in new[]{(62,40,true),(40,62,true),(63,40,false),(40,63,false)}){var oddPixel=$"{oddRedacted} {oddX}x{oddY}.gray";await ProcessRunner.RunAsync(encoder,["-v","error","-i",oddRedacted,"-frames:v","1","-vf",$"format=gray,crop=1:1:{oddX}:{oddY}","-f","rawvideo",oddPixel]);var oddValue=File.ReadAllBytes(oddPixel)[0];Check(oddInside?oddValue<160:oddValue>220,$"{oddMode} redaction at an odd position {(oddInside?"left":"changed")} pixel {oddX},{oddY} ({oddValue})");}
        }
        var speed=await engine.ToolAsync("changeVideoSpeed",[source],directory,new(){Speed=2});Check((await engine.InspectAsync(speed[0])).Duration<1.3,"Speed duration is wrong");
        var crop=await engine.ToolAsync("cropVideo",[source],directory,new(){Regions=[new(0,0,64,40)]});var cropped=await engine.InspectAsync(crop[0]);Check(cropped.Video?.Width==64&&cropped.Video.Height==40,"Crop dimensions wrong");
        var capped=await engine.InspectAsync((await engine.ConvertAsync([source],"mp4",directory,new(){Height=720}))[0]);var shrunk=await engine.InspectAsync((await engine.ConvertAsync([source],"mp4",directory,new(){Height=48}))[0]);Check(capped.Video?.Width==128&&capped.Video.Height==72&&shrunk.Video?.Height==48,"Maximum video height upscaled a smaller video");
        var clips=await engine.ToolAsync("splitVideo",[source],directory,new(){EqualClips=2});Check(clips.Count==2,"Split count wrong");
        foreach(var clip in clips)Check((await engine.InspectAsync(clip)).Duration<1.25,"Split interval wrong");
        var sourceDuration=(await engine.InspectAsync(source)).Duration;var tail=await engine.ToolAsync("splitVideo",[source],directory,new(){SplitInterval=sourceDuration/2-.01});Check(tail.Count==2,"A sub-0.05 s interval remainder failed the split instead of joining the last clip");
        var joined=await engine.ToolAsync("joinVideos",clips,directory,new());Check((await engine.InspectAsync(joined[0])).Duration>1.8,"Join lost content");
        // White 16:9 clips: DV stored at 720x480 with 32:27 pixels, then square 640x360. Both fit the 720x480 canvas as 720x405 between black bars;
        // fitting the stored 3:2 frame and then setting square pixels stretched the DV clip over the whole canvas.
        var joinDv=Path.Combine(directory,"Join DV.mp4");var joinPhone=Path.Combine(directory,"Join phone.mp4");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","color=white:s=720x480:r=25:d=1,setsar=32/27","-c:v","libx264","-pix_fmt","yuv420p",joinDv]);await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","color=white:s=640x360:r=25:d=1","-c:v","libx264","-pix_fmt","yuv420p",joinPhone]);
        var joinedShapes=(await engine.ToolAsync("joinVideos",[joinDv,joinPhone],directory,new()))[0];Check((await ProcessRunner.RunAsync(probe,["-v","error","-select_streams","v:0","-show_entries","stream=width,height,sample_aspect_ratio","-of","csv=p=0",joinedShapes])).Output.Trim()=="720,480,1:1","Joined anamorphic video changed its canvas");
        foreach(var (joinTime,joinY,joinBar) in new[]{("0.5",10,true),("0.5",240,false),("1.5",10,true),("1.5",240,false)}){var joinPixel=$"{joinedShapes} {joinTime} {joinY}.gray";await ProcessRunner.RunAsync(encoder,["-v","error","-ss",joinTime,"-i",joinedShapes,"-frames:v","1","-vf",$"format=gray,crop=1:1:360:{joinY}","-f","rawvideo",joinPixel]);var joinValue=File.ReadAllBytes(joinPixel)[0];Check(joinBar?joinValue<64:joinValue>192,$"Joined clip at {joinTime} s lost its 16:9 shape (row {joinY} is {joinValue})");}
        // Live Matroska recordings carry no duration (inspected as 0); "-t 0" encoded an empty clip, so Join dropped them (as fixed on macOS).
        var liveJoinSilent=Path.Combine(directory,"Live join silent.mkv");var liveJoinVoiced=Path.Combine(directory,"Live join voiced.mkv");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","testsrc2=size=128x72:rate=12:duration=2","-c:v","libx264","-live","1",liveJoinSilent]);await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","testsrc2=size=128x72:rate=12:duration=2","-f","lavfi","-i","sine=duration=2:sample_rate=48000","-c:v","libx264","-c:a","aac","-live","1",liveJoinVoiced]);
        Check((await engine.InspectAsync(liveJoinSilent)).Duration==0&&(await engine.InspectAsync(liveJoinVoiced)).Duration==0,"Live Matroska fixtures reported a duration");
        var liveJoined=(await engine.ToolAsync("joinVideos",[source,liveJoinSilent,liveJoinVoiced],directory,new()))[0];var liveJoinStreams=(await ProcessRunner.RunAsync(probe,["-v","error","-show_entries","stream=codec_type,duration","-of","csv=p=0",liveJoined])).Output.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(l=>l.Split(',')).ToDictionary(p=>p[0],p=>double.Parse(p[1],System.Globalization.CultureInfo.InvariantCulture));
        Check(liveJoinStreams["video"]>5.8&&liveJoinStreams["audio"]>5.8&&liveJoinStreams["audio"]<6.3,FormattableString.Invariant($"Join dropped clips without a known duration or let their generated silence run on (video {liveJoinStreams["video"]} s, audio {liveJoinStreams["audio"]} s)"));
        var normalized=await engine.ToolAsync("normalizeAudio",audio,directory,new());var normalizedInfo=await engine.InspectAsync(normalized[0]);Check(normalizedInfo.HasAudio,"Normalization failed");
        // loudnorm runs at 192 kHz; PCM outputs keep that rate unless the source rate is requested (MP3 cannot exceed 48 kHz, so test WAV).
        var wav=await engine.ConvertAsync([source],"wav",directory,new());var normalizedWav=await engine.ToolAsync("normalizeAudio",wav,directory,new());
        var sourceRate=(await engine.InspectAsync(wav[0])).Audio?.SampleRate??0;var normalizedRate=(await engine.InspectAsync(normalizedWav[0])).Audio?.SampleRate??0;Check(sourceRate>0&&normalizedRate==sourceRate,$"Normalization changed the sample rate ({sourceRate} Hz → {normalizedRate} Hz)");
        var snapshots=await engine.ToolAsync("videoSnapshots",[source],directory,new(){SnapshotTimes=[0,1]});Check(snapshots.Count==2&&snapshots.All(File.Exists),"Snapshot output missing");
        // Image viewers ignore a PNG pixel aspect, so frames of DV (32:27), NTSC 4:3 (8:9) and rotated HDV (4:3) videos are saved in their displayed shape, also at the end.
        foreach(var (frameSize,frameSar,frameRotation,frameShape) in new[]{("720x480","32/27","0","853x480"),("720x480","8/9","0","640x480"),("1440x1080","4/3","90","810x1440")})
        {
            var frameSource=Path.Combine(directory,$"Frame source {frameSize} {frameSar.Replace('/',' ')}.mp4");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i",$"testsrc2=size={frameSize}:rate=12:duration=0.5,setsar={frameSar}","-c:v","libx264","-pix_fmt","yuv420p",frameSource+".mp4"]);await ProcessRunner.RunAsync(encoder,["-v","error","-display_rotation:v:0",frameRotation,"-i",frameSource+".mp4","-c","copy",frameSource]);
            foreach(var frame in await engine.ToolAsync("videoSnapshots",[frameSource],directory,new(){SnapshotTimes=[.2,(await engine.InspectAsync(frameSource)).Duration]})){var frameInfo=(await engine.InspectAsync(frame)).Video;Check($"{frameInfo?.Width}x{frameInfo?.Height}"==frameShape,$"A frame of a {frameSize} video with {frameSar} pixels was saved at {frameInfo?.Width}x{frameInfo?.Height} instead of {frameShape}");}
        }
        // The player shows the last frame from its start to the end, including an audio tail after the picture.
        var pictureEndsEarly=Path.Combine(directory,"Picture ends early.mp4");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","testsrc=size=128x96:rate=25:duration=2","-f","lavfi","-i","sine=duration=2.5:sample_rate=48000","-c:v","libx264","-pix_fmt","yuv420p","-c:a","aac",pictureEndsEarly]);
        // Matroska stores no video stream duration, so its end is found by decoding.
        var pictureEndsEarlyMkv=Path.ChangeExtension(pictureEndsEarly,"mkv");await ProcessRunner.RunAsync(encoder,["-v","error","-i",pictureEndsEarly,"-c","copy",pictureEndsEarlyMkv]);
        // MPEG-4 with B-frames in AVI leaves the last packets without a presentation time.
        var pictureEndsEarlyAvi=Path.ChangeExtension(pictureEndsEarly,"avi");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","testsrc=size=128x96:rate=25:duration=2","-f","lavfi","-i","sine=duration=2.5:sample_rate=48000","-c:v","mpeg4","-bf","2","-c:a","libmp3lame",pictureEndsEarlyAvi]);
        foreach(var clip in new[]{pictureEndsEarly,pictureEndsEarlyMkv,pictureEndsEarlyAvi})
        {
            var middle=clip+" middle.png";var last=clip+" last.png";await ProcessRunner.RunAsync(encoder,["-v","error","-ss","1","-i",clip,"-map","0:v:0","-frames:v","1","-c:v","png",middle]);await ProcessRunner.RunAsync(encoder,["-v","error","-i",clip,"-map","0:v:0","-update","1","-c:v","png",last]);
            var endFrames=await engine.ToolAsync("videoSnapshots",[clip],directory,new(){SnapshotTimes=[..(clip==pictureEndsEarlyAvi?new[]{1,2.3}:[1,1.97,1.99,2.3]),(await engine.InspectAsync(clip)).Duration]});var lastBytes=File.ReadAllBytes(last);
            // In AVI, FFmpeg retimes the reordered last frames slightly past 2 s, so only playheads after them are compared there.
            Check(endFrames.Count==(clip==pictureEndsEarlyAvi?3:5)&&File.ReadAllBytes(endFrames[0]).SequenceEqual(File.ReadAllBytes(middle))&&endFrames.Skip(1).All(f=>File.ReadAllBytes(f).SequenceEqual(lastBytes))&&!lastBytes.SequenceEqual(File.ReadAllBytes(middle)),"End-of-video snapshots did not show the last frame, or a mid-video snapshot moved ("+Path.GetExtension(clip)+")");
        }
        var bleeped=await engine.ToolAsync("redactAudio",audio,directory,new(){AudioRedactions=[new(.25,.75)]});Check((await engine.InspectAsync(bleeped[0])).HasAudio,"Bleep export failed");
        // Fully transparent videos with a white stripe: overlay blends a blurred or pixelated patch by its alpha, so a patch that kept it showed the stripe.
        foreach(var (alphaName,alphaCodec) in new[]{("Alpha qtrle.mov",new[]{"-c:v","qtrle"}),("Alpha ProRes 4444.mov",["-c:v","prores_ks","-profile:v","4444","-pix_fmt","yuva444p10le"]),("Alpha PNG.mov",["-c:v","png"]),("Alpha VP9.webm",["-c:v","libvpx-vp9","-pix_fmt","yuva420p"])})
        {
            var alphaSource=Path.Combine(directory,alphaName);await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","color=black:s=128x96:r=12:d=1,drawbox=x=60:y=0:w=8:h=96:color=white:t=fill,format=rgba,colorchannelmixer=aa=0",..alphaCodec,alphaSource]);
            foreach(var alphaMode in new[]{"blur","pixelate"})
            {var alphaRedacted=(await engine.ToolAsync("redactVideo",[alphaSource],directory,new(){Regions=[new(16,16,96,64)],RedactionMode=alphaMode}))[0];var alphaPixel=alphaRedacted+".gray";await ProcessRunner.RunAsync(encoder,["-v","error","-i",alphaRedacted,"-frames:v","1","-vf","format=gray,crop=1:1:63:48","-f","rawvideo",alphaPixel]);Check(File.ReadAllBytes(alphaPixel)[0]<128,$"{alphaName} {alphaMode} redaction left the original pixels visible ({File.ReadAllBytes(alphaPixel)[0]})");}
        }
        var stereo=Path.Combine(directory,"Stereo.wav");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","sine=duration=1:sample_rate=48000","-ac","2",stereo]);var stereoBleep=await engine.ToolAsync("redactAudio",[stereo],directory,new(){AudioRedactions=[new(.25,.75)]});
        Check((await ProcessRunner.RunAsync(probe,["-v","error","-select_streams","a:0","-show_entries","stream=channels","-of","csv=p=0",stereoBleep[0]])).Output.Trim()=="2","Audio redaction downmixed a stereo file");
        // FLAC and AC-3 surround decode as 5.1(side); libopus takes only Vorbis-order layouts and WMA at most two channels.
        async Task<string> AudioLayout(string file)=>(await ProcessRunner.RunAsync(probe,["-v","error","-select_streams","a:0","-show_entries","stream=channels,channel_layout","-of","csv=p=0",file])).Output.Trim();
        var surround=Path.Combine(directory,"Surround.mkv");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","testsrc2=size=128x72:rate=12:duration=1","-f","lavfi","-i","sine=duration=1:sample_rate=48000","-af","aformat=channel_layouts=5.1(side)","-c:v","libx264","-c:a","flac",surround]);Check(await AudioLayout(surround)=="6,5.1(side)","Surround fixture has another layout");
        // ASF stores no channel layout, so WMA and WMV report only their two channels.
        foreach(var (surroundFormat,layout) in new[]{("opus","6,5.1"),("webm","6,5.1"),("wma","2,unknown"),("wmv","2,unknown"),("flac","6,5.1(side)"),("mkv","6,5.1")})
        {string? converted=null;try{converted=(await engine.ConvertAsync([surround],surroundFormat,directory,new()))[0];}catch(IOException){}Check(converted is not null&&await AudioLayout(converted)==layout,"5.1(side) audio did not convert to "+surroundFormat);}
        var surroundOpus=Path.Combine(directory,"Surround source.opus");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","sine=duration=1:sample_rate=48000","-af","aformat=channel_layouts=5.1","-c:a","libopus",surroundOpus]);
        foreach(var (surroundTool,surroundOptions,layout) in new (string,MediaOptions,string)[]{("trimAudio",new(){Start=.25,End=.75},"6,5.1"),("audioChannels",new(){Channels=2},"2,stereo"),("audioChannels",new(){Channels=1},"1,mono")})
            Check(await AudioLayout((await engine.ToolAsync(surroundTool,[surroundOpus],directory,surroundOptions))[0])==layout,"Surround Opus "+surroundTool+" wrote the wrong channels");
        // Edits re-encode WMV audio as two-channel WMA; copies and muted exports keep or drop it unchanged.
        var surroundWmv=Path.Combine(directory,"Surround source.wmv");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","testsrc2=size=128x72:rate=12:duration=1","-f","lavfi","-i","sine=duration=1:sample_rate=48000","-af","aformat=channel_layouts=5.1(side)","-c:v","wmv2","-c:a","pcm_s16le",surroundWmv]);
        foreach(var (surroundTool,surroundOptions,layout) in new (string,MediaOptions,string)[]{("trimVideo",new(){Start=.25,End=.75},"2,unknown"),("cropVideo",new(){Regions=[new(0,0,64,40)]},"2,unknown"),("changeVideoSpeed",new(){Speed=2},"2,unknown"),("redactVideo",new(){Regions=[new(0,0,32,32)]},"2,unknown"),("removeMetadata",new(),"6,5.1(side)"),("muteVideo",new(),"")})
        {string? edited=null;try{edited=(await engine.ToolAsync(surroundTool,[surroundWmv],directory,surroundOptions))[0];}catch(IOException){}Check(edited is not null&&await AudioLayout(edited)==layout,"Surround WMV "+surroundTool+" failed or wrote the wrong channels");}
        var redacted=await engine.ToolAsync("redactVideo",[source],directory,new(){Regions=[new(0,0,32,32,.25,.75)],RedactionMode="blur"});Check((await engine.InspectAsync(redacted[0])).Video is not null,"Video redaction failed");
        var channels=await engine.ToolAsync("audioChannels",audio,directory,new(){Channels=1});Check((await engine.InspectAsync(channels[0])).HasAudio,"Channel export failed");
        // Per-channel sample peaks of the first audio track, decoded to 32-bit float.
        async Task<double[]> Peaks(string file){var count=int.Parse((await ProcessRunner.RunAsync(probe,["-v","error","-select_streams","a:0","-show_entries","stream=channels","-of","csv=p=0",file])).Output.Trim(),System.Globalization.CultureInfo.InvariantCulture);var raw=file+".f32";await ProcessRunner.RunAsync(encoder,["-v","error","-i",file,"-map","0:a:0","-c:a","pcm_f32le","-f","f32le",raw]);var bytes=await File.ReadAllBytesAsync(raw);File.Delete(raw);var peaks=new double[count];for(var n=0;n+4<=bytes.Length;n+=4)peaks[n/4%count]=Math.Max(peaks[n/4%count],Math.Abs(BitConverter.ToSingle(bytes,n)));return peaks;}
        // A mono file keeps its level at 100 %, like on macOS; upmixing it to stereo first lowered it by 3 dB.
        var monoWave=Path.Combine(directory,"Mono.wav");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","sine=frequency=440:duration=1:sample_rate=48000","-c:a","pcm_s16le",monoWave]);var monoPeak=(await Peaks(monoWave)).Single();
        foreach(var (count,left,right,expected) in new (int,double,double,double[])[]{(1,1,1,[1]),(2,1,1,[1,1]),(1,.5,1,[.75]),(2,.5,1,[.5,1])})
        {var leveled=await Peaks((await engine.ToolAsync("audioChannels",[monoWave],directory,new(){Channels=count,LeftVolume=left,RightVolume=right}))[0]);Check(leveled.Length==count&&leveled.Zip(expected).All(p=>Math.Abs(p.First-monoPeak*p.Second)<.001),FormattableString.Invariant($"Audio Channels changed a mono file's level ({count} ch, {left}/{right}): {string.Join(", ",leveled.Select(p=>p.ToString(System.Globalization.CultureInfo.InvariantCulture)))} vs {monoPeak}"));}
        var stereoPeaks=await Peaks(stereo);var stereoLeveled=await Peaks((await engine.ToolAsync("audioChannels",[stereo],directory,new(){LeftVolume=.5}))[0]);Check(stereoLeveled.Length==2&&Math.Abs(stereoLeveled[0]-stereoPeaks[0]*.5)<.001&&Math.Abs(stereoLeveled[1]-stereoPeaks[1])<.001,"Audio Channels changed stereo levels");
        var visualized=await engine.ToolAsync("audioToVideo",audio,directory,new(){Width=128,Height=72});Check((await engine.InspectAsync(visualized[0])).Video?.Width==128,"Audio visualization failed");
        var cover=Path.Combine(directory,"Cover.png");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","color=c=red:s=64x64","-frames:v","1",cover]);var still=await engine.ToolAsync("audioToVideo",audio,directory,new(){Width=128,Height=72,Visualization="image",VisualizationImage=cover});Check((await engine.InspectAsync(still[0])).Video?.Width==128,"Still-image visualization failed");
        var compressed=await engine.ToolAsync("compress",[source],directory,new(){TargetBytes=100000});Check(new FileInfo(compressed[0]).Length<=100000,"Compression ignored target");
        var metadata=await engine.ToolAsync("removeMetadata",[source],directory,new());Check((await engine.InspectAsync(metadata[0])).Video is not null,"Metadata export failed");
        foreach(var ext in MediaEngine.AudioFormats)
        {
            var converted=await engine.ConvertAsync([source],ext,directory,new());Check((await engine.InspectAsync(converted[0])).HasAudio,"No audio in "+ext);if(FeatureCatalog.Conversions.ContainsKey(ext)){var edited=await engine.ToolAsync("removeMetadata",converted,directory,new(){RemoveAllMetadata=false,Metadata=new(){{"title","Nashi tag roundtrip"}}});Check((await Tags(edited[0])).GetValueOrDefault("title")=="Nashi tag roundtrip","Audio metadata failed: "+ext);}
        }
        // FFmpeg reads Ogg Vorbis and Opus comments as audio-stream tags, while MP3, M4A, FLAC and WAV store file-level tags.
        async Task<Dictionary<string,string>> FileTags(string file){using var doc=JsonDocument.Parse((await ProcessRunner.RunAsync(probe,["-v","error","-show_format","-of","json",file])).Output);var tags=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);if(doc.RootElement.GetProperty("format").TryGetProperty("tags",out var values))foreach(var p in values.EnumerateObject())tags[p.Name]=p.Value.ToString();return tags;}
        foreach(var (oggName,oggCodec) in new[]{("Tagged.ogg","libvorbis"),("Tagged.opus","libopus")})
        {
            var oggSource=Path.Combine(directory,oggName);await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","sine=duration=1:sample_rate=48000","-c:a",oggCodec,"-metadata","title=Ogg title","-metadata","artist=Ogg artist",oggSource]);
            foreach(var target in new[]{"mp3","m4a","flac","wav"})
            {
                var kept=await FileTags((await engine.ConvertAsync([oggSource],target,directory,new()))[0]);Check(kept.GetValueOrDefault("title")=="Ogg title"&&kept.GetValueOrDefault("artist")=="Ogg artist",oggName+" lost its tags converting to "+target+" with metadata kept");
                var dropped=await FileTags((await engine.ConvertAsync([oggSource],target,directory,new(){PreserveMetadata=false}))[0]);Check(!dropped.ContainsKey("title")&&!dropped.ContainsKey("artist"),oggName+" kept its tags converting to "+target+" with metadata off");
            }
        }
        foreach(var ext in MediaEngine.VideoFormats)
        {
            var converted=await engine.ConvertAsync([source],ext,directory,new());Check((await engine.InspectAsync(converted[0])).Video is not null,"No video in "+ext);
        }
        // WMA stops at 48 kHz: hi-res audio is saved at 48 kHz in WMA and WMV, other audio keeps its rate.
        foreach(var (name,fixture,format,expected) in new[]{("Hi-res.m4a",new[]{"-f","lavfi","-i","sine=sample_rate=192000:duration=1","-ac","2","-c:a","alac"},"wma","wmav2,48000,2"),("Hi-res 88.2 kHz.flac",["-f","lavfi","-i","sine=sample_rate=88200:duration=1"],"wma","wmav2,48000,1"),("Hi-res audio.mkv",["-f","lavfi","-i","testsrc=size=64x48:duration=1","-f","lavfi","-i","sine=sample_rate=96000:duration=1","-c:a","flac"],"wmv","wmav2,48000,1"),("22 kHz.wav",["-f","lavfi","-i","sine=sample_rate=22050:duration=1","-ac","2"],"wma","wmav2,22050,2"),("44.1 kHz.mp4",["-f","lavfi","-i","testsrc=size=64x48:duration=1","-f","lavfi","-i","sine=sample_rate=44100:duration=1"],"wmv","wmav2,44100,1")})
        {
            var file=Path.Combine(directory,name);await ProcessRunner.RunAsync(encoder,["-v","error",..fixture,file]);string written;
            try{written=(await ProcessRunner.RunAsync(probe,["-v","error","-select_streams","a:0","-show_entries","stream=codec_name,sample_rate,channels","-of","csv=p=0",(await engine.ConvertAsync([file],format,directory,new()))[0]])).Output.Trim();}catch(IOException error){written=error.Message.Split('\n')[0];}
            Check(written==expected,$"{name} → {format}: {written}");
        }
        var gif=Path.Combine(directory,"Animated.gif");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i","testsrc2=size=128x72:rate=12:duration=1",gif]);var gifWebm=await engine.ConvertAsync([gif],"webm",directory,new());Check((await engine.InspectAsync(gifWebm[0])).Video?.Codec=="vp9","GIF to WebM conversion did not produce a VP9 video");
        // GIF viewers ignore a pixel aspect, so DV (32:27), NTSC 4:3 (8:9) and HDV (4:3) frames are resized to their displayed shape; square pixels keep their size.
        foreach(var (gifSize,gifSar,gifShape) in new[]{("720x480","32/27","720x406"),("720x480","8/9","720x540"),("1440x1080","4/3","960x540"),("128x72","1","128x72")})
        {var gifSource=Path.Combine(directory,$"GIF source {gifSize} {gifSar.Replace('/',' ')}.mp4");await ProcessRunner.RunAsync(encoder,["-v","error","-f","lavfi","-i",$"testsrc2=size={gifSize}:rate=12:duration=0.5,setsar={gifSar}","-c:v","libx264","-pix_fmt","yuv420p",gifSource]);var gifShaped=(await engine.InspectAsync((await engine.ConvertAsync([gifSource],"gif",directory,new()))[0])).Video;Check($"{gifShaped?.Width}x{gifShaped?.Height}"==gifShape,$"A {gifSize} video with {gifSar} pixels became a {gifShaped?.Width}x{gifShaped?.Height} GIF instead of {gifShape}");}
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

// Optional ExifTool checks install the pinned upstream package beside the test media tools.
if(Environment.GetEnvironmentVariable("NASHI_VERIFY_DOWNLOAD_TOOLS")=="1"&&Environment.GetEnvironmentVariable("NASHI_VERIFY_TOOLS_DIRECTORY") is {} metadataTools)
{
    var metadata=new MetadataService(metadataTools.TrimEnd('/','\\')+" Metadata");await metadata.InstallAsync(null,CancellationToken.None);
    var directory=Path.Combine(Path.GetTempPath(),"Nashi-Metadata-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
    try
    {
        var photo=Path.Combine(directory,"Fixture ' $().jpg");using(var image=new ImageMagick.MagickImage(ImageMagick.MagickColors.Gold,16,16))image.Write(photo,ImageMagick.MagickFormat.Jpeg);
        var tagged=await metadata.WriteAsync([photo],directory,false,new Dictionary<string,string>{{"EXIF:Artist","Nashi fixture"}});
        Check((await metadata.ReadAsync(tagged[0])).GetValueOrDefault("EXIF:Artist")=="Nashi fixture","ExifTool did not write and read back a tag");
        Check(!(await metadata.ReadAsync(photo)).ContainsKey("EXIF:Artist"),"Metadata editing changed the original");

        // Regression check: Non-ASCII TMP/TEMP, non-ASCII and Turkish file paths, multiline with bare CR, and leading space
        var prevTmp = Environment.GetEnvironmentVariable("TMP");
        var prevTemp = Environment.GetEnvironmentVariable("TEMP");
        var nonAsciiTemp = Path.Combine(directory, "Geçici Dizin ğüşiöç");
        Directory.CreateDirectory(nonAsciiTemp);
        Environment.SetEnvironmentVariable("TMP", nonAsciiTemp);
        Environment.SetEnvironmentVariable("TEMP", nonAsciiTemp);
        try
        {
            var unicodePhoto=Path.Combine(directory,"Vesikalık fotoğraf ğüşiöç ' $().jpg");File.Copy(photo,unicodePhoto);
            var multilineComment="Satır 1\r\nSatır 2\nSatır 3\rSatır 4 & 'özel' ğüşiöç";
            var leadingSpaceArtist=" Örnek Sanatçı";
            var unicodeTagged=await metadata.WriteAsync([unicodePhoto],directory,false,new Dictionary<string,string>
            {
                {"EXIF:Artist",leadingSpaceArtist},
                {"EXIF:UserComment",multilineComment}
            });
            var readBack=await metadata.ReadAsync(unicodeTagged[0]);
            Check(readBack.GetValueOrDefault("EXIF:Artist")==leadingSpaceArtist,"ExifTool did not preserve leading space or non-ASCII tag and path");
            Check(readBack.GetValueOrDefault("EXIF:UserComment")==multilineComment,"ExifTool multiline comment mismatch");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMP", prevTmp);
            Environment.SetEnvironmentVariable("TEMP", prevTemp);
        }

        Console.WriteLine(JsonSerializer.Serialize(new{passed=checks,scope="Verified ExifTool download, non-ASCII path, multiline and leading space tag write/read back"}));
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

