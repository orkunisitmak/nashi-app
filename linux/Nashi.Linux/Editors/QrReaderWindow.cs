using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MagickImage = ImageMagick.MagickImage;
using MagickReadSettings = ImageMagick.MagickReadSettings;
using MagickFormat = ImageMagick.MagickFormat;
using Nashi.Core;
using Path = Avalonia.Controls.Shapes.Path;

namespace Nashi.Linux.Editors;

/// One QR code found: the file, its PDF page (1-based) or null, and the text.
public sealed record QrResult(string Source, int? Page, string Text)
{
    /// Only web links open: http and https, never file:, javascript: or a program.
    public Uri? Link => Uri.TryCreate(Text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
}

/// Read QR Codes (macOS QRCodeReaderView): scans the images and PDF pages straight away with ZXing, then shows each
/// code in a card with Copy (and Open for web links), Scan again, a count and Copy all.
public sealed class QrReaderWindow : Window
{
    private static class Copy
    {
        public const string Title = "Read QR Codes";
        public const string Hint = "A little code, decoded.";
        public const string Looking = "Looking for little squares…";
        public const string None = "No QR codes this time.";
        public const string Try = "Try a clearer image or another PDF.";
        public const string Page = "Page {0}";
        public const string CopyOne = "Copy";
        public const string Copied = "Copied";
        public const string Open = "Open";
        public const string Again = "Scan again";
        public const string Found = "{0} found";
        public const string CopyAll = "Copy all";
        public const string CopiedAll = "Copied all";
        public const string Cancel = "Cancel";
        public const string Close = "Close";
    }

    private readonly IReadOnlyList<string> inputs;
    private readonly StackPanel cards = new() { Spacing = 14, Margin = new Thickness(20) };
    private readonly StackPanel busy = new() { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(20, 14), IsVisible = false };
    private readonly TextBlock label = new() { FontSize = 11, Classes = { "secondary" }, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock count = new();
    private readonly Button again = new() { Classes = { "quiet", "compact" } };
    private readonly Button copyAll = new() { Classes = { "primary", "compact" } };
    private CancellationTokenSource? scan;
    private List<QrResult> results = [];
    private List<string> errors = [];

    public IReadOnlyList<QrResult> Results => results;
    /// The scan in progress (or the last one).
    public Task Scanning { get; private set; } = Task.CompletedTask;

    public QrReaderWindow(IReadOnlyList<string> inputs)
    {
        this.inputs = inputs;
        Desktop.Translucent(this);
        Title = L10n.T(Copy.Title) + " · Nashi";
        Width = 560;
        Height = 520;
        MinWidth = 530;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var close = new Button { Classes = { "quiet" }, Padding = new Thickness(5), Content = new PathIcon { Data = (Geometry)Application.Current!.FindResource("IconClose")!, Width = 12, Height = 12 }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(close, L10n.T(Copy.Close));
        close.Click += (_, _) => Close();
        var icon = new Path { Data = WheelIcons.ForId("readQRCode"), StrokeThickness = WheelIcons.Stroke, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Width = 23, Height = 23, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, icon.GetResourceObservable("NashiGoldInk"));
        var titles = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = L10n.T(Copy.Title), FontSize = 21, FontWeight = FontWeight.Bold, LetterSpacing = -0.5 });
        titles.Children.Add(new TextBlock { Text = L10n.T(Copy.Hint), FontSize = 11, Classes = { "secondary" } });
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 13, Margin = new Thickness(20), Background = Brushes.Transparent };
        header.Children.AddRange([close, icon, titles]);
        header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };

        var cancel = Ui.Button(Copy.Cancel, () => scan?.Cancel());
        var looking = new StackPanel { Spacing = 3 };
        looking.Children.Add(new TextBlock { Text = L10n.T(Copy.Looking), FontSize = 12, FontWeight = FontWeight.SemiBold });
        looking.Children.Add(label);
        busy.Children.AddRange([new ProgressBar { IsIndeterminate = true, Width = 60, Height = 4, MinHeight = 4, VerticalAlignment = VerticalAlignment.Center, Foreground = (IBrush?)Application.Current!.FindResource("NashiPrimaryFill") }, looking, cancel]);

        again.Content = L10n.T(Copy.Again);
        again.Click += (_, _) => Start();
        copyAll.Click += async (_, _) => await CopyAsync(string.Join("\n", results.Select(r => r.Text)), copyAll, Copy.CopiedAll);
        var pill = new Border { Classes = { "pill" }, Child = count };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 10, Margin = new Thickness(20) };
        footer.Children.Add(again);
        Grid.SetColumn(pill, 2);
        footer.Children.Add(pill);
        Grid.SetColumn(copyAll, 3);
        footer.Children.Add(copyAll);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto") };
        root.Children.Add(header);
        root.Children.Add(Line(1));
        Grid.SetRow(busy, 2);
        root.Children.Add(busy);
        var scroller = new ScrollViewer { Content = cards };
        Grid.SetRow(scroller, 3);
        root.Children.Add(scroller);
        root.Children.Add(Line(4));
        Grid.SetRow(footer, 5);
        root.Children.Add(footer);
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) Close(); };
        Closed += (_, _) => scan?.Cancel();
        Start();
    }

    static Border Line(int row)
    {
        var line = new Border { Height = 1 };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("NashiBorder"));
        Grid.SetRow(line, row);
        return line;
    }

    /// Shows the reader over `owner` (Home) or activated on its own (the pointer flow).
    public void Present(Window? owner)
    {
        if (owner is not null) { Show(owner); return; }
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Opened += (_, _) => { Activate(); Desktop.Activate(this, 0); };
        Show();
    }

    private void Start()
    {
        if (!Scanning.IsCompleted) return;
        scan = new CancellationTokenSource();
        Scanning = ScanAsync(scan.Token);
    }

    private async Task ScanAsync(CancellationToken token)
    {
        busy.IsVisible = true;
        again.IsEnabled = false;
        results = [];
        errors = [];
        Fill();
        var progress = new Progress<string>(name => label.Text = name);
        try { (results, errors) = await Task.Run(() => Scan(inputs, progress, token), token); }
        catch (OperationCanceledException) { }
        busy.IsVisible = false;
        again.IsEnabled = true;
        Fill();
    }

    /// Every QR code in the files, in order, plus a message for each file that couldn't be read.
    public static (List<QrResult>, List<string>) Scan(IReadOnlyList<string> files, IProgress<string>? progress, CancellationToken token)
    {
        var found = new List<QrResult>();
        var failed = new List<string>();
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var name = System.IO.Path.GetFileName(file);
            progress?.Report(name);
            try
            {
                if (FeatureCatalog.Extension(file) == "pdf")
                {
                    var pages = PdfRaster.RenderAsync(file, 144, token).GetAwaiter().GetResult();
                    for (var n = 0; n < pages.Length; n++)
                    {
                        using var page = new MagickImage(pages[n], new MagickReadSettings { Format = MagickFormat.Png });
                        found.AddRange(ImageTools.QrCodes(page).Select(text => new QrResult(name, n + 1, text)));
                    }
                }
                else
                {
                    using var image = PhotoCodec.Read(file);
                    found.AddRange(ImageTools.QrCodes(image).Select(text => new QrResult(name, null, text)));
                }
            }
            catch (Exception error) when (error is not OperationCanceledException) { failed.Add(name + ": " + L10n.T(error.Message)); }
        }
        return (found, failed);
    }

    private void Fill()
    {
        cards.Children.Clear();
        count.Text = L10n.F(Copy.Found, results.Count);
        copyAll.Content = L10n.T(Copy.CopyAll);
        copyAll.IsEnabled = results.Count > 0 && !busy.IsVisible;
        if (results.Count == 0 && !busy.IsVisible)
        {
            var empty = new StackPanel { Spacing = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 45) };
            var mark = new Path { Data = WheelIcons.ForId("readQRCode"), StrokeThickness = 1.2, Width = 44, Height = 44, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
            mark.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, mark.GetResourceObservable("NashiTextSecondary"));
            empty.Children.Add(mark);
            empty.Children.Add(new TextBlock { Text = L10n.T(Copy.None), FontSize = 19, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center });
            empty.Children.Add(new TextBlock { Text = L10n.T(Copy.Try), FontSize = 12, Classes = { "secondary" }, HorizontalAlignment = HorizontalAlignment.Center });
            cards.Children.Add(empty);
        }
        foreach (var result in results)
        {
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 8 };
            top.Children.Add(new TextBlock { Text = result.Source, FontSize = 11, FontWeight = FontWeight.SemiBold, Classes = { "secondary" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            if (result.Page is { } page)
            {
                var pagePill = new Border { Classes = { "pill" }, Child = new TextBlock { Text = L10n.F(Copy.Page, page) } };
                Grid.SetColumn(pagePill, 1);
                top.Children.Add(pagePill);
            }
            if (result.Link is { } link)
            {
                var open = Ui.Button(Copy.Open, () => Desktop.Start("xdg-open", [link.AbsoluteUri], null));
                Grid.SetColumn(open, 3);
                top.Children.Add(open);
            }
            var copy = new Button { Content = L10n.T(Copy.CopyOne), Classes = { "secondary", "compact" } };
            copy.Click += async (_, _) => await CopyAsync(result.Text, copy, Copy.Copied);
            Grid.SetColumn(copy, 4);
            top.Children.Add(copy);
            var body = new StackPanel { Spacing = 14 };
            body.Children.Add(top);
            body.Children.Add(new SelectableTextBlock { Text = result.Text, FontSize = 13, TextWrapping = TextWrapping.Wrap });
            cards.Children.Add(new Border { Classes = { "card" }, CornerRadius = new CornerRadius(18), Padding = new Thickness(18), Child = body });
        }
        foreach (var error in errors) cards.Children.Add(Ui.Warning(new TextBlock { Text = error }));
    }

    private async Task CopyAsync(string text, Button button, string done)
    {
        if (Clipboard is null) return;
        await Clipboard.SetTextAsync(text);
        button.Content = L10n.T(done);
    }
}
