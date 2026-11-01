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
            // WebVTT ends cue text at the next timing line; SRT files that leave out the blank line also put the next cue's number before it.
            var text=new List<string>();while(++i<lines.Length&&lines[i].Trim().Length>0){if(Timing().IsMatch(lines[i])){if(text.Count>0&&text[^1].Trim().All(char.IsAsciiDigit))text.RemoveAt(text.Count-1);i--;break;}text.Add(lines[i]);}cues.Add(new(a,b,string.Join('\n',text),match.Groups[3].Value.Trim()));
        }
        if(cues.Count==0)throw new InvalidDataException("No timed subtitle cues were found.");return cues.ToArray();
    }
    private static long Milliseconds(string text)
    {
        var p=text.Replace(',','.').Split(':');var last=p[^1].Split('.');var seconds=int.Parse(last[0],CultureInfo.InvariantCulture);var minutes=int.Parse(p[^2],CultureInfo.InvariantCulture);if(seconds>=60||minutes>=60)throw new InvalidDataException("Invalid subtitle timestamp.");
        return checked((p.Length==3?long.Parse(p[0],CultureInfo.InvariantCulture)*3600000:0)+minutes*60000L+seconds*1000L+int.Parse(last[1].PadRight(3,'0'),CultureInfo.InvariantCulture));
    }
    private static string Stamp(long value,bool vtt)=>$"{value/3600000:00}:{value/60000%60:00}:{value/1000%60:00}{(vtt?'.':',')}{value%1000:000}";
    // WebVTT follows HTML for numeric character references, as on the Mac: 0x80–0x9F name windows-1252 characters, and NUL,
    // surrogates and values past U+10FFFF stand for U+FFFD. WebUtility.HtmlDecode uses raw code points, so rewrite each reference
    // to the character HTML means before the single decoding pass; "&amp;#146;" is not a reference and stays as written.
    [GeneratedRegex("&#(?:([0-9]+)|[xX]([0-9A-Fa-f]+));")]private static partial Regex NumericReference();
    private static readonly int[] Windows1252=[0x20AC,0x81,0x201A,0x192,0x201E,0x2026,0x2020,0x2021,0x2C6,0x2030,0x160,0x2039,0x152,0x8D,0x17D,0x8F,0x90,0x2018,0x2019,0x201C,0x201D,0x2022,0x2013,0x2014,0x2DC,0x2122,0x161,0x203A,0x153,0x9D,0x17E,0x178];
    private static string HtmlNumbers(string text)=>NumericReference().Replace(text,m=>
    {
        var hex=m.Groups[2].Success;long value=0;foreach(var digit in hex?m.Groups[2].Value:m.Groups[1].Value)value=Math.Min(0x110000,value*(hex?16:10)+(char.IsAsciiDigit(digit)?digit-'0':(digit|0x20)-'a'+10));
        value=value is >=0x80 and <=0x9F?Windows1252[value-0x80]:value is 0 or >0x10FFFF or (>=0xD800 and <=0xDFFF)?0xFFFD:value;return "&#"+value.ToString(CultureInfo.InvariantCulture)+";";
    });
    public static string Convert(string source,string format)
    {
        var cues=Parse(source);if(format is "srt" or "txt")cues=[..cues.Select(c=>c with{Text=HtmlNumbers(c.Text)})];return format switch
        {
            "vtt"=>"WEBVTT\n\n"+string.Join("\n\n",cues.Select(c=>$"{Stamp(c.Start,true)} --> {Stamp(c.End,true)}{(c.Settings.Length>0?" "+c.Settings:"")}\n{c.Text}"))+"\n",
            "srt"=>string.Join("\n\n",cues.Select((c,i)=>$"{i+1}\n{Stamp(c.Start,false)} --> {Stamp(c.End,false)}\n{WebUtility.HtmlDecode(VttOnlyTags().Replace(c.Text,""))}"))+"\n",
            "txt"=>string.Join("\n\n",cues.Select(c=>WebUtility.HtmlDecode(Tags().Replace(c.Text,""))))+"\n",_=>throw new ArgumentException("Choose SRT, VTT or TXT.")
        };
    }
}
