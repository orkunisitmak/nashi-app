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
    private static Dictionary<string,string> Load()
    {
        using var stream=typeof(AppLocalization).Assembly.GetManifestResourceStream("Nashi.Turkish.json")??throw new InvalidDataException("Missing Turkish UI catalog.");
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??throw new InvalidDataException("Invalid Turkish UI catalog.");
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
        for(var i=0;i<10;i++)pattern=pattern.Replace(Regex.Escape("{"+i+"}"),WordBound(x.Key,"{"+i+"}")?@"(\S+?)":"(.*?)",StringComparison.Ordinal);
        return(new Regex("^"+pattern+"$",RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50)),x.Value);
    }).ToArray();
    // A placeholder at the very start or end of a key that touches a word ("{0} files", "Convert to {0}")
    // stands for one token such as a count, format or version. Matching prose there produced
    // "Add or change dosya" and "JPG · Nashi biçimine dönüştür", so those captures may not contain spaces.
    private static bool WordBound(string key,string token)=>
        key.StartsWith(token,StringComparison.Ordinal)&&Regex.IsMatch(key[token.Length..],@"^ \p{L}")||
        key.EndsWith(token,StringComparison.Ordinal)&&Regex.IsMatch(key[..^token.Length],@"\p{L} $");
    public static string Text(string source,string language)
    {
        if(Normalize(language)!="tr")return source;
        if(Turkish.TryGetValue(source,out var exact)||uppercase.TryGetValue(source,out exact))return exact;
        if(source.Length>=16_384)return source;
        // Window titles: translate the title, keep the app suffix ("Convert to JPG · Nashi").
        const string suffix=" · Nashi";if(source.Length>suffix.Length&&source.EndsWith(suffix,StringComparison.Ordinal))return Text(source[..^suffix.Length],language)+suffix;
        foreach(var (pattern,target) in templates)
        {
            var match=pattern.Match(source);if(!match.Success)continue;
            return Regex.Replace(target,@"\{([0-9])\}",token=>int.TryParse(token.Groups[1].Value,out var index)&&index+1<match.Groups.Count?match.Groups[index+1].Value:token.Value);
        }
        return source;
    }
}
