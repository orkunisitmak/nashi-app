using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
namespace Nashi.Core;
public static partial class SubtitleCodec
{
    public sealed record Cue(long Start,long End,string Text,string Settings="");
    [GeneratedRegex(@"^\s*((?:\d+:)?\d{2}:\d{2}[.,]\d{1,3})\s*-->\s*((?:\d+:)?\d{2}:\d{2}[.,]\d{1,3})(?:\s+(.*))?\s*$")]private static partial Regex Timing();
    [GeneratedRegex("<[^>]+>")]private static partial Regex Tags();
    [GeneratedRegex(@"<(?:/?(?:c|v|lang|ruby|rt)(?:[ .][^>]*)?|\d{2}:\d{2}(?::\d{2})?\.\d{3})>")]private static partial Regex VttOnlyTags();
    public static Cue[] Parse(string source)
    {
        var lines=source.TrimStart('\uFEFF').Replace("\r\n","\n").Replace('\r','\n').Split('\n');var cues=new List<Cue>();
        for(var i=0;i<lines.Length;i++)
        {
            var line=lines[i].Trim();if(line is "NOTE" or "STYLE" or "REGION"||line.StartsWith("NOTE ",StringComparison.Ordinal)){while(i<lines.Length&&lines[i].Trim().Length>0)i++;continue;}
            var match=Timing().Match(line);if(!match.Success)continue;var a=Milliseconds(match.Groups[1].Value);var b=Milliseconds(match.Groups[2].Value);if(b<=a)throw new InvalidDataException("A subtitle cue ends before it starts.");
            var text=new List<string>();while(++i<lines.Length&&lines[i].Trim().Length>0)text.Add(lines[i]);cues.Add(new(a,b,string.Join('\n',text),match.Groups[3].Value.Trim()));
        }
        if(cues.Count==0)throw new InvalidDataException("No timed subtitle cues were found.");return cues.ToArray();
    }
    private static long Milliseconds(string text)
    {
        var p=text.Replace(',','.').Split(':');var last=p[^1].Split('.');var seconds=int.Parse(last[0],CultureInfo.InvariantCulture);var minutes=int.Parse(p[^2],CultureInfo.InvariantCulture);if(seconds>=60||minutes>=60)throw new InvalidDataException("Invalid subtitle timestamp.");
        return checked((p.Length==3?long.Parse(p[0],CultureInfo.InvariantCulture)*3600000:0)+minutes*60000L+seconds*1000L+int.Parse(last[1].PadRight(3,'0'),CultureInfo.InvariantCulture));
    }
    private static string Stamp(long value,bool vtt)=>$"{value/3600000:00}:{value/60000%60:00}:{value/1000%60:00}{(vtt?'.':',')}{value%1000:000}";
    public static string Convert(string source,string format)
    {
        var cues=Parse(source);return format switch
        {
            "vtt"=>"WEBVTT\n\n"+string.Join("\n\n",cues.Select(c=>$"{Stamp(c.Start,true)} --> {Stamp(c.End,true)}{(c.Settings.Length>0?" "+c.Settings:"")}\n{c.Text}"))+"\n",
            "srt"=>string.Join("\n\n",cues.Select((c,i)=>$"{i+1}\n{Stamp(c.Start,false)} --> {Stamp(c.End,false)}\n{WebUtility.HtmlDecode(VttOnlyTags().Replace(c.Text,""))}"))+"\n",
            "txt"=>string.Join("\n\n",cues.Select(c=>WebUtility.HtmlDecode(Tags().Replace(c.Text,""))))+"\n",_=>throw new ArgumentException("Choose SRT, VTT or TXT.")
        };
    }
}
