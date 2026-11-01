using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Nashi.Linux;

static class CaptionChecks
{
    public static void Run(Action<bool, string> check, Action<Avalonia.Media.Imaging.WriteableBitmap?, string> smoke)
    {
        var language = L10n.Language;
        try
        {
            foreach (var locale in new[] { "en", "tr" })
            {
                L10n.Language = locale;
                foreach (var extension in Capabilities.InputExtensions)
                foreach (var count in new[] { 1, 2 })
                foreach (var side in WheelFlow.Sides(Enumerable.Range(0, count).Select(i => $"file{i}.{extension}").ToArray()))
                {
                    for (var i = 0; i < side.Items.Count; i++)
                    {
                        var item = side.Items[i];
                        var caption = WheelCaptions.For(item);
                        var icon = WheelIcons.ForId(item.Id) is not null;
                        var layout = Wheel.Layout(side.Items.Count, i, caption, icon, singleLine: true);
                        check(layout.Fits && layout.Lines.Count == 1 && layout.FontSize == (icon ? 9.5 : 13)
                            && Wheel.Rects(layout).All(rect => Wheel.Contains(Wheel.Petal(side.Items.Count, i), rect)),
                            $"{locale} {count} {extension}: {item.Id} fits one line at its normal font size");
                        if (locale == "en") check(layout.Lines[0] == caption, $"{extension}: {caption} fits without an ellipsis");
                        foreach (var child in item.Group ?? [])
                        {
                            var text = new FormattedText(WheelCaptions.For(child), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 9.5, Brushes.Black);
                            if (locale == "en") check(text.Width <= Wheel.SubBand - 12, $"Outer caption {WheelCaptions.For(child)} fits the expanded ring at 9.5 pt ({text.Width:F1})");
                        }
                    }
                }
            }
            L10n.Language = "en";
            var window = new WheelWindow("", []);
            window.Wheel.Sides = WheelFlow.Sides(["sample.wav"]);
            window.Wheel.StartAt(1);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var wheel = window.Wheel;
            wheel.OpenGroup(wheel.Items.ToList().FindIndex(item => item.Id == "group:audio"));
            window.Wheel.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            check(window.FindControl<TextBlock>("DescriptionText")?.Text == "Convert Audio Channels"
                && WheelCaptions.For(wheel.SubItems[wheel.SubHighlightedIndex]) == "Channels",
                "A short outer caption shows the full action name below the wheel for keyboard selection");
            check(window.FindControl<Border>("DescriptionCard") is { IsVisible: true, IsHitTestVisible: false },
                "The full-name caption is visible and cannot intercept a drag or click");
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            smoke(window.CaptureRenderedFrame(), "wheel-single-line-caption");
            wheel.HighlightAt(new Point(wheel.Bounds.Width / 2, wheel.Bounds.Height / 2));
            check(window.FindControl<Border>("DescriptionCard")?.IsVisible == false, "Leaving the petals hides the full-name caption");
            window.Close();
        }
        finally { L10n.Language = language; }
    }
}
