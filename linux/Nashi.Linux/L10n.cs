using System.Globalization;
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

    public static string T(string english) => AppLocalization.Text(english, Language);

    /// Translates the template, then fills it in.
    public static string F(string template, params object[] args) => string.Format(T(template), args);

    public static string Upper(string english) => AppLocalization.Uppercase(T(english), Language);
}

/// {l:T 'English text'} in XAML.
public sealed class TExtension(string text) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => L10n.T(text);
}
