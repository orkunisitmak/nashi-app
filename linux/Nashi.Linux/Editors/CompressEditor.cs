using Avalonia.Controls;
using Avalonia.Layout;
using Nashi.Core;

namespace Nashi.Linux.Editors;

/// Compress for images (macOS compressionControls): the original size, Balanced or Strong with the quality they set,
/// an image size, and an optional target in MB, KB or % smaller. No preview: the options stand alone, as on macOS.
public sealed class CompressEditor(IReadOnlyList<string> inputs) : EditorPanel("compress", inputs)
{
    private static class Copy
    {
        public const string Hint = "Make some space. Keep what matters.";
        public const string Primary = "Compress & save";
        public const string Original = "Original size: {0}";
        public const string Originals = "Original size: {0} in {1} files";
        public const string Preset = "Compression preset";
        public const string Balanced = "Balanced";
        public const string Strong = "Strong";
        public const string BalancedHint = "Smaller files with less quality loss.";
        public const string StrongHint = "Smaller files with stronger compression.";
        public const string Quality = "Quality";
        public const string Target = "Compress to target file size";
        public const string Percent = "% reduction";
        public const string Positive = "Enter a positive, finite target size.";
        public const string Reduction = "Enter a reduction greater than 0% and less than 100%.";
    }

    private string preset = Copy.Balanced;
    private int quality = 80;
    private bool target;
    private double amount = 1;
    private string unit = "MB";
    private SizePicker? size;

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public override bool SideBySide => false;

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 16 };
        var bytes = Inputs.Sum(f => new FileInfo(f).Length);
        stack.Children.Add(new TextBlock { Text = Inputs.Count == 1 ? L10n.F(Copy.Original, Size(bytes)) : L10n.F(Copy.Originals, Size(bytes), Inputs.Count), FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        stack.Children.Add(Ui.Heading(Copy.Preset));
        var presets = new Segments([Copy.Balanced, Copy.Strong], preset);
        var hint = Ui.Caption(preset == Copy.Balanced ? Copy.BalancedHint : Copy.StrongHint);
        var qualityRow = Ui.Slider(Copy.Quality, 1, 100, quality, 80, v => { quality = (int)Math.Round(v); Change(); }, "%", v => Math.Round(v).ToString(System.Globalization.CultureInfo.CurrentCulture));
        presets.Chosen += value =>
        {
            preset = value;
            hint.Text = L10n.T(value == Copy.Balanced ? Copy.BalancedHint : Copy.StrongHint);
            Ui.SliderOf(qualityRow).Value = value == Copy.Balanced ? 80 : 45;
        };
        stack.Children.Add(presets);
        stack.Children.Add(hint);
        stack.Children.Add(qualityRow);
        size = new SizePicker(Source!.Width, Source.Height, Change);
        stack.Children.Add(size);
        var line = new Border { Height = 1 };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("NashiBorder"));
        stack.Children.Add(line);
        var toggle = new CheckBox { Content = L10n.T(Copy.Target), IsChecked = target, FontSize = 12 };
        var value = new TextBox { Text = amount.ToString(System.Globalization.CultureInfo.CurrentCulture), Width = 96, FontSize = 12 };
        var units = new Segments([("MB", "MB"), ("KB", "KB"), ("percent", Copy.Percent)], unit);
        var targetRow = Ui.Row(value, units);
        targetRow.IsVisible = target;
        toggle.IsCheckedChanged += (_, _) => { target = toggle.IsChecked == true; targetRow.IsVisible = target; Change(); };
        value.TextChanged += (_, _) => { amount = double.TryParse(value.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var n) ? n : double.NaN; Change(); };
        units.Chosen += u => { unit = u; Change(); };
        stack.Children.Add(toggle);
        stack.Children.Add(targetRow);
        return stack;
    }

    static string Size(long bytes) => bytes switch
    {
        >= 1_000_000 => (bytes / 1_000_000.0).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " MB",
        >= 1_000 => (bytes / 1_000.0).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " KB",
        _ => bytes + " B",
    };

    /// Sets the target the way the fields do (the checks use it).
    public void Target(double value, string inUnit)
    {
        (target, amount, unit) = (true, value, inUnit);
        Change();
    }

    public void Preset(string value) => quality = value == Copy.Strong ? 45 : 80;
    public void Size(string choice) => size?.Choose(choice);

    public CompressOptions Settings => new()
    {
        Quality = quality,
        TargetBytes = target && unit != "percent" && double.IsFinite(amount) && amount > 0 ? (long)Math.Max(1, amount * (unit == "MB" ? 1_000_000 : 1_000)) : null,
        ReductionPercent = target && unit == "percent" ? amount : null,
        Scale = size?.Scale,
        LongestEdge = size?.LongestEdge,
        Width = size is { Custom: true } ? size.PixelWidth : null,
        Height = size is { Custom: true } ? size.PixelHeight : null,
    };

    public override string? Validate()
    {
        if (!target) return null;
        if (!double.IsFinite(amount) || amount <= 0) return L10n.T(Copy.Positive);
        if (unit == "percent" && amount >= 100) return L10n.T(Copy.Reduction);
        return null;
    }

    public override void Reset() => (preset, quality, target, amount, unit) = (Copy.Balanced, 80, false, 1, "MB");

    public override EditorJob Job()
    {
        var o = Settings;
        return (files, destination, progress, token) => Task.Run(() => ImageOperations.Compress(files, destination, progress, token, o), token);
    }
}
