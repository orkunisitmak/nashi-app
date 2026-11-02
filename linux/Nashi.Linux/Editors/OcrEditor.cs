using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nashi.Core;

namespace Nashi.Linux.Editors;

/// Review locally recognized text before copying or exporting it. Closing/cancelling kills the active OCR process.
public sealed class OcrEditor(IReadOnlyList<string> inputs, OcrEngine engine) : EditorPanel("recognizeText", inputs)
{
    const string Install = "Install Tesseract and its English/Turkish language packs, then check again.";
    readonly ComboBox language = new() { MinWidth = 180 };
    readonly ComboBox layout = new() { MinWidth = 180 };
    readonly ComboBox files = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox pages = new() { Watermark = "1-3, 5", MaxWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBox password = new() { PasswordChar = '•', MaxWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBox text = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 220, MaxHeight = 420 };
    readonly CheckBox existing = new() { IsChecked = true };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    readonly Button recognize = new() { Classes = { "secondary", "compact" } };
    readonly Button cancel = new() { Classes = { "quiet", "compact" } };
    readonly Button copy = new() { Classes = { "secondary", "compact" } };
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? active;
    OcrStatus? installed;
    List<OcrResult> results = [];
    bool showing;
    string shown = "";
    StackPanel? content;
    (string, string)[] choices = [];
    readonly StackPanel installPanel = new() { Spacing = 8 };
    public Task Pending { get; private set; } = Task.CompletedTask;
    public IReadOnlyList<OcrResult> Results => results;
    public bool Running => active is not null;
    Grid? stage;
    public override bool SideBySide => true;
    public override bool ComparesResult => false;
    public override Control Stage
    {
        get
        {
            if (stage is not null) return stage;
            stage = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
            stage.Children.Add(files);
            text.MaxHeight = double.PositiveInfinity;
            text.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetRow(text, 1); stage.Children.Add(text);
            Grid.SetRow(copy, 2); stage.Children.Add(copy);
            return stage;
        }
    }
    public override bool ImageSource => false;
    public override bool Canvas => false;
    public override string Hint => "Read text locally. Review it before saving.";
    public override string Primary => "Save text";
    public override bool CanSave => !Running && results.Any(r => !string.IsNullOrWhiteSpace(r.Text));

    public override async Task LoadAsync(CancellationToken token)
    {
        installed = null;
        try { installed = await engine.ProbeAsync(token); }
        catch (Exception error) when (error is not OperationCanceledException) { status.Text = L10n.T(error.Message); }
    }

    public override Control Options()
    {
        if (content is not null) return content;
        var body = content = new StackPanel { Spacing = 12 };
        body.Children.Add(Ui.Caption("Recognition runs on this computer. No files are uploaded."));
        installPanel.Children.Add(Ui.Caption(Install));
        installPanel.Children.Add(new TextBox { Text = "sudo apt install tesseract-ocr tesseract-ocr-eng tesseract-ocr-tur", IsReadOnly = true, TextWrapping = TextWrapping.Wrap });
        installPanel.Children.Add(Ui.Caption("Ubuntu/Debian example. Use your distribution's package manager."));
        body.Children.Add(installPanel);
        body.Children.Add(Ui.Button("Check again", async () =>
        {
            try { await LoadAsync(lifetime.Token); Configure(); }
            catch (OperationCanceledException) { }
        }));
        layout.ItemsSource = new[] { L10n.T("Automatic layout"), L10n.T("Single text block"), L10n.T("Scattered text") };
        layout.SelectedIndex = 0;
        body.Children.Add(Ui.Labeled("Language", language));
        body.Children.Add(Ui.Labeled("Page layout", layout));
        if (Inputs.Any(f => FeatureCatalog.Extension(f) == "pdf"))
        {
            body.Children.Add(Ui.Labeled("PDF pages (blank = all)", pages));
            body.Children.Add(Ui.Labeled("PDF password (if needed)", password));
            existing.Content = L10n.T("Use existing PDF text when available");
            body.Children.Add(existing);
        }
        recognize.Content = L10n.T("Recognize Text");
        cancel.Content = L10n.T("Cancel recognition");
        cancel.IsEnabled = false;
        recognize.Click += (_, _) =>
        {
            if (language.SelectedIndex < 0 || Running) return;
            Pending = RecognizeAsync(new OcrOptions(choices[language.SelectedIndex].Item2,
                new[] { 3, 6, 11 }[layout.SelectedIndex], pages.Text ?? "", password.Text ?? "", existing.IsChecked == true));
        };
        cancel.Click += (_, _) => active?.Cancel();
        body.Children.Add(Ui.Row(recognize, cancel));
        if (choices.Length == 0) status.Text = L10n.T("Install the English or Turkish Tesseract language pack, then check again.");
        body.Children.Add(status);
        files.SelectionChanged += (_, _) => ShowText();
        text.TextChanged += (_, _) =>
        {
            if (showing || text.Text == shown || files.SelectedIndex < 0 || files.SelectedIndex >= results.Count) return;
            results[files.SelectedIndex] = results[files.SelectedIndex] with { Text = text.Text ?? "" };
            shown = text.Text ?? "";
            Change();
        };
        files.ItemsSource = results.Select(r => Path.GetFileName(r.Input)).ToArray();
        files.SelectedIndex = results.Count > 0 ? 0 : -1;
        files.IsVisible = results.Count > 1;
        copy.Content = L10n.T("Copy text");
        copy.IsEnabled = results.Count > 0;
        copy.Click += async (_, _) =>
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(text)?.Clipboard;
                if (clipboard is not null) await clipboard.SetTextAsync(text.Text ?? "");
            }
            catch (Exception error) { status.Text = L10n.T(error.Message); }
        };
        Configure();
        return body;
    }

    void Configure()
    {
        choices = new[] { ("English", "eng"), ("Türkçe", "tur"), ("English + Türkçe", "eng+tur") }
            .Where(pair => installed is not null && pair.Item2.Split('+').All(installed.Languages.Contains))
            .Concat((installed?.Languages ?? []).Where(l => l is not ("eng" or "tur" or "osd")).Select(l => (l, l))).ToArray();
        language.ItemsSource = choices.Select(c => c.Item1).ToArray();
        language.SelectedIndex = choices.Length > 0 ? 0 : -1;
        recognize.IsEnabled = choices.Length > 0;
        installPanel.IsVisible = installed is null || !new[] { "eng", "tur" }.All(installed.Languages.Contains);
        if (installed is not null) status.Text = installed.Version;
        if (installed is not null && choices.Length == 0) status.Text = L10n.T("Install the English or Turkish Tesseract language pack, then check again.");
    }

    void ShowText()
    {
        showing = true;
        shown = files.SelectedIndex >= 0 && files.SelectedIndex < results.Count ? results[files.SelectedIndex].Text : "";
        text.Text = shown;
        showing = false;
    }

    public async Task RecognizeAsync(OcrOptions options)
    {
        if (Running) return;
        active = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = active.Token;
        results = [];
        text.Text = "";
        copy.IsEnabled = recognize.IsEnabled = language.IsEnabled = layout.IsEnabled = pages.IsEnabled = password.IsEnabled = existing.IsEnabled = false;
        cancel.IsEnabled = true;
        Change();
        try
        {
            var progress = new Progress<OperationProgress>(p => status.Text = p.Label);
            results = (await Task.Run(() => engine.RecognizeAsync(Inputs, options, progress, token), token)).ToList();
            if (token.IsCancellationRequested) return;
            files.ItemsSource = results.Select(r => Path.GetFileName(r.Input)).ToArray();
            files.SelectedIndex = results.Count > 0 ? 0 : -1;
            files.IsVisible = results.Count > 1;
            ShowText();
            status.Text = L10n.T(results.Any(r => !string.IsNullOrWhiteSpace(r.Text)) ? "Review the text before saving." : "No text was found. Try another language or page layout.");
            copy.IsEnabled = results.Count > 0;
        }
        catch (OperationCanceledException) { status.Text = L10n.T("Recognition cancelled."); }
        catch (Exception error) { status.Text = L10n.T(error.Message); }
        finally
        {
            active.Dispose(); active = null;
            recognize.IsEnabled = language.IsEnabled = layout.IsEnabled = pages.IsEnabled = password.IsEnabled = existing.IsEnabled = true;
            cancel.IsEnabled = false;
            Change();
        }
    }

    public override EditorJob Job()
    {
        if (files.SelectedIndex >= 0 && files.SelectedIndex < results.Count)
            results[files.SelectedIndex] = results[files.SelectedIndex] with { Text = text.Text ?? "" };
        var snapshot = results.ToArray();
        return (files, destination, _, token) => Task.Run(() => OcrEngine.Save(snapshot, files, destination, token), token);
    }
    public override void Reset() { active?.Cancel(); results = []; showing = true; shown = text.Text = ""; showing = false; files.ItemsSource = Array.Empty<string>(); files.IsVisible = false; copy.IsEnabled = false; Change(); }
    public override void Dispose() { lifetime.Cancel(); active?.Cancel(); lifetime.Dispose(); }
}
