using System.Globalization;

namespace Nashi.Core;

public static class MediaTimecode
{
    public static double Parse(string text, CultureInfo? culture = null)
    {
        var value = text.Trim();
        if (value.Contains(':'))
        {
            var parts = value.Split(':');
            if (parts.Length is not (2 or 3)) throw InvalidTimecode();
            try
            {
                if (parts.Length == 2)
                {
                    if (!TryUnsigned(parts[0], out var minutes) || !TrySeconds(parts[1], culture, out var seconds) || seconds >= 60)
                        throw InvalidTimecode();
                    return (double)(minutes * 60m + seconds);
                }

                if (!TryUnsigned(parts[0], out var hours) ||
                    !TryUnsigned(parts[1], out var remainingMinutes) || remainingMinutes >= 60 ||
                    !TrySeconds(parts[2], culture, out var finalSeconds) || finalSeconds >= 60)
                    throw InvalidTimecode();
                return (double)(hours * 3600m + remainingMinutes * 60m + finalSeconds);
            }
            catch (OverflowException) { throw InvalidTimecode(); }
        }

        if ((!double.TryParse(value, NumberStyles.Float, culture ?? CultureInfo.CurrentCulture, out var plain) &&
             !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out plain)) ||
            !double.IsFinite(plain) || plain < 0)
            throw InvalidTimecode();
        return plain;
    }

    public static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        long milliseconds;
        try { milliseconds = decimal.ToInt64(decimal.Round((decimal)seconds * 1000m, 0, MidpointRounding.AwayFromZero)); }
        catch (OverflowException) { throw new ArgumentOutOfRangeException(nameof(seconds)); }

        var wholeSeconds = milliseconds / 1000;
        var fraction = milliseconds % 1000;
        var hours = wholeSeconds / 3600;
        var minutes = hours > 0 ? wholeSeconds / 60 % 60 : wholeSeconds / 60;
        var remainder = wholeSeconds % 60;
        return hours > 0
            ? FormattableString.Invariant($"{hours}:{minutes:00}:{remainder:00}.{fraction:000}")
            : FormattableString.Invariant($"{minutes:00}:{remainder:00}.{fraction:000}");
    }

    private static bool TryUnsigned(string value, out decimal result) =>
        decimal.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private static bool TrySeconds(string value, CultureInfo? culture, out decimal result)
    {
        value = value.Trim();
        const NumberStyles styles = NumberStyles.AllowDecimalPoint;
        if (decimal.TryParse(value, styles, culture ?? CultureInfo.CurrentCulture, out result) ||
            decimal.TryParse(value, styles, CultureInfo.InvariantCulture, out result))
            return result >= 0;
        if (!value.Contains('.') && value.Count(character => character == ',') == 1 &&
            decimal.TryParse(value.Replace(',', '.'), styles, CultureInfo.InvariantCulture, out result))
            return result >= 0;
        result = 0;
        return false;
    }

    private static ArgumentException InvalidTimecode() => new("Enter a valid timecode or seconds.");
}
