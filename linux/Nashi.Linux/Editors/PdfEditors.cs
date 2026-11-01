using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Nashi.Core;
using Nashi.Linux.Editors.Media;

namespace Nashi.Linux.Editors;

/// Organize PDF (macOS pdfControls): every page as a thumbnail; click to select (Ctrl adds, Shift extends), drag a
/// page onto another to move it there, and Rotate, Duplicate, Delete, Move Left and Move Right for the selection, with
/// Make all pages the same width. A locked PDF asks for its password first.
public sealed class PdfOrganizeEditor(IReadOnlyList<string> inputs) : EditorPanel("organizePDF", inputs)
{
    private static class Copy
    {
        public const string Hint = "Fine-tune it. Save a fresh copy.";
        public const string Primary = "Save organized PDF";
        public const string Rotate = "Rotate";
        public const string Duplicate = "Duplicate";
        public const string Delete = "Delete";
        public const string Left = "Move Left";
        public const string Right = "Move Right";
        public const string Uniform = "Make all pages the same width";
        public const string Drag = "Click a page to select it (Ctrl adds, Shift extends). Drag a page onto another to move it there.";
        public const string Pages = "{0} pages";
        public const string Unlock = "Unlock";
        public const string Page = "Page {0}";
    }

    public sealed class Page(int number, Bitmap thumbnail)
    {
        public int Number { get; } = number;
        public Bitmap Thumbnail { get; } = thumbnail;
        public int Rotation { get; set; }
    }

    private readonly List<Page> pages = [];
    private readonly HashSet<Page> selection = [];
    private readonly WrapPanel grid = new() { ItemSpacing = 12, LineSpacing = 12 };
    private Page? dragging;
    private bool uniform, locked;
    private string password = "";
    private TextBlock? count;

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public override bool ImageSource => false;
    public override bool ComparesResult => false;
    public override bool Canvas => false;
    public override Control? Stage => stageControl ??= Build();
    private Control? stageControl;
    public override bool CanSave => !locked && pages.Count > 0;
    public IReadOnlyList<Page> Pages => pages;
    public bool Locked => locked;

    private Control Build()
    {
        var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        grid.PointerReleased += (_, e) =>
        {
            if (dragging is not { } from) return;
            dragging = null;
            var target = grid.Children.OfType<Border>().FirstOrDefault(t => t.Bounds.Contains(e.GetPosition(grid)));
            if (target?.Tag is Page to && to != from) Move(pages.IndexOf(from), pages.IndexOf(to));
        };
        return scroll;
    }

    public override async Task LoadAsync(CancellationToken token)
    {
        var refusal = await Task.Run(() => Locks.PdfRefusal(Inputs[0]), token);
        locked = refusal == Locks.Refused;
        if (refusal is null) await Thumbnails(token);
        else if (!locked) Say(refusal);
    }

    private async Task Thumbnails(CancellationToken token)
    {
        var images = await PdfRaster.RenderAsync(Inputs[0], 72, token, password);
        pages.Clear();
        selection.Clear();
        for (var n = 0; n < images.Length; n++) pages.Add(new Page(n + 1, new Bitmap(new MemoryStream(images[n]))));
        Fill();
    }

    /// Unlocks a password-protected PDF and shows its pages; false when the password is wrong.
    public async Task<bool> UnlockAsync(string value)
    {
        password = value;
        if (Locks.PdfRefusal(Inputs[0], value) is { } refusal) { Say(refusal); return false; }
        locked = false;
        Say(null);
        await Thumbnails(CancellationToken.None);
        RebuildOptions();
        Change();
        return true;
    }

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 14 };
        if (locked)
        {
            stack.Children.Add(Ui.Caption(Locks.Says(Inputs[0])));
            stack.Children.Add(Locks.Field(false, password, p => password = p));
            stack.Children.Add(Ui.Button(Copy.Unlock, async () => await UnlockAsync(password)));
            return stack;
        }
        stack.Children.Add(Ui.Caption(Copy.Drag));
        count = new TextBlock { FontSize = 12 };
        stack.Children.Add(count);
        stack.Children.Add(Ui.Wrap(Ui.Button(Copy.Rotate, Rotate), Ui.Button(Copy.Duplicate, Duplicate), Ui.Button(Copy.Delete, () => Delete()), Ui.Button(Copy.Left, () => MoveSelected(-1)), Ui.Button(Copy.Right, () => MoveSelected(1))));
        var box = new CheckBox { Content = L10n.T(Copy.Uniform), IsChecked = uniform, FontSize = 12 };
        box.IsCheckedChanged += (_, _) => { uniform = box.IsChecked == true; Change(); };
        stack.Children.Add(box);
        Fill();
        return stack;
    }

    private void Fill()
    {
        if (count is not null) count.Text = L10n.F(Copy.Pages, pages.Count);
        grid.Children.Clear();
        for (var n = 0; n < pages.Count; n++)
        {
            var page = pages[n];
            var image = new Image { Source = page.Thumbnail, Width = 96, Height = 124, Stretch = Stretch.Uniform };
            var turned = new LayoutTransformControl { Child = image, LayoutTransform = new RotateTransform(page.Rotation) };
            var label = new TextBlock { Text = (n + 1).ToString(CultureInfo.CurrentCulture), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "secondary" } };
            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(new Border { Height = 128, Width = 128, Child = turned });
            content.Children.Add(label);
            var tile = new Border { Child = content, Padding = new Thickness(8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), Tag = page, Cursor = new Cursor(StandardCursorType.Hand) };
            tile.BorderBrush = selection.Contains(page) ? Ui.Gold : Brushes.Transparent;
            tile.Background = new SolidColorBrush(Color.FromArgb(selection.Contains(page) ? (byte)0x30 : (byte)0x10, 0x80, 0x80, 0x80));
            Avalonia.Automation.AutomationProperties.SetName(tile, L10n.F(Copy.Page, page.Number));
            tile.PointerPressed += (_, e) =>
            {
                Select(pages.IndexOf(page), e.KeyModifiers);
                dragging = page;
            };
            grid.Children.Add(tile);
        }
    }

    /// Selects a page as a click does (Ctrl adds or removes, Shift extends from the last one).
    public void Select(int index, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (index < 0 || index >= pages.Count) return;
        var page = pages[index];
        if ((modifiers & KeyModifiers.Control) != 0) { if (!selection.Remove(page)) selection.Add(page); }
        else if ((modifiers & KeyModifiers.Shift) != 0 && selection.Count > 0)
        {
            var from = selection.Max(p => pages.IndexOf(p));
            for (var n = Math.Min(from, index); n <= Math.Max(from, index); n++) selection.Add(pages[n]);
        }
        else { selection.Clear(); selection.Add(page); }
        Fill();
    }

    public void Rotate()
    {
        foreach (var page in selection) page.Rotation = (page.Rotation + 90) % 360;
        Fill();
        Change();
    }

    public void Duplicate()
    {
        foreach (var page in pages.Where(selection.Contains).Reverse().ToList())
            pages.Insert(pages.IndexOf(page) + 1, new Page(page.Number, page.Thumbnail) { Rotation = page.Rotation });
        Fill();
        Change();
    }

    public override bool Delete()
    {
        if (selection.Count == 0 || pages.Count - selection.Count < 1) return false;
        pages.RemoveAll(selection.Contains);
        selection.Clear();
        Fill();
        Change();
        return true;
    }

    public void MoveSelected(int delta)
    {
        var chosen = pages.Where(selection.Contains).ToList();
        if (delta > 0) chosen.Reverse();
        foreach (var page in chosen)
        {
            var at = pages.IndexOf(page);
            var to = at + delta;
            if (to < 0 || to >= pages.Count || selection.Contains(pages[to])) continue;
            (pages[at], pages[to]) = (pages[to], pages[at]);
        }
        Fill();
        Change();
    }

    /// Moves the page at `from` to `to` (a drag).
    public void Move(int from, int to)
    {
        if (from < 0 || from >= pages.Count || to < 0 || to >= pages.Count || from == to) return;
        var page = pages[from];
        pages.RemoveAt(from);
        pages.Insert(to, page);
        Fill();
        Change();
    }

    public void Uniform(bool on) { uniform = on; Change(); }

    public override void Reset()
    {
        if (locked) return;
        var original = pages.GroupBy(p => p.Number).Select(g => g.First()).OrderBy(p => p.Number).ToList();
        foreach (var page in original) page.Rotation = 0;
        pages.Clear();
        pages.AddRange(original);
        selection.Clear();
        uniform = false;
    }

    public override EditorJob Job()
    {
        var order = pages.Select(p => p.Number).ToArray();
        var turns = pages.Select(p => p.Rotation).ToArray();
        var (wide, secret) = (uniform, password);
        return (files, destination, progress, token) => Task.Run(() => DocumentEngine.Pdf("organizePDF", files, destination, order, token, pageRotations: turns, password: secret, uniformWidth: wide), token);
    }
}

/// Split PDF (macOS splitPDF): every page, every N pages, or page ranges such as "1-3, 5, 7-9" (one file per range),
/// checked against each PDF's page count before Save. A locked PDF asks for its password.
public sealed class PdfSplitEditor(IReadOnlyList<string> inputs) : EditorPanel("splitPDF", inputs)
{
    private static class Copy
    {
        public const string Hint = "Fine-tune it. Save a fresh copy.";
        public const string Primary = "Split PDF";
        public const string Every = "Every page";
        public const string Group = "Every N pages";
        public const string Ranges = "Page ranges";
        public const string PagesPerFile = "Pages per file";
        public const string Example = "Page ranges, for example 1-3, 5";
        public const string RangesHint = "Each range becomes its own PDF.";
        public const string Count = "{0}: {1} pages";
        public const string Invalid = "Choose page ranges such as 1-3, 5 within the PDF page count.";
    }

    private string mode = "every";
    private int perFile = 2;
    private string ranges = "";
    private string password = "";
    private readonly Dictionary<string, int> counts = [];

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public override bool SideBySide => false;
    public override bool ImageSource => false;

    public override Task LoadAsync(CancellationToken token) => Task.Run(Count, token);

    private void Count()
    {
        foreach (var file in Inputs)
        {
            if (!Locks.PdfOpens(file, password)) continue;
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(file, new UglyToad.PdfPig.ParsingOptions { Password = password });
            counts[file] = pdf.NumberOfPages;
        }
    }

    public void Mode(string value, int pages = 2, string text = "") { (mode, perFile, ranges) = (value, pages, text); Change(); }
    public void Password(string value) { password = value; counts.Clear(); Count(); }

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 14 };
        foreach (var file in Inputs) stack.Children.Add(Ui.Caption(counts.TryGetValue(file, out var n) ? L10n.F(Copy.Count, Path.GetFileName(file), n) : Locks.Says(file)));
        if (counts.Count < Inputs.Count) stack.Children.Add(Locks.Field(false, password, Password));
        var modes = new Segments([("every", Copy.Every), ("group", Copy.Group), ("ranges", Copy.Ranges)], mode);
        var group = Ui.Labeled(Copy.PagesPerFile, Ui.Number(perFile, v => { perFile = v; Change(); }));
        var text = new TextBox { Text = ranges, Watermark = L10n.T(Copy.Example), FontSize = 12 };
        text.TextChanged += (_, _) => { ranges = text.Text ?? ""; Change(); };
        var rangeBox = new StackPanel { Spacing = 6 };
        rangeBox.Children.Add(text);
        rangeBox.Children.Add(Ui.Caption(Copy.RangesHint));
        void Show() { group.IsVisible = mode == "group"; rangeBox.IsVisible = mode == "ranges"; }
        modes.Chosen += v => { mode = v; Show(); Change(); };
        Show();
        stack.Children.Add(modes);
        stack.Children.Add(group);
        stack.Children.Add(rangeBox);
        return stack;
    }

    /// The ranges typed, one per output ("1-3", "5").
    public string[] RangeList => [.. ranges.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public override string? Validate()
    {
        if (Locks.PdfRefusal(Inputs, password) is { } refusal) return refusal;
        if (counts.Count < Inputs.Count) Count();
        if (mode == "group" && perFile is < 1 or > 1000) return L10n.T("Choose between 1 and 1,000 pages per file.");
        if (mode != "ranges") return null;
        if (RangeList.Length == 0 || !RangeList.All(r => Regex.IsMatch(r, @"^\d+\s*(-\s*\d+)?$", RegexOptions.None, TimeSpan.FromSeconds(1)))) return L10n.T(Copy.Invalid);
        foreach (var file in Inputs)
        {
            try { _ = DocumentEngine.PageRange(ranges, counts[file]); }
            catch (ArgumentException error) { return Path.GetFileName(file) + ": " + L10n.T(error.Message); }
        }
        return null;
    }

    public override EditorJob Job()
    {
        var (how, size, list, secret) = (mode, mode == "group" ? perFile : 1, RangeList, password);
        return (files, destination, progress, token) => Task.Run<IReadOnlyList<string>>(() =>
        {
            if (how != "ranges") return DocumentEngine.Pdf("splitPDF", files, destination, null, token, pagesPerFile: size, password: secret);
            using var tx = new ExportTransaction(destination);
            var outputs = new List<string>();
            foreach (var file in files)
                foreach (var range in list)
                {
                    token.ThrowIfCancellationRequested();
                    var part = DocumentEngine.Pdf("splitPDF", [file], tx.StagingDirectory, null, token, pagesPerFile: 1000, password: secret, pageRanges: range)[0];
                    var named = SafePaths.Unique(tx.StagingDirectory, Path.GetFileNameWithoutExtension(file) + " Pages " + Regex.Replace(range, @"\s", "", RegexOptions.None, TimeSpan.FromSeconds(1)) + ".pdf");
                    File.Move(part, named);
                    outputs.Add(named);
                }
            return tx.Publish(outputs, token);
        }, token);
    }
}

/// Join Videos and Merge PDFs (macOS orderControls): the files in playback or document order, each with its length
/// or page count; drag a row onto another, or use the arrows. Merge asks for a password when a PDF is locked.
public sealed class OrderEditor(string tool, IReadOnlyList<string> inputs, MediaTools? tools) : EditorPanel(tool, inputs)
{
    private static class Copy
    {
        public const string Hint = "Better together.";
        public const string Join = "Join Videos";
        public const string Merge = "Merge PDFs";
        public const string Order = "Playback / document order";
        public const string Drag = "Drag to reorder.";
        public const string Pages = "{0} pages";
        public const string Total = "Total: {0}";
        public const string Locked = "Locked";
        public const string Normalized = "Every clip is fitted to the first one's size at 30 fps; clips without sound get silence.";
    }

    const double RowHeight = 38;
    private readonly Dictionary<string, string> details = [];
    private readonly Dictionary<string, double> durations = [];
    private StackPanel? list;
    private TextBlock? total;
    private int dragging = -1;
    private string password = "";

    public override string Hint => Copy.Hint;
    public override string Primary => Tool == "joinVideos" ? Copy.Join : Copy.Merge;
    public override bool SideBySide => false;
    public override bool ImageSource => false;

    public override async Task LoadAsync(CancellationToken token)
    {
        foreach (var file in Inputs)
        {
            if (Tool == "joinVideos")
            {
                var info = await tools!.Engine.InspectAsync(file, token);
                durations[file] = info.Duration;
                details[file] = (info.Video is { Width: { } w, Height: { } h } ? $"{w} × {h} · " : "") + Timeline.Format(info.Duration);
            }
            else details[file] = await Task.Run(() => Pages(file, ""), token);
        }
    }

    private string Pages(string file, string secret)
    {
        if (Locks.PdfRefusal(file, secret) is { } refusal) return refusal == Locks.Refused ? L10n.T(Copy.Locked) : refusal;
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(file, new UglyToad.PdfPig.ParsingOptions { Password = secret });
        return L10n.F(Copy.Pages, pdf.NumberOfPages);
    }

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Ui.Heading(Copy.Order));
        stack.Children.Add(Ui.Caption(Copy.Drag));
        list = new StackPanel { Background = Brushes.Transparent };
        list.PointerReleased += (_, e) =>
        {
            if (dragging < 0) return;
            var from = dragging;
            dragging = -1;
            Move(from, Math.Clamp((int)(e.GetPosition(list).Y / RowHeight), 0, Inputs.Count - 1));
        };
        stack.Children.Add(list);
        total = new TextBlock { FontSize = 12, Classes = { "secondary" } };
        stack.Children.Add(total);
        if (Tool == "joinVideos") stack.Children.Add(Ui.Caption(Copy.Normalized));
        else if (Inputs.Any(f => details.GetValueOrDefault(f) == L10n.T(Copy.Locked)))
            stack.Children.Add(Locks.Field(false, password, p => { password = p; foreach (var f in Inputs) details[f] = Pages(f, p); Fill(); }));
        Fill();
        return stack;
    }

    private void Fill()
    {
        if (list is null) return;
        list.Children.Clear();
        for (var n = 0; n < Inputs.Count; n++)
        {
            var at = n;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,Auto,Auto,Auto"), ColumnSpacing = 6 };
            grid.Children.Add(new TextBlock { Text = (n + 1).ToString(CultureInfo.CurrentCulture), FontSize = 12, Classes = { "secondary" }, VerticalAlignment = VerticalAlignment.Center });
            var name = new TextBlock { Text = Path.GetFileName(Inputs[n]), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var detail = new TextBlock { Text = details.GetValueOrDefault(Inputs[n], ""), FontSize = 11, Classes = { "secondary" }, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(detail, 2);
            grid.Children.Add(detail);
            var up = new Button { Content = "↑", Classes = { "quiet", "compact" }, IsEnabled = n > 0, Padding = new Thickness(7, 2) };
            up.Click += (_, _) => Move(at, at - 1);
            Grid.SetColumn(up, 3);
            grid.Children.Add(up);
            var down = new Button { Content = "↓", Classes = { "quiet", "compact" }, IsEnabled = n < Inputs.Count - 1, Padding = new Thickness(7, 2) };
            down.Click += (_, _) => Move(at, at + 1);
            Grid.SetColumn(down, 4);
            grid.Children.Add(down);
            var row = new Border { Height = RowHeight, Padding = new Thickness(6, 0), CornerRadius = new CornerRadius(8), Child = grid, Cursor = new Cursor(StandardCursorType.SizeNorthSouth) };
            row.PointerPressed += (_, e) => { if (!(e.Source is Visual v && (v is Button || v.FindAncestorOfType<Button>() is not null))) dragging = at; };
            list.Children.Add(row);
        }
        if (total is not null) total.Text = Tool == "joinVideos" ? L10n.F(Copy.Total, Timeline.Format(durations.Values.Sum())) : "";
    }

    /// Moves the file at `from` to `to` in the order.
    public void Move(int from, int to)
    {
        if (from < 0 || from >= Inputs.Count || to < 0 || to >= Inputs.Count || from == to) return;
        var order = Inputs.ToList();
        var file = order[from];
        order.RemoveAt(from);
        order.Insert(to, file);
        Inputs = order;
        Fill();
        Change();
    }

    public void Password(string value) => password = value;

    public override string? Validate() => Tool == "mergePDF" ? Locks.PdfRefusal(Inputs, password) : null;

    public override EditorJob Job()
    {
        var secret = password;
        if (Tool == "mergePDF") return (files, destination, progress, token) => Task.Run(() => DocumentEngine.Pdf("mergePDFs", files, destination, token: token, password: secret), token);
        var engine = tools!.Engine;
        return (files, destination, progress, token) => engine.ToolAsync("joinVideos", files, destination, new MediaOptions(), progress, token);
    }
}
