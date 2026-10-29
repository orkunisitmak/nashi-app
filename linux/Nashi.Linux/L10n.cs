using System.Globalization;
using Avalonia.Markup.Xaml;
using Nashi.Core;

namespace Nashi.Linux;

/// UI language: NASHI_LANGUAGE, else the system UI culture ("tr" or English). Fixed at startup.
public static class L10n
{
    public static string Language { get; set; } = AppLocalization.Normalize(
        Environment.GetEnvironmentVariable("NASHI_LANGUAGE") is { Length: > 0 } forced ? forced : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

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
