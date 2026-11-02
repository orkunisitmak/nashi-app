using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Nashi.Core;

namespace Nashi.Linux;

/// All exposed defaults are connected to an operation. Saving preserves shelf state changed while this is open.
public sealed class SettingsWindow : Window
{
    private readonly ComboBox appearance, gesture, output, ocrLanguage, language;
    private readonly TextBox folder;
    private readonly CheckBox sound, keepInTray, bubble;
    private readonly NumericUpDown opacity;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly PreferencesStore preferences = new(AppPaths.Data);
    private readonly Dictionary<string, OutputProfile> profiles;
    private readonly ComboBox format;
    private readonly NumericUpDown quality = Number(10, 100), bitrate = Number(8, 512), dpi = Number(36, 600), gifRate = Number(1, 60), gifWidth = Number(16, 8192);
    private readonly ComboBox depth = Choice(["matchSource", "pcm16", "pcm24", "pcm32", "float32"], "matchSource");
    private readonly CheckBox metadata = new() { Content = "Keep media metadata" };
    private readonly Control qualityRow, bitrateRow, depthRow, dpiRow, gifRateRow, gifWidthRow;
    private readonly List<WatchFolder> watches;
    private readonly StackPanel watchRows = new() { Spacing = 8 };
    private string shownFormat = "jpg";

    public SettingsWindow()
    {
        Title = "Settings · Nashi"; Width = 650; Height = 650; MinWidth = 520; MinHeight = 480;
        Desktop.Translucent(this);
        var settings = Settings.Current;
        profiles = new(preferences.Value.Formats);
        watches = [.. settings.WatchFolders];
        appearance = Choice(["system", "light", "dark"], settings.Appearance);
        language = Choice(["system", "en", "tr"], settings.Language);
        gesture = Choice(LinuxSettings.Keys, settings.GestureKey);
        ocrLanguage = Choice(["eng", "tur", "eng+tur"], settings.OcrLanguage);
        output = Choice(["beside", "ask", "folder"], settings.OutputMode);
        folder = new TextBox { Text = settings.OutputFolder, Watermark = "Choose an output folder" };
        sound = new CheckBox { Content = "Play wheel selection sound", IsChecked = settings.Sound };
        keepInTray = new CheckBox { Content = "Keep running when Home closes", IsChecked = settings.KeepInTray };
        bubble = new CheckBox { Content = "Show a floating drop target", IsChecked = settings.BubbleEnabled };
        opacity = Number(60, 100); opacity.Value = (decimal)(settings.WheelOpacity * 100);
        format = Choice(Capabilities.ImageFormats.Where(f => f != "pdf").Concat(Capabilities.AudioOutputs).Concat(Capabilities.VideoOutputs).Concat(["docx", "html", "rtf", "pages", "epub", "pptx"]).Distinct().Order().ToArray(), shownFormat);
        var general = Page();
        Add(general, "Appearance", appearance); Add(general, "Drag gesture", gesture);
        general.Children.Add(new TextBlock { Text = "Global drag gestures require X11. On Wayland, use Open With or drop files into Nashi.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        Add(general, "Wheel opacity (%)", opacity); general.Children.Add(sound);
        general.Children.Add(keepInTray);
        general.Children.Add(bubble);
        general.Children.Add(new TextBlock { Text = "The tray menu can reopen Nashi. Tray visibility depends on your desktop; launching Nashi again also reopens Home.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        Add(general, "OCR language for scanned PDF conversions", ocrLanguage);
        general.Children.Add(new TextBlock { Text = "Requires local Tesseract and the selected language packs. Digital PDF text does not need OCR.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        Add(general, "Language", language);
        general.Children.Add(new TextBlock { Text = "Language changes apply the next time Nashi starts.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var outputs = Page();
        outputs.Children.Add(new TextBlock { Text = "Save beside each source, choose a folder for each task, or always use the folder below.", TextWrapping = TextWrapping.Wrap });
        Add(outputs, "Save results", output); Add(outputs, "Output folder", folder);
        var browse = new Button { Content = "Choose folder…" }; browse.Click += async (_, _) => { if (await WheelFlow.PickLocalFolderAsync(this, "Output folder") is { } path) folder.Text = path; }; outputs.Children.Add(browse);
        outputs.Children.Add(new TextBlock { Text = "Originals are kept. Existing files receive a numbered name instead of being replaced.", TextWrapping = TextWrapping.Wrap });
        var conversion = Page();
        Add(conversion, "Format", format); qualityRow = Add(conversion, "Image / media quality (%)", quality);
        bitrateRow = Add(conversion, "Audio bitrate (kbps)", bitrate); depthRow = Add(conversion, "Audio sample depth", depth); conversion.Children.Add(metadata);
        dpiRow = Add(conversion, "PDF rendering resolution (DPI)", dpi); gifRateRow = Add(conversion, "GIF frames per second (video sources)", gifRate); gifWidthRow = Add(conversion, "GIF width (video sources, px)", gifWidth);
        conversion.Children.Add(new TextBlock { Text = "Defaults apply when the selected format supports the setting. An editor can override them for one task. Image conversion removes EXIF/GPS and keeps the color profile.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        LoadProfile();
        format.SelectionChanged += (_, _) => { StoreProfile(); shownFormat = format.SelectedItem as string ?? "jpg"; LoadProfile(); };
        var watched = Page();
        watched.Children.Add(new TextBlock { Text = "New files in these folders are processed beside their sources while Nashi is running. Existing files are left alone when a new rule starts.", TextWrapping = TextWrapping.Wrap });
        watched.Children.Add(watchRows); RefreshWatches();
        var add = new Button { Content = "Add watched folder…" };
        add.Click += async (_, _) =>
        {
            try
            {
                if (await WheelFlow.PickLocalFolderAsync(this, "Watch folder") is { } path)
                {
                    if (watches.Any(w => w.Path == path)) { status.Text = "That folder already has a rule."; return; }
                    if (watches.Count >= 50) { status.Text = "Use at most 50 watched folders."; return; }
                    watches.Add(new(path, "convert:png")); RefreshWatches();
                }
            }
            catch (Exception error) { status.Text = error.Message; }
        };
        watched.Children.Add(add);
        var tabs = new TabControl { ItemsSource = new[] { Tab("General", general), Tab("Output", outputs), Tab("Conversions", conversion), Tab("Watched folders", watched) } };
        var root = new DockPanel { Margin = new Thickness(24) };
        var footer = new StackPanel { Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };
        footer.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => Close(); actions.Children.Add(cancel);
        var save = new Button { Content = "Save changes", Name = "SaveSettings" }; save.Classes.Add("primary"); save.Click += (_, _) => { if (SaveChanges()) Close(); }; actions.Children.Add(save);
        footer.Children.Add(actions); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs); Content = root;
        if (preferences.RecoveryNotice is { } notice) status.Text = notice;
    }

    internal bool SaveChanges()
    {
        try
        {
            StoreProfile();
            var mode = (string)output.SelectedItem!;
            if (mode == "folder" && (string.IsNullOrWhiteSpace(folder.Text) || !Path.IsPathFullyQualified(folder.Text) || !Directory.Exists(folder.Text)))
                throw new ArgumentException("Choose an existing absolute output folder.");
            if (watches.Any(w => !Directory.Exists(w.Path))) throw new ArgumentException("A watched folder no longer exists. Remove it before saving.");
            preferences.Save(preferences.Value with { Formats = profiles });
            Settings.Update(s => s with { Language = (string)language.SelectedItem!, Appearance = (string)appearance.SelectedItem!, GestureKey = (string)gesture.SelectedItem!, OutputMode = mode,
                OutputFolder = folder.Text ?? "", OcrLanguage = (string)ocrLanguage.SelectedItem!, Sound = sound.IsChecked == true, KeepInTray = keepInTray.IsChecked == true,
                BubbleEnabled = bubble.IsChecked == true, WheelOpacity = (double)(opacity.Value ?? 100) / 100, WatchFolders = [.. watches] });
            ApplyAppearance(Settings.Current.Appearance);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { status.Text = error.Message; return false; }
    }

    internal static void ApplyAppearance(string value)
    { if (Application.Current is { } app) app.RequestedThemeVariant = value == "dark" ? ThemeVariant.Dark : value == "light" ? ThemeVariant.Light : ThemeVariant.Default; }

    void StoreProfile() => profiles[shownFormat] = profiles.GetValueOrDefault(shownFormat, new()) with
    { Quality = (double)(quality.Value ?? 85) / 100, AudioBitrate = (int)(bitrate.Value ?? 256), Dpi = (int)(dpi.Value ?? 144), GifFrameRate = (int)(gifRate.Value ?? 15), GifWidth = (int)(gifWidth.Value ?? 960), SampleDepth = (string)depth.SelectedItem!, PreserveMetadata = metadata.IsChecked == true };
    void LoadProfile()
    {
        var profile = profiles.GetValueOrDefault(shownFormat, new());
        quality.Value = (decimal)(profile.Quality * 100); bitrate.Value = profile.AudioBitrate; dpi.Value = profile.Dpi; gifRate.Value = profile.GifFrameRate; gifWidth.Value = profile.GifWidth; depth.SelectedItem = profile.SampleDepth; metadata.IsChecked = profile.PreserveMetadata;
        var audio = Capabilities.AudioOutputs.Contains(shownFormat);
        var video = Capabilities.VideoOutputs.Contains(shownFormat) && shownFormat != "gif";
        qualityRow.IsVisible = video || Capabilities.ImageFormats.Contains(shownFormat) && shownFormat is not ("pdf" or "gif");
        bitrateRow.IsVisible = metadata.IsVisible = audio || video;
        depthRow.IsVisible = shownFormat is "wav" or "aiff";
        dpiRow.IsVisible = shownFormat is "jpg" or "png" or "pptx" or "docx" or "html" or "rtf" or "pages" or "epub";
        gifRateRow.IsVisible = gifWidthRow.IsVisible = shownFormat == "gif";
    }
    void RefreshWatches()
    {
        watchRows.Children.Clear();
        foreach (var rule in watches.ToArray())
        {
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(new TextBlock { Text = rule.Path, TextWrapping = TextWrapping.Wrap });
            var choices = new[] { "convert:png", "convert:jpg", "convert:webp", "convert:pdf", "quick:zip" };
            var routes = Choice(choices.Contains(rule.Route) ? choices : choices.Append(rule.Route).ToArray(), rule.Route);
            var enabled = new CheckBox { Content = "Enabled", IsChecked = rule.Enabled };
            var index = watches.IndexOf(rule);
            routes.SelectionChanged += (_, _) => watches[index] = watches[index] with { Route = (string)routes.SelectedItem! };
            enabled.IsCheckedChanged += (_, _) => watches[index] = watches[index] with { Enabled = enabled.IsChecked == true };
            row.Children.Add(routes); row.Children.Add(enabled);
            var remove = new Button { Content = "Remove rule" }; remove.Click += (_, _) => { watches.RemoveAt(index); RefreshWatches(); }; row.Children.Add(remove);
            watchRows.Children.Add(row);
        }
    }
    static StackPanel Page() => new() { Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
    static TabItem Tab(string title, Control page) => new() { Header = new TextBlock { Text = title, FontSize = 14 }, Content = new ScrollViewer { Content = page } };
    static Control Add(Panel page, string title, Control input)
    { AutomationProperties.SetName(input, title); var row = new StackPanel { Spacing = 5 }; row.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Medium }); row.Children.Add(input); page.Children.Add(row); return row; }
    static ComboBox Choice(string[] values, string selected) => new()
    {
        ItemsSource = values, SelectedItem = selected, HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemTemplate = new FuncDataTemplate<string>((value, _) => new TextBlock { Text = Label(value ?? "") }),
    };
    static string Label(string value) => value switch
    {
        "beside" => "Next to each source", "ask" => "Ask every time", "folder" => "Use a folder",
        "system" => "Follow system", "light" => "Light", "dark" => "Dark", "matchSource" => "Match source",
        "pcm16" => "16-bit PCM", "pcm24" => "24-bit PCM", "pcm32" => "32-bit PCM", "float32" => "32-bit float",
        "en" or "eng" => "English", "tr" or "tur" => "Türkçe", "eng+tur" => "English + Türkçe", "quick:zip" => "Pack into ZIP",
        _ when value.StartsWith("convert:", StringComparison.Ordinal) => "Convert to " + value[8..].ToUpperInvariant(),
        _ => value,
    };
    static NumericUpDown Number(decimal min, decimal max) => new() { Minimum = min, Maximum = max, Increment = 1, FormatString = "0" };
}
