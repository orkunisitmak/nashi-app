using Nashi.Core;
namespace Nashi.Windows;
// What every wheel shares: its size, where the first item sits, the user's wheel preferences and the icons.
// The wheel itself is drawn by WheelFace.
internal static class WheelPresentation
{
    internal const double Diameter=320;
    internal static double StartAngle(int count)=>count==3?-180:-90;
    internal static Preferences Preferences{get;set;}=new();
    // Glass wheels frost what is really under them; solid wheels stay paper.
    internal static bool Clear=>Theme.Glass&&Preferences.WheelFinish!="solid";
    // Line icons for tools and file actions; formats and downloads read best as words alone.
    internal static string? Icon(string id)=>id switch
    {
        "convert"=>"\uE8AB","compress"=>"\uE73F","removeMetadata"=>"\uE8EC","editImage"=>"\uE9E9","annotate" or "annotateImage"=>"\uE70F","frameImage"=>"\uE8B9",
        "crop" or "cropImage" or "cropVideo"=>"\uE7A8","trimAudio" or "trimVideo"=>"\uE8C6","muteVideo"=>"\uE74F","normalizeAudio" or "audioChannels"=>"\uE767","audioToVideo"=>"\uE714",
        "createCollage"=>"\uE8A9","redactImage" or "redactVideo" or "redactAudio"=>"\uE72E","readQRCode"=>"\uED14","resize"=>"\uE740","rotate"=>"\uE7AD","mergePDF" or "mergePDFs" or "createPDF"=>"\uE8A5","splitPDF" or "splitVideo"=>"\uE8C6","organizePDF"=>"\uE8FD","extractArchive" or "unzip"=>"\uE8B7","joinVideos"=>"\uE714","videoSnapshots"=>"\uE722","changeVideoSpeed"=>"\uE916",
        "share"=>"\uE72D","float"=>"\uE8A7","copy"=>"\uE8C8","move"=>"\uE8DE","copyPath"=>"\uE71B","terminal"=>"\uE756",_=>null
    };
}
