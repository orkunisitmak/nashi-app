namespace Nashi.Core;
/// <summary>A stand-in drag payload for the Settings wheel preview; the paths never exist on disk.</summary>
public sealed record WheelSample(string Id,string[] Extensions)
{
    public string Title=>(Extensions[0]=="gz"?"GZIP":Extensions[0].ToUpperInvariant()) is var name&&Extensions.Length>1?Extensions.Length+" × "+name:name;
    public string[] Inputs=>Extensions.Select((ext,i)=>Path.Combine(Path.GetTempPath(),"Nashi Wheel Sample","sample-"+i+"."+ext)).ToArray();
    internal static WheelSample Of(params string[] extensions)=>new(string.Join("+",extensions),extensions);
}
/// <summary>Which wheel items people can hide, and how the Settings preview shows each of them. Mirrors the Mac WheelCustomization.</summary>
public static class WheelCustomization
{
    private static readonly (string Group,string[] Formats)[] AllFormatGroups=[("Images",["jpg","png","webp","heic","tiff","svg","avif","bmp"]),("Documents",["pdf","docx","pages","ppt","pptx","txt","md","rtf","rtfd","textclipping","html","epub"]),("Spreadsheets",["xlsx","xls","ods","csv","tsv"]),("Audio",["mp3","m4a","wav","flac","ogg","opus","aiff","wma"]),("Video",["mp4","mov","mkv","webm","avi","wmv","gif"]),("Archives",["zip","tar","gz","rar"]),("Subtitles",["srt","vtt"])];
    public static readonly string[] ConvertTargets=AllFormatGroups.SelectMany(g=>g.Formats).Where(FeatureCatalog.Conversions.Values.SelectMany(v=>v).ToHashSet().Contains).ToArray();
    public static readonly (string Group,string[] Formats)[] FormatGroups=AllFormatGroups.Select(g=>(g.Group,g.Formats.Where(ConvertTargets.Contains).ToArray())).Where(g=>g.Item2.Length>0).ToArray();
    public static readonly (string Group,string[] Tools)[] ToolGroups=[("General",["compress","removeMetadata","readQRCode"]),("Images",["editImage","annotateImage","frameImage","cropImage","redactImage","createCollage"]),("Video",["trimVideo","cropVideo","muteVideo","changeVideoSpeed","splitVideo","joinVideos","videoSnapshots","redactVideo"]),("Audio",["trimAudio","normalizeAudio","audioChannels","audioToVideo","redactAudio"]),("Documents",["createPDF","mergePDF","splitPDF","organizePDF"]),("Archives",["extractArchive"])];
    // Together these samples show every target and tool at least once (Nashi.Verify WheelCustomizationChecks).
    public static readonly WheelSample[] ConvertSamples=[WheelSample.Of("png"),WheelSample.Of("mp3"),WheelSample.Of("mp4"),WheelSample.Of("gif"),WheelSample.Of("docx"),WheelSample.Of("pdf"),WheelSample.Of("xlsx"),WheelSample.Of("srt"),WheelSample.Of("vtt"),WheelSample.Of("zip"),WheelSample.Of("tar")];
    public static readonly WheelSample[] ToolSamples=[WheelSample.Of("png"),WheelSample.Of("mp3"),WheelSample.Of("mp4"),WheelSample.Of("pdf"),WheelSample.Of("zip"),WheelSample.Of("png","png"),WheelSample.Of("mp4","mp4"),WheelSample.Of("pdf","pdf")];
    public static readonly string[] Actions=["share","float","copy","move","copyPath","terminal"];
    // The Windows editors open three tools under older ids; Settings always stores the catalog id.
    private static readonly Dictionary<string,string> EditorIds=new(){["cropImage"]="crop",["annotateImage"]="annotate",["mergePDF"]="mergePDFs"};
    public static string EditorToolId(string tool)=>EditorIds.GetValueOrDefault(tool,tool);
    public static string ToolId(string choice)=>EditorIds.FirstOrDefault(pair=>pair.Value==choice).Key??choice;
    /// <summary>Drops hidden ids and keeps order. keepOne returns everything instead of an empty wheel.</summary>
    public static IReadOnlyList<T> Visible<T>(IReadOnlyList<T> items,IReadOnlySet<string> hidden,Func<T,string> id,bool keepOne=false){var shown=items.Where(item=>!hidden.Contains(id(item))).ToArray();return keepOne&&shown.Length==0?items:shown;}
    public static WheelSample? FirstConvertSample(string format)=>ConvertSamples.FirstOrDefault(s=>FeatureCatalog.Formats(s.Inputs).Contains(format));
    public static WheelSample? FirstToolSample(string tool)=>ToolSamples.FirstOrDefault(s=>FeatureCatalog.Tools(s.Inputs).Contains(tool));
}
