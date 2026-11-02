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
                            var span = Wheel.SubSpan(360.0 / side.Items.Count, item.Group!.Count);
                            var width = Math.Min(Wheel.SubLabelWidth, Math.Max(30, span * Math.PI / 180 * (Wheel.SubInner + Wheel.SubOuter) / 2 - 12));
                            if (locale == "en") check(text.Width <= width, $"Outer caption {WheelCaptions.For(child)} fits the expanded ring at 9.5 pt ({text.Width:F1})");
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
            // Native control renders for sparse and dense menus; these are UI examples, not mockup artwork.
            foreach (var (parents, children) in new[] { (2, 1), (2, 2), (3, 3), (4, 2), (8, 6) })
            {
                var labels = new[] { "Crop", "Adjust", "B&W", "90° L", "90° R", "Flip H" };
                var options = labels.Take(children).Select((title, i) => new WheelItem("sample:" + i, title)).ToArray();
                var main = Enumerable.Range(0, parents).Select(i => i == 0
                    ? new WheelItem("group:edit", "Edit", Group: options)
                    : new WheelItem("group:sample" + i, i % 2 == 0 ? "Create" : "Files")).ToArray();
                var sample = new WheelWindow("", []);
                sample.Wheel.Sides = [new WheelSide("Tools", main, "tools")];
                sample.Show();
                Dispatcher.UIThread.RunJobs();
                sample.Wheel.OpenGroup(0);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                Dispatcher.UIThread.RunJobs();
                sample.CaptureRenderedFrame();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                smoke(sample.CaptureRenderedFrame(), $"wheel-adaptive-{parents}-main-{children}-sub");
                sample.Close();
            }

        }
        finally { L10n.Language = language; }
    }
}
