using Avalonia.Controls;
using Avalonia.Layout;
using ImageMagick;
using Nashi.Core;

namespace Nashi.Linux.Editors;

/// The size picker of Edit Photo and Compress (macOS compressionSize): original, a percentage, a longest edge, or
/// custom W/H fields.
sealed class SizePicker : StackPanel
{
    private static class Copy
    {
        public const string Size = "Image size";
        public const string Original = "Keep original dimensions";
        public const string Large = "Large · 2560 px longest edge";
        public const string Medium = "Medium · 1920 px longest edge";
        public const string Small = "Small · 1280 px longest edge";
        public const string Custom = "Custom dimensions";
    }

    public static readonly (string Id, string Label)[] Choices = [("original", Copy.Original), ("75", "75%"), ("50", "50%"), ("25", "25%"), ("large", Copy.Large), ("medium", Copy.Medium), ("small", Copy.Small), ("custom", Copy.Custom)];

    private readonly ComboBox picker = new() { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
    private readonly NumberBox width, height;
    private readonly Control fields;
    private readonly Action changed;
    private int sourceWidth, sourceHeight;

    public string Choice { get; private set; } = "original";
    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }

    public SizePicker(int sourceWidth, int sourceHeight, Action changed, string choice = "original")
    {
        this.changed = changed;
        (this.sourceWidth, this.sourceHeight) = (sourceWidth, sourceHeight);
        Spacing = 8;
        picker.ItemsSource = Choices.Select(c => L10n.T(c.Label)).ToList();
        width = Ui.Number(sourceWidth, v => { if (v > 0) { PixelWidth = v; Choose("custom", fill: false); } }, 96);
        height = Ui.Number(sourceHeight, v => { if (v > 0) { PixelHeight = v; Choose("custom", fill: false); } }, 96);
        fields = Ui.Row(Ui.Labeled("W", width), Ui.Labeled("H", height), new TextBlock { Text = "px", FontSize = 12, Classes = { "secondary" }, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(new TextBlock { Text = L10n.T(Copy.Size), FontSize = 12 });
        Children.Add(picker);
        Children.Add(fields);
        picker.SelectionChanged += (_, _) => { if (picker.SelectedIndex >= 0 && Choices[picker.SelectedIndex].Id != Choice) Choose(Choices[picker.SelectedIndex].Id); };
        Choose(choice);
    }

    /// The finished size is turned: W and H swap (Rotate Left/Right).
    public void Turn()
    {
        (sourceWidth, sourceHeight) = (sourceHeight, sourceWidth);
        if (Choice == "custom") (PixelWidth, PixelHeight) = (PixelHeight, PixelWidth);
        Choose(Choice);
    }

    public void Choose(string id, bool fill = true)
    {
        Choice = id;
        picker.SelectedIndex = Array.FindIndex(Choices, c => c.Id == id);
        if (fill && id != "custom")
        {
            var (w, h) = ImageTools.TargetSize(sourceWidth, sourceHeight, Scale, LongestEdge, null, null);
            (PixelWidth, PixelHeight) = (w, h);
        }
        width.Show(PixelWidth);
        height.Show(PixelHeight);
        changed();
    }

    /// A percentage preset, as a factor of each image's own size.
    public double? Scale => Choice is "75" or "50" or "25" ? int.Parse(Choice, System.Globalization.CultureInfo.InvariantCulture) / 100.0 : null;
    public int? LongestEdge => Choice switch { "large" => 2560, "medium" => 1920, "small" => 1280, _ => null };
    public bool Custom => Choice == "custom";
}

/// Edit Photo (macOS imageControls): rotate, flip, size, and the light, colour and detail sliders with the macOS
/// ranges. Contrast, gamma and saturation show their change from 1, as macOS does.
public sealed class PhotoEditor(IReadOnlyList<string> inputs) : EditorPanel("editImage", inputs)
{
    private static class Copy
    {
        public const string Hint = "Little tweaks. A whole new look.";
        public const string Primary = "Save edited copy";
        public const string RotateLeft = "Rotate Left";
        public const string RotateRight = "Rotate Right";
        public const string FlipHorizontal = "Flip horizontally";
        public const string FlipVertical = "Flip vertically";
        public const string Lighting = "Lighting";
        public const string Color = "Color";
        public const string Detail = "Detail";
    }

    private sealed record Setting(string Label, double Min, double Max, double Neutral, Func<PhotoAdjustments, double> Get, Func<PhotoAdjustments, double, PhotoAdjustments> Set, string Unit = "");

    // macOS ToolEditor.imageControls, in its order and ranges. The relative ones store 1 + the slider.
    private static readonly (string Group, Setting[] Settings)[] Groups =
    [
        (Copy.Lighting, [
            new("Exposure", -3, 3, 0, a => a.Exposure, (a, v) => a with { Exposure = v }, "EV"),
            new("Brightness", -1, 1, 0, a => a.Brightness, (a, v) => a with { Brightness = v }),
            new("Contrast", -1, 1, 0, a => a.Contrast - 1, (a, v) => a with { Contrast = v + 1 }),
            new("Highlights", -1, 1, 0, a => a.Highlights, (a, v) => a with { Highlights = v }),
            new("Shadows", -1, 1, 0, a => a.Shadows, (a, v) => a with { Shadows = v }),
            new("Whites", -1, 1, 0, a => a.Whites, (a, v) => a with { Whites = v }),
            new("Blacks", -1, 1, 0, a => a.Blacks, (a, v) => a with { Blacks = v }),
            new("Gamma", -.8, 2, 0, a => a.Gamma - 1, (a, v) => a with { Gamma = v + 1 }),
        ]),
        (Copy.Color, [
            new("Temperature", -1, 1, 0, a => a.Temperature, (a, v) => a with { Temperature = v }),
            new("Tint", -1, 1, 0, a => a.Tint, (a, v) => a with { Tint = v }),
            new("Saturation", -1, 1, 0, a => a.Saturation - 1, (a, v) => a with { Saturation = v + 1 }),
            new("Vibrance", -1, 1, 0, a => a.Vibrance, (a, v) => a with { Vibrance = v }),
            new("Hue", -180, 180, 0, a => a.Hue, (a, v) => a with { Hue = v }, "°"),
        ]),
        (Copy.Detail, [
            new("Clarity", 0, 1, 0, a => a.Clarity, (a, v) => a with { Clarity = v }),
            new("Texture", -1, 1, 0, a => a.Texture, (a, v) => a with { Texture = v }),
            new("Sharpen", 0, 2, 0, a => a.Sharpen, (a, v) => a with { Sharpen = v }),
            new("Dehaze", 0, 1, 0, a => a.Dehaze, (a, v) => a with { Dehaze = v }),
            new("Grain", 0, 1, 0, a => a.Grain, (a, v) => a with { Grain = v }),
            new("Noise reduction", 0, 1, 0, a => a.NoiseReduction, (a, v) => a with { NoiseReduction = v }),
            new("Vignette", 0, 2, 0, a => a.Vignette, (a, v) => a with { Vignette = v }),
            new("Fade", 0, 1, 0, a => a.Fade, (a, v) => a with { Fade = v }),
            new("Blur", 0, 30, 0, a => a.Blur, (a, v) => a with { Blur = v }, "px"),
        ]),
    ];

    private PhotoOptions options = new();
    private SizePicker? size;
    private readonly Dictionary<string, Slider> sliders = [];

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public PhotoOptions Settings => Current();

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 12 };
        Button? flipH = null, flipV = null;
        void ShowFlips()
        {
            flipH!.Classes.Set("chosen", options.FlipHorizontal);
            flipV!.Classes.Set("chosen", options.FlipVertical);
            Change();
        }
        flipH = Ui.Button(Copy.FlipHorizontal, () => { options = options with { FlipHorizontal = !options.FlipHorizontal }; ShowFlips(); }, "segment");
        flipV = Ui.Button(Copy.FlipVertical, () => { options = options with { FlipVertical = !options.FlipVertical }; ShowFlips(); }, "segment");
        stack.Children.Add(Ui.Wrap(Ui.Button(Copy.RotateLeft, () => Rotate(-90)), Ui.Button(Copy.RotateRight, () => Rotate(90))));
        var flips = new Border { Classes = { "segments" }, Child = Ui.Row(flipH, flipV) };
        ((StackPanel)flips.Child).Spacing = 2;
        stack.Children.Add(flips);
        ShowFlips();
        var turned = options.Rotation / 90 % 2 != 0;
        size = new SizePicker(turned ? Source!.Height : Source!.Width, turned ? Source.Width : Source.Height, Change);
        stack.Children.Add(size);
        sliders.Clear();
        foreach (var (group, settings) in Groups)
        {
            stack.Children.Add(Ui.Heading(group));
            foreach (var setting in settings)
            {
                var row = Ui.Slider(setting.Label, setting.Min, setting.Max, setting.Get(options.Adjustments), setting.Neutral, v =>
                {
                    options = options with { Adjustments = setting.Set(options.Adjustments, v) };
                    Change();
                }, setting.Unit);
                sliders[setting.Label] = Ui.SliderOf(row);
                stack.Children.Add(row);
            }
        }
        return stack;
    }

    /// Sets one slider by its English label (the checks drive it this way).
    public void Adjust(string label, double value) => sliders[label].Value = value;

    public void Rotate(int degrees)
    {
        options = options with { Rotation = options.Rotation + degrees };
        size?.Turn();
        Change();
    }

    public void Flip(bool horizontal)
    {
        options = horizontal ? options with { FlipHorizontal = !options.FlipHorizontal } : options with { FlipVertical = !options.FlipVertical };
        Change();
    }

    public void Size(string choice) => size?.Choose(choice);

    private PhotoOptions Current() => size is null ? options : options with
    {
        Scale = size.Scale,
        LongestEdge = size.LongestEdge,
        Width = size.Custom ? size.PixelWidth : null,
        Height = size.Custom ? size.PixelHeight : null,
    };

    public override Func<MagickImage>? Preview()
    {
        var source = Source!;
        var o = Current();
        var f = source.Scale;
        // The preview copy is 1/f the size: pixel sizes shrink with it, so the preview looks like the export.
        var preview = o with
        {
            Width = o.Width is { } w ? Math.Max(1, (int)Math.Round(w / f)) : null,
            Height = o.Height is { } h ? Math.Max(1, (int)Math.Round(h / f)) : null,
            LongestEdge = o.LongestEdge is { } edge ? Math.Max(1, (int)Math.Round(Math.Min(edge, Math.Max(source.Width, source.Height)) / f)) : null,
            Adjustments = o.Adjustments with { Blur = o.Adjustments.Blur / f },
        };
        return () => ImageTools.Edit(source.Preview, preview);
    }

    public override void Reset() => options = new();

    public override EditorJob Job()
    {
        var o = Current();
        return Each(" - Edited", (_, image) => ImageTools.Edit(image, o));
    }
}
