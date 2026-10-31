using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MagickImage = ImageMagick.MagickImage;
using Nashi.Core;

namespace Nashi.Linux.Editors;

/// The full-size export an editor hands back on Save: its files, where they go, progress and cancel.
public delegate Task<IReadOnlyList<string>> EditorJob(IReadOnlyList<string> files, string destination, IProgress<OperationProgress> progress, CancellationToken token);

/// The first file of an editor: its preview copy (at most ImageTools.PreviewEdge) and full size.
public sealed record EditorSource(string Path, MagickImage Preview, double Scale, int Width, int Height)
{
    public Size Size => new(Width, Height);
}

/// One tool inside ToolEditorWindow (macOS ToolEditor's per-tool controls): its options, its live preview, its
/// marks on the preview and its export. Coordinates are source pixels from the top left.
public abstract class EditorPanel(string tool, IReadOnlyList<string> inputs)
{
    public string Tool { get; } = tool;
    /// The files to save, in order (Create Collage reorders them).
    public IReadOnlyList<string> Inputs { get; protected set; } = inputs;
    public abstract string Hint { get; }
    public abstract string Primary { get; }
    /// A preview beside the options; Compress and Metadata have the options only.
    public virtual bool SideBySide => true;
    /// The Original / Result toggle over the preview.
    public virtual bool ComparesResult => true;
    public virtual bool CanSave => true;
    /// Pointer work on the preview: crop, redact and annotate.
    public virtual bool Interactive => false;
    public EditorSource? Source { get; internal set; }
    /// Opens an image picker (backgrounds, stamps); the window sets it, the checks set their own.
    public Func<Task<string?>>? PickImage { get; set; }
    /// Options changed: the window re-renders the preview, the marks and the Save button.
    public event Action? Changed;
    /// Says something under the options (a failed preview, a refused save); null clears it.
    public event Action<string?>? Message;
    protected void Change() => Changed?.Invoke();
    internal void Say(string? message) => Message?.Invoke(message);

    /// Off the UI thread, before Options: anything besides the preview copy (metadata, more images).
    public virtual Task LoadAsync(CancellationToken token) => Task.CompletedTask;
    public abstract Control Options();
    /// Source pixels per preview pixel of what Preview renders (the collage renders its own canvas smaller).
    public virtual double PreviewScale => Source!.Scale;
    /// A snapshot of the options that renders the preview off the UI thread; null shows the source as is.
    public virtual Func<MagickImage>? Preview() => null;
    /// Why Save can't go ahead (already localized), or null.
    public virtual string? Validate() => null;
    /// A snapshot of the options as the full-size export.
    public abstract EditorJob Job();
    public virtual void Reset() { }
    /// Frees cached preview copies; the window calls it once no render can still use them.
    public virtual void Dispose() { }

    public virtual void Press(Point point, KeyModifiers modifiers, double tolerance) { }
    public virtual void Drag(Point point, KeyModifiers modifiers) { }
    public virtual void Release(Point point, KeyModifiers modifiers) { }
    /// Draws marks over the preview; `view` maps source pixels to the control.
    public virtual void Draw(DrawingContext context, Matrix view, double zoom) { }
    /// Delete / Backspace on the preview: removes the selected mark (and reports the change).
    public virtual bool Delete() => false;
    public virtual StandardCursorType Cursor(Point point, double tolerance) => StandardCursorType.Cross;

    /// One transaction of edited copies, off the UI thread.
    protected static EditorJob Each(string suffix, Func<string, MagickImage, MagickImage> work, bool png = false) =>
        (files, destination, progress, token) => Task.Run(() => ImageTools.Each(files, destination, suffix, work, progress, token, png), token);
}

/// The editor controls in the Nashi glass: section labels, captions, segmented pickers, sliders with a reset,
/// number fields and swatches.
static class Ui
{
    public static readonly IBrush Gold = new SolidColorBrush(Color.Parse("#E3B859"));

    /// A warning or error: plain text (white in dark, black in light) after a small warning sign. Never gold, which
    /// is only for the primary action and the selection.
    public static Control Warning(TextBlock text)
    {
        text.FontSize = 11;
        text.TextWrapping = TextWrapping.Wrap;
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("NashiText"));
        var sign = new PathIcon { Data = Geometry.Parse("F1 M8,1 L15.5,14.5 H0.5 Z M7.2,5.5 V10 H8.8 V5.5 Z M7.2,11.2 V12.8 H8.8 V11.2 Z"), Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        sign.Bind(PathIcon.ForegroundProperty, sign.GetResourceObservable("NashiTextSecondary"));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 7 };
        grid.Children.Add(sign);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    public static TextBlock Section(string english) => new()
    {
        Text = L10n.Upper(english), FontSize = 10, FontWeight = FontWeight.Bold, LetterSpacing = 0.8,
        Classes = { "secondary" }, Margin = new Thickness(0, 6, 0, 0),
    };

    public static TextBlock Heading(string english) => new() { Text = L10n.T(english), FontSize = 13, FontWeight = FontWeight.SemiBold, Classes = { "secondary" }, Margin = new Thickness(0, 8, 0, 0) };

    public static TextBlock Caption(string english) => new() { Text = L10n.T(english), FontSize = 11, TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } };

    public static Button Button(string english, Action click, string classes = "secondary")
    {
        var button = new Button { Content = L10n.T(english), Classes = { classes, "compact" } };
        button.Click += (_, _) => click();
        return button;
    }

    public static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.AddRange(children);
        return row;
    }

    public static WrapPanel Wrap(params Control[] children)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        wrap.Children.AddRange(children);
        return wrap;
    }

    /// A label, a slider, its value and a reset to `neutral` (macOS ToolEditor.slider).
    public static Control Slider(string english, double min, double max, double value, double neutral, Action<double> changed, string unit = "", Func<double, string>? format = null)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -6) };
        var shown = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Classes = { "secondary" } };
        void Show() => shown.Text = (format?.Invoke(slider.Value) ?? slider.Value.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture)) + (unit.Length > 0 ? " " + unit : "");
        Show();
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            Show();
            changed(slider.Value);
        };
        var reset = new Button { Content = new PathIcon { Data = Geometry.Parse("M8,2 A6,6 0 1 1 2.4,6 H4.2 A4.3,4.3 0 1 0 8,3.7 V6 L4.5,3 L8,0 Z"), Width = 10, Height = 10 }, Classes = { "quiet", "compact" }, Padding = new Thickness(5) };
        ToolTip.SetTip(reset, L10n.T("Reset"));
        reset.Click += (_, _) => slider.Value = neutral;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("105,*,64,Auto"), Tag = slider };
        grid.Children.Add(new TextBlock { Text = L10n.T(english), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        Grid.SetColumn(shown, 2);
        grid.Children.Add(shown);
        Grid.SetColumn(reset, 3);
        grid.Children.Add(reset);
        return grid;
    }

    /// The slider inside a Slider row (Reset sets it).
    public static Slider SliderOf(Control row) => (Slider)row.Tag!;

    /// A whole-number field; `changed` gets every valid value typed into it (not the ones Show puts there).
    public static NumberBox Number(int value, Action<int> changed, double width = 78) => new(value, changed) { Width = width };

    public static Control Labeled(string label, Control field) => Row(new TextBlock { Text = L10n.T(label), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 14 }, field);

    /// Colour dots; the chosen one has a gold ring.
    public static Control Swatches(IReadOnlyList<string> colors, string selected, Action<string> chosen)
    {
        var panel = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        var rings = new List<Border>();
        foreach (var hex in colors)
        {
            var color = Color.Parse(hex.Length == 9 ? "#" + hex[7..] + hex[1..7] : hex); // #RRGGBBAA → #AARRGGBB
            var dot = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1),
                Background = color.A == 0 ? new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Colors.White, .45), new GradientStop(Color.Parse("#E0605C"), .5), new GradientStop(Colors.White, .55) } } : new SolidColorBrush(color),
            };
            dot.Bind(Border.BorderBrushProperty, dot.GetResourceObservable("NashiBorder"));
            var ring = new Border { Padding = new Thickness(2), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2), Child = dot, Cursor = new Cursor(StandardCursorType.Hand), Tag = hex };
            ring.BorderBrush = hex == selected ? Gold : Brushes.Transparent;
            ring.PointerPressed += (_, _) =>
            {
                foreach (var r in rings) r.BorderBrush = Brushes.Transparent;
                ring.BorderBrush = Gold;
                chosen(hex);
            };
            rings.Add(ring);
            panel.Children.Add(ring);
        }
        return panel;
    }
}

/// A whole-number field. Text the editor shows (Show) is not reported back as typed, even though Avalonia raises
/// TextChanged for it later.
sealed class NumberBox : TextBox
{
    private string shown;
    protected override Type StyleKeyOverride => typeof(TextBox);

    public NumberBox(int value, Action<int> changed)
    {
        shown = Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        FontSize = 12;
        Padding = new Thickness(8, 5);
        MinHeight = 28;
        TextChanged += (_, _) =>
        {
            if (Text == shown) return;
            shown = "";
            if (int.TryParse(Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n)) changed(n);
        };
    }

    public void Show(int value)
    {
        shown = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Text = shown;
    }
}

/// A segmented picker (macOS .segmented): glass chips with the chosen one in gold.
sealed class Segments : Border
{
    private readonly List<(string Value, Button Button)> items = [];
    public string Value { get; private set; }
    public event Action<string>? Chosen;

    public Segments(IEnumerable<(string Value, string Label)> options, string value)
    {
        Value = value;
        Classes.Add("segments");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (v, label) in options)
        {
            var button = new Button { Content = L10n.T(label), Classes = { "segment" } };
            button.Click += (_, _) => { Select(v); Chosen?.Invoke(v); };
            items.Add((v, button));
            row.Children.Add(button);
        }
        Child = row;
        Select(value);
    }

    public Segments(IEnumerable<string> labels, string value) : this(labels.Select(l => (l, l)), value) { }

    /// Shows `value` as chosen without raising Chosen.
    public void Select(string value)
    {
        Value = value;
        foreach (var (v, button) in items) button.Classes.Set("chosen", v == value);
    }
}
