using System.Globalization;
namespace Nashi.Windows;

/// Validation copy stays localizable instead of exposing framework messages.
internal static class NumericInput
{
    // No group separators: in Turkish "12.5" would otherwise read as 125. Like
    // ToolEditor.Value, a decimal point is still accepted when the culture uses a comma.
    internal static double ParseFinite(string text,CultureInfo? culture=null)
    {
        if(!double.TryParse(text,NumberStyles.Float,culture??CultureInfo.CurrentCulture,out var value)&&!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!double.IsFinite(value))
            throw new ArgumentException("Enter a finite number.");
        return value;
    }
}
