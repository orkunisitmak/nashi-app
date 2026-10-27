using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Nashi.Core;

public static class AppLocalization
{
    public static string Normalize(string? language)=>language=="tr"?"tr":"en";
    public static bool Matches(string source,string query,string language)
    {
        var compare=CultureInfo.GetCultureInfo(Normalize(language)).CompareInfo;
        return compare.IndexOf(source,query,CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace)>=0||compare.IndexOf(Text(source,language),query,CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace)>=0;
    }
    public static IReadOnlyDictionary<string,string> Turkish{get;}=Load();
    // Shared catalog first, then Windows-only copy, then the Mac strings file for anything still missing.
    private static Dictionary<string,string> Load()
    {
        var catalog=Json("Nashi.Turkish.json")??throw new InvalidDataException("Missing Turkish UI catalog.");
        foreach(var (source,target) in Json("Nashi.Turkish.Windows.json")??[])catalog.TryAdd(source,target);
        using var stream=typeof(AppLocalization).Assembly.GetManifestResourceStream("Nashi.Turkish.strings");
        // Kept local: static initializers run in declaration order and Turkish loads first.
        var strings=new Regex("""^"((?:[^"\\]|\\.)*)"\s*=\s*"((?:[^"\\]|\\.)*)";""",RegexOptions.Multiline|RegexOptions.CultureInvariant);
        if(stream is not null)foreach(Match entry in strings.Matches(new StreamReader(stream).ReadToEnd()))
        {
            var source=Placeholders(Unescape(entry.Groups[1].Value));if(!catalog.ContainsKey(source))catalog[source]=Placeholders(Unescape(entry.Groups[2].Value));
        }
        return catalog;
    }
    private static Dictionary<string,string>? Json(string name)
    {
        using var stream=typeof(AppLocalization).Assembly.GetManifestResourceStream(name);
        return stream is null?null:JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??throw new InvalidDataException("Invalid Turkish UI catalog: "+name);
    }
    private static string Unescape(string value)=>Regex.Replace(value,@"\\(.)",m=>m.Groups[1].Value switch{"n"=>"\n","t"=>"\t",var other=>other});
    // %@, %lld, %d and positional %2$@ become the {0} style used by the Windows UI.
    private static string Placeholders(string value)
    {
        var next=0;
        return Regex.Replace(value.Replace("%%","\u0001"),@"%(?:([0-9])\$)?(?:@|l{0,2}[du]|\.?[0-9]*f)",m=>"{"+(m.Groups[1].Success?int.Parse(m.Groups[1].Value)-1:next++)+"}").Replace("\u0001","%");
    }
    // Product and key names that keep Latin capitals in Turkish upper case (SHIFT, not SHİFT). Declared before `uppercase`, which uses it.
    public static IReadOnlySet<string> LatinWords{get;}=LoadLatinWords();
    private static HashSet<string> LoadLatinWords()
    {
        using var stream=typeof(AppLocalization).Assembly.GetManifestResourceStream("Nashi.LatinWords.json")??throw new InvalidDataException("Missing Latin word list.");
        return JsonSerializer.Deserialize<HashSet<string>>(stream)??throw new InvalidDataException("Invalid Latin word list.");
    }
    public static string Uppercase(string value,string language)
    {
        if(Normalize(language)!="tr")return value.ToUpperInvariant();
        var turkish=CultureInfo.GetCultureInfo("tr-TR");
        return Regex.Replace(value,@"\p{L}+",word=>LatinWords.Contains(word.Value)?word.Value.ToUpperInvariant():word.Value.ToUpper(turkish));
    }
    private static readonly Dictionary<string,string> uppercase=Turkish.Where(x=>!x.Key.Contains("{0}",StringComparison.Ordinal)).GroupBy(x=>x.Key.ToUpperInvariant()).ToDictionary(g=>g.Key,g=>Uppercase(g.First().Value,"tr"));
    private static readonly (Regex Pattern,string Target)[] templates=Turkish.Where(x=>x.Key.Contains("{0}",StringComparison.Ordinal)).OrderByDescending(x=>x.Key.Length).Select(x=>
    {
        var pattern=Regex.Escape(x.Key);
        for(var i=0;i<10;i++)pattern=pattern.Replace(Regex.Escape("{"+i+"}"),"(.*?)",StringComparison.Ordinal);
        return(new Regex("^"+pattern+"$",RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50)),x.Value);
    }).ToArray();
    // Exact catalog entries only; templates could match file names or other user data.
    public static bool Known(string source)=>Turkish.ContainsKey(source)||uppercase.ContainsKey(source);
    public static string Text(string source,string language)
    {
        if(Normalize(language)!="tr")return source;
        if(Turkish.TryGetValue(source,out var exact)||uppercase.TryGetValue(source,out exact))return exact;
        if(source.Length>=16_384)return source;
        foreach(var (pattern,target) in templates)
        {
            var match=pattern.Match(source);if(!match.Success)continue;
            return Regex.Replace(target,@"\{([0-9])\}",token=>int.TryParse(token.Groups[1].Value,out var index)&&index+1<match.Groups.Count?match.Groups[index+1].Value:token.Value);
        }
        return source;
    }
}
