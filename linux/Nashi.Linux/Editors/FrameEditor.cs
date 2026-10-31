using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ImageMagick;
using Nashi.Core;

namespace Nashi.Linux.Editors;

/// Add BG (macOS frameControls): a solid, gradient or image background, its colour, blur, padding, corners, shadow,
/// canvas ratio and where the photo sits.
public sealed class FrameEditor(IReadOnlyList<string> inputs) : EditorPanel("frameImage", inputs)
{
    private static class Copy
    {
        public const string Hint = "Give it a little breathing room.";
        public const string Primary = "Save with background";
        public const string Background = "Background";
        public const string Solid = "Solid";
        public const string Gradient = "Gradient";
        public const string Image = "Image";
        public const string Color = "Color";
        public const string Transparent = "Transparent backgrounds save as PNG to keep their transparency.";
        public const string Choose = "Choose Background Image…";
        public const string Blur = "Blur";
        public const string Padding = "Padding";
        public const string Corners = "Corners";
        public const string Shadow = "Shadow";
        public const string Canvas = "Canvas";
        public const string Auto = "Auto";
        public const string Position = "Position";
    }

    /// macOS's gradient presets, with a graphite one in place of the green.
    public static readonly string[][] Gradients = [["#FF6B79", "#FFD85C", "#895CE0"], ["#4B79A1", "#283E51"], ["#232526", "#5B5E63"]];
    public static readonly string[] Colors = ["#FFFFFF", "#000000", "#FFFFFF00", "#F4EBDD", "#E3B859", "#FF6B79", "#4B79A1", "#895CE0"];

    private FrameOptions options = new();
    private readonly Dictionary<string, MagickImage> backgrounds = [];

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public FrameOptions Settings { get => options; set { options = value; Change(); } }

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 14 };
        var backdrop = new Segments([("solid", Copy.Solid), ("gradient", Copy.Gradient), ("image", Copy.Image)], options.Backdrop);
        var gradients = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, IsVisible = options.Backdrop == "gradient" };
        foreach (var colors in Gradients)
        {
            var swatch = new Border
            {
                Width = 60, Height = 36, CornerRadius = new CornerRadius(6), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), BorderThickness = new Thickness(2),
                Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = [.. colors.Select((c, i) => new GradientStop(Avalonia.Media.Color.Parse(c), i / (double)(colors.Length - 1)))] },
                BorderBrush = options.Gradient.SequenceEqual(colors) ? Ui.Gold : Brushes.Transparent,
            };
            swatch.PointerPressed += (_, _) =>
            {
                foreach (var other in gradients.Children.OfType<Border>()) other.BorderBrush = Brushes.Transparent;
                swatch.BorderBrush = Ui.Gold;
                Settings = options with { Backdrop = "gradient", Gradient = colors };
                backdrop.Select("gradient");
            };
            gradients.Children.Add(swatch);
        }
        var transparent = Ui.Caption(Copy.Transparent);
        transparent.IsVisible = ImageTools.Color(options.Color).Alpha < 255;
        async Task Pick()
        {
            if (PickImage is null || await PickImage() is not { } path) return;
            Settings = options with { Backdrop = "image", BackgroundImage = path };
            backdrop.Select("image");
            gradients.IsVisible = false;
        }
        var choose = Ui.Button(Copy.Choose, () => _ = Pick());
        backdrop.Chosen += value =>
        {
            gradients.IsVisible = value == "gradient";
            // Image without one chosen yet asks for it first.
            if (value == "image" && options.BackgroundImage is null) { backdrop.Select(options.Backdrop); _ = Pick(); return; }
            Settings = options with { Backdrop = value };
        };
        stack.Children.Add(Labeled(Copy.Background, backdrop));
        stack.Children.Add(gradients);
        stack.Children.Add(Labeled(Copy.Color, Ui.Swatches(Colors, options.Color, hex => { transparent.IsVisible = ImageTools.Color(hex).Alpha < 255; Settings = options with { Color = hex }; })));
        stack.Children.Add(transparent);
        stack.Children.Add(choose);
        stack.Children.Add(Ui.Slider(Copy.Blur, 0, 60, options.BackgroundBlur, 0, v => Settings = options with { BackgroundBlur = v }, "", Whole));
        stack.Children.Add(Ui.Slider(Copy.Padding, 0, 400, options.Padding, 160, v => Settings = options with { Padding = (int)Math.Round(v) }, "px", Whole));
        stack.Children.Add(Ui.Slider(Copy.Corners, 0, 100, options.CornerRadius, 24, v => Settings = options with { CornerRadius = v }, "px", Whole));
        stack.Children.Add(Ui.Slider(Copy.Shadow, 0, 80, options.ShadowRadius, 24, v => Settings = options with { ShadowRadius = v }, "px", Whole));
        var ratio = new Segments([Copy.Auto, "1:1", "16:9", "4:5"], options.AspectRatio switch { 1 => "1:1", { } r when Math.Abs(r - 16 / 9.0) < .001 => "16:9", { } r when Math.Abs(r - .8) < .001 => "4:5", _ => Copy.Auto });
        ratio.Chosen += value => Settings = options with { AspectRatio = value switch { "1:1" => 1, "16:9" => 16 / 9.0, "4:5" => 4 / 5.0, _ => null } };
        stack.Children.Add(Labeled(Copy.Canvas, ratio));
        stack.Children.Add(Labeled(Copy.Position, Positions()));
        return stack;
    }

    static string Whole(double v) => Math.Round(v).ToString(System.Globalization.CultureInfo.CurrentCulture);

    static Control Labeled(string label, Control control)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = L10n.T(label), FontSize = 12 });
        panel.Children.Add(control);
        return panel;
    }

    // Nine spots: top left to bottom right, the chosen one gold.
    private Control Positions()
    {
        var grid = new UniformGrid { Columns = 3, Rows = 3, Width = 84, Height = 60, HorizontalAlignment = HorizontalAlignment.Left };
        var dots = new List<Border>();
        for (var n = 0; n < 9; n++)
        {
            double x = n % 3 / 2.0, y = n / 3 / 2.0;
            var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(3), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Tag = (x, y) };
            void Paint() { foreach (var d in dots) { var (dx, dy) = ((double, double))d.Tag!; d.Background = dx == options.X && dy == options.Y ? Ui.Gold : null; if (d.Background is null) d.Bind(Border.BackgroundProperty, d.GetResourceObservable("NashiChipHover")); } }
            dot.PointerPressed += (_, _) => { options = options with { X = x, Y = y }; Paint(); Change(); };
            dots.Add(dot);
            grid.Children.Add(dot);
            if (n == 8) Paint();
        }
        return new Border { Classes = { "segments" }, Child = grid };
    }

    private MagickImage Background(string path)
    {
        lock (backgrounds)
        {
            if (!backgrounds.TryGetValue(path, out var small)) backgrounds[path] = small = ImageTools.PreviewCopy(path).Image;
            return (MagickImage)small.Clone();
        }
    }

    public override Func<MagickImage>? Preview()
    {
        var source = Source!;
        var o = options;
        return () => ImageTools.Frame(source.Preview, o, 1 / source.Scale, Background);
    }

    public override void Reset() => options = new();

    public override EditorJob Job()
    {
        var o = options;
        return Each(" - Framed", (_, image) => ImageTools.Frame(image, o), png: ImageTools.Color(o.Color).Alpha < 255);
    }
}
