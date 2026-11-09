using System.Globalization;
using Nashi.Core;

internal static class MediaTimecodeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(Math.Abs(MediaTimecode.Parse("12:33.900") - 753.9) < .000001, "Minute/second timecode parsed incorrectly");
        check(Math.Abs(MediaTimecode.Parse("12:33,9", CultureInfo.GetCultureInfo("en-US")) - 753.9) < .000001, "Comma decimal timecode was not accepted");
        check(Math.Abs(MediaTimecode.Parse("1:02:33.5") - 3753.5) < .000001, "Hour timecode parsed incorrectly");
        check(Math.Abs(MediaTimecode.Parse("753.9") - 753.9) < .000001, "Plain seconds stopped working");
        check(Math.Abs(MediaTimecode.Parse("12,5", CultureInfo.GetCultureInfo("tr-TR")) - 12.5) < .000001, "Localized plain seconds parsed incorrectly");
        check(MediaTimecode.Format(753.9) == "12:33.900", "Minute timecode formatting changed");
        check(MediaTimecode.Format(3753.5) == "1:02:33.500", "Hour timecode formatting changed");
        check(MediaTimecode.Format(3599.9999) == "1:00:00.000", "Timecode rounding did not carry into the next hour");
        foreach (var value in new[] { 0d, 12.345, 3599.999, 3600d, 7384.125 })
            check(Math.Abs(MediaTimecode.Parse(MediaTimecode.Format(value)) - value) < .001, "Timecode round-trip failed: " + value);

        foreach (var invalid in new[] { "", "NaN", "Infinity", "-5", "12:60", "1:60:00", "1:2:3:4", "1::02", "1:2:-3", "79228162514264337593543950335:00:00" })
        {
            try { MediaTimecode.Parse(invalid); throw new Exception("Invalid timecode accepted: " + invalid); }
            catch (ArgumentException) { check(true, "Invalid timecode rejected"); }
        }
    }
}
