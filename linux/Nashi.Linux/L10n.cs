using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Markup.Xaml;
using Nashi.Core;

namespace Nashi.Linux;

/// UI language: explicit environment override, saved preference, then system UI culture. Fixed at startup.
public static class L10n
{
    public static string Language { get; set; } = ResolveLanguage(Settings.Current.Language,
        Environment.GetEnvironmentVariable("NASHI_LANGUAGE"), CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    public static string ResolveLanguage(string preference, string? forced, string system) => AppLocalization.Normalize(
        forced is { Length: > 0 } ? forced : preference is "en" or "tr" ? preference : system);

    public static string T(string english) => Text(english, Language);

    /// Linux-only strings live in linux/Nashi.Linux/Turkish.json, so the shared catalog (and the macOS strings generated
    /// from it) stays untouched. They are looked up first, the same ways the shared catalog is: exact, upper case, template.
    public static string Text(string english, string language)
    {
        if (AppLocalization.Normalize(language) != "tr") return english;
        if (Linux.TryGetValue(english, out var exact) || LinuxUpper.TryGetValue(english, out exact)) return exact;
        var shared = AppLocalization.Text(english, language);
        if (shared != english) return shared;
        foreach (var (pattern, target) in LinuxTemplates)
            if (pattern.Match(english) is { Success: true } match)
                return Regex.Replace(target, @"\{([0-9])\}", t => int.Parse(t.Groups[1].Value) + 1 < match.Groups.Count ? match.Groups[int.Parse(t.Groups[1].Value) + 1].Value : t.Value);
        return english;
    }

    static readonly Dictionary<string, string> Linux = LoadLinux();
    static readonly Dictionary<string, string> LinuxUpper = Linux.Where(x => !x.Key.Contains("{0}", StringComparison.Ordinal))
        .GroupBy(x => x.Key.ToUpperInvariant()).ToDictionary(g => g.Key, g => AppLocalization.Uppercase(g.First().Value, "tr"));
    static readonly (Regex Pattern, string Target)[] LinuxTemplates = [.. Linux.Where(x => x.Key.Contains("{0}", StringComparison.Ordinal))
        .OrderByDescending(x => x.Key.Length).Select(x => (new Regex("^" + Regex.Replace(Regex.Escape(x.Key), @"\\\{[0-9]}", "(.*?)") + "$",
            RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)), x.Value))];

    static Dictionary<string, string> LoadLinux()
    {
        using var stream = typeof(L10n).Assembly.GetManifestResourceStream("Nashi.Linux.Turkish.json") ?? throw new InvalidDataException("Missing Linux Turkish catalog.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException("Invalid Linux Turkish catalog.");
    }

    /// Translates the template, then fills it in.
    public static string F(string template, params object[] args) => string.Format(T(template), args);

    public static string Upper(string english) => AppLocalization.Uppercase(T(english), Language);
}

/// {l:T 'English text'} in XAML.
public sealed class TExtension(string text) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => L10n.T(text);
}
