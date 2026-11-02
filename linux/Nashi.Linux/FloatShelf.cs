using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using ImageMagick;
using Orientation = Avalonia.Layout.Orientation;
using FontWeight = Avalonia.Media.FontWeight;
using Nashi.Core;

namespace Nashi.Linux;

/// What Float holds: references to files and folders, in the order they came, each once. Removing a reference never
/// touches the file. Kept in Settings, so the shelf comes back after a restart.
public sealed class Shelf
{
    private readonly List<string> paths;

    public Shelf() => paths = [.. Settings.Current.Shelf.Where(p => File.Exists(p) || Directory.Exists(p))];

    public IReadOnlyList<string> Paths => paths;
    public event Action? Changed;

    /// Adds what exists and isn't here yet; how many were added.
    public int Add(IEnumerable<string> incoming)
    {
        var added = 0;
        foreach (var path in incoming.Where(p => File.Exists(p) || Directory.Exists(p)).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Take(500))
            if (!paths.Contains(path)) { paths.Add(path); added++; }
        Save();
        return added;
    }

    public void Remove(IEnumerable<string> gone)
    {
        var set = gone.ToHashSet();
        paths.RemoveAll(set.Contains);
        Save();
    }

    public void Clear()
    {
        paths.Clear();
        Save();
    }

    private void Save()
    {
        Settings.Update(s => s with { Shelf = [.. paths] });
        Changed?.Invoke();
    }
}

/// Float (macOS FloatShelf.swift, docs/FLOAT.md) on Linux: a small, always-on-top, translucent shelf pinned to a
/// screen edge. Drop files on it (an XDND target while a drag runs, from the background instance) and the wheel opens
/// for them; pick some (Ctrl/Shift for several) to open the wheel, copy, reveal or remove them. It folds into a pill
/// on its edge. Nothing on it is ever moved or deleted.
public sealed class FloatShelf : Window
{
    private static class Copy
    {
        public const string Title = "Float";
        public const string Items = "{0} items";
        public const string OneItem = "1 item";
        public const string Empty = "Drop files here";
        public const string Wheel = "Open wheel";
        public const string Remove = "Remove";
        public const string Clear = "Clear";
        public const string CopyFiles = "Copy";
        public const string Reveal = "Show in folder";
        public const string Collapse = "Collapse Float";
        public const string Expand = "Expand Float";
        public const string Pin = "Pin to another edge";
        public const string Close = "Close Float without deleting files";
        public const string Selected = "{0} selected";
        public const string Hint = "Removing a reference never deletes the file.";
        public const string Unread = "Nashi couldn’t read what was dropped. Drop files from your file manager or browser.";
        public const string Copied = "Copied. Paste them in your file manager.";
    }

    public const double PillWidth = 150, PillHeight = 44, ShelfWidth = 232;

    private static FloatShelf? current;
    private readonly ListBox list = new() { SelectionMode = SelectionMode.Multiple, Background = Brushes.Transparent, Padding = new Thickness(6, 2) };
    private readonly TextBlock count = new() { FontSize = 12, FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = new() { FontSize = 10, TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } };
    private readonly Border card = new() { Classes = { "card" }, CornerRadius = new CornerRadius(22), Padding = new Thickness(10) };
    private readonly Grid shelf = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
    private readonly Button pill = new() { Classes = { "quiet" }, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly Dictionary<string, Bitmap?> thumbnails = [];
    private IDisposable? dropTarget;

    public Shelf Items { get; } = new();

    /// Opens the wheel for files (the drop and "Open wheel"); the background flow's tools wheel.
    public Action<IReadOnlyList<string>> OpenWheel { get; set; }

    /// Where Copy puts the files: the clipboard. The checks swap in their own.
    public Func<IReadOnlyList<string>, Task> CopyFiles { get; set; }

    public bool Collapsed => Settings.Current.ShelfCollapsed;

    /// The shelf on screen, if there is one.
    public static FloatShelf? Current => current is { IsVisible: true } shelf ? shelf : null;

    /// Adds files to Float (the Actions wheel's Float petal) and shows it expanded.
    public static FloatShelf Open(IEnumerable<string> files, Action<IReadOnlyList<string>> openWheel)
    {
        var shelf = Current ?? (current = new FloatShelf(openWheel));
        shelf.Items.Add(files);
        shelf.SetCollapsed(false);
        if (!shelf.IsVisible) shelf.Show();
        return shelf;
    }

    public FloatShelf(Action<IReadOnlyList<string>> openWheel)
    {
        OpenWheel = openWheel;
        CopyFiles = async files =>
        {
            if (Clipboard is { } clipboard) await FileActions.CopyFilesAsync(clipboard, files);
        };
        Title = L10n.T(Copy.Title);
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyBackgroundFallback = Brushes.Transparent;
        Desktop.Translucent(this);
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("NashiCanvasClear"));
        // The selection is gold, as everywhere in Nashi.
        foreach (var state in new[] { ":selected", ":selected:pointerover" })
            Styles.Add(new Avalonia.Styling.Style(x => x.OfType<ListBoxItem>().Class(state).Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
            {
                Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("NashiSelection")) },
            });

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), Margin = new Thickness(4, 0, 0, 6) };
        header.Children.Add(new Image { Source = MainWindow.Mark, Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetColumn(count, 1);
        header.Children.Add(count);
        header.Children.Add(Glyph(2, "⇄", Copy.Pin, () => Pin(Next(Settings.Current.ShelfEdge))));
        header.Children.Add(Glyph(3, "⌄", Copy.Collapse, () => SetCollapsed(true)));
        header.Children.Add(Glyph(4, "×", Copy.Close, Close));
        shelf.Children.Add(header);

        list.SelectionChanged += (_, _) => Refresh();
        list.DoubleTapped += (_, _) => Wheel();
        Grid.SetRow(list, 1);
        shelf.Children.Add(list);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 8, 0, 4) };
        actions.Children.Add(Action(Copy.Wheel, Wheel, primary: true));
        actions.Children.Add(Action(Copy.CopyFiles, () => _ = CopyAsync()));
        actions.Children.Add(Action(Copy.Reveal, Reveal));
        actions.Children.Add(Action(Copy.Remove, RemoveSelected));
        actions.Children.Add(Action(Copy.Clear, Items.Clear));
        Grid.SetRow(actions, 2);
        shelf.Children.Add(actions);
        Grid.SetRow(status, 3);
        shelf.Children.Add(status);

        pill.Click += (_, _) => SetCollapsed(false);
        AutomationProperties(pill, Copy.Expand);
        Content = card;
        Items.Changed += Fill;
        KeyDown += OnKey;
        Opened += (_, _) => Place();
        Closed += (_, _) => { dropTarget?.Dispose(); if (current == this) current = null; };
        Fill();
        SetCollapsed(Settings.Current.ShelfCollapsed);
    }

    private Button Glyph(int column, string glyph, string help, Action click)
    {
        var button = new Button { Classes = { "quiet", "compact" }, Content = glyph, FontSize = 14, Padding = new Thickness(7, 2), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(button, L10n.T(help));
        AutomationProperties(button, help);
        button.Click += (_, _) => click();
        Grid.SetColumn(button, column);
        return button;
    }

    private static Button Action(string text, Action click, bool primary = false)
    {
        var button = new Button { Classes = { primary ? "primary" : "secondary", "compact" }, Content = L10n.T(text), FontSize = 11 };
        button.Click += (_, _) => click();
        return button;
    }

    private static void AutomationProperties(Control control, string name) => Avalonia.Automation.AutomationProperties.SetName(control, L10n.T(name));

    /// The selected references, or all of them when none is selected.
    public IReadOnlyList<string> Selection => list.SelectedItems?.Cast<ListBoxItem>().Select(i => (string)i.Tag!).ToList() is { Count: > 0 } picked ? picked : Items.Paths;

    public void Select(params int[] indexes)
    {
        list.SelectedItems?.Clear();
        foreach (var index in indexes) if (index < list.ItemCount) list.SelectedItems?.Add(list.Items[index]);
    }

    private void Fill()
    {
        var selected = list.SelectedItems?.Cast<ListBoxItem>().Select(i => (string)i.Tag!).ToHashSet() ?? [];
        list.Items.Clear();
        foreach (var path in Items.Paths)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var art = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(7), ClipToBounds = true };
            art.Bind(Border.BackgroundProperty, this.GetResourceObservable("NashiSheet"));
            art.Child = new TextBlock { Text = Directory.Exists(path) ? "▤" : FeatureCatalog.Extension(path).ToUpperInvariant(), FontSize = 8, FontWeight = Avalonia.Media.FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (FeatureCatalog.IsImage(path)) _ = Thumbnail(art, path);
            row.Children.Add(art);
            row.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontSize = 11, MaxWidth = ShelfWidth - 90, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var item = new ListBoxItem { Content = row, Tag = path, Padding = new Thickness(4) };
            ToolTip.SetTip(item, path);
            list.Items.Add(item);
            if (selected.Contains(path)) list.SelectedItems?.Add(item);
        }
        Refresh();
        if (IsVisible) Place();
    }

    private async Task Thumbnail(Border art, string path)
    {
        if (!thumbnails.TryGetValue(path, out var bitmap))
        {
            try
            {
                var png = await Task.Run(() =>
                {
                    using var image = PhotoCodec.Read(path);
                    image.AutoOrient();
                    image.Thumbnail(new MagickGeometry(68, 68));
                    return image.ToByteArray(MagickFormat.Png);
                });
                bitmap = new Bitmap(new MemoryStream(png));
            }
            catch (Exception error) when (error is MagickException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException) { bitmap = null; }
            thumbnails[path] = bitmap;
        }
        if (bitmap is not null) art.Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
    }

    private void Refresh()
    {
        var n = Items.Paths.Count;
        var label = n == 0 ? L10n.T(Copy.Empty) : n == 1 ? L10n.T(Copy.OneItem) : L10n.F(Copy.Items, n);
        count.Text = label;
        pill.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { new Image { Source = MainWindow.Mark, Width = 22, Height = 22 }, new TextBlock { Text = label, FontSize = 12, FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } },
        };
        var picked = list.SelectedItems?.Count ?? 0;
        status.Text = picked > 0 ? L10n.F(Copy.Selected, picked) : L10n.T(Copy.Hint);
    }

    /// Folds into the pill on its edge, or opens into the shelf.
    public void SetCollapsed(bool collapsed)
    {
        if (Settings.Current.ShelfCollapsed != collapsed) Settings.Update(s => s with { ShelfCollapsed = collapsed });
        card.Child = collapsed ? pill : shelf;
        card.Padding = collapsed ? new Thickness(4) : new Thickness(10);
        Width = collapsed ? PillWidth : ShelfWidth;
        Height = collapsed ? PillHeight : Math.Clamp(150 + Math.Max(1, Items.Paths.Count) * 46, 260, 560);
        if (IsVisible) Place();
    }

    /// Pins the shelf to a screen edge ("left", "right", "top", "bottom").
    public void Pin(string edge)
    {
        Settings.Update(s => s with { ShelfEdge = edge });
        Place();
    }

    private static string Next(string edge) => edge switch { "right" => "bottom", "bottom" => "left", "left" => "top", _ => "right" };

    /// Its place on the edge it is pinned to, centered along it, inside the work area.
    public void Place()
    {
        if (Desktop.ScreenAt(this, Desktop.Pointer()) is not { } screen) return;
        Position = EdgePosition(screen.WorkingArea, PixelSize.FromSize(new Size(Width, Height), screen.Scaling), Settings.Current.ShelfEdge, (int)(8 * screen.Scaling));
    }

    public static PixelPoint EdgePosition(PixelRect area, PixelSize size, string edge, int margin) => edge switch
    {
        "left" => new(area.X + margin, area.Y + (area.Height - size.Height) / 2),
        "top" => new(area.X + (area.Width - size.Width) / 2, area.Y + margin),
        "bottom" => new(area.X + (area.Width - size.Width) / 2, area.Bottom - size.Height - margin),
        _ => new(area.Right - size.Width - margin, area.Y + (area.Height - size.Height) / 2),
    };

    /// A drag-and-drop started or ended somewhere (the background instance's X11 poll): while one runs, the shelf
    /// is an XDND drop target.
    public void DragRunning(bool running)
    {
        dropTarget?.Dispose();
        dropTarget = null;
        if (!running || !IsVisible) return;
        var scaling = Desktop.ScreenAt(this, Position)?.Scaling ?? 1;
        var rect = new PixelRect(Position, PixelSize.FromSize(new Size(Width, Height), scaling));
        var xid = TryGetPlatformHandle() is { HandleDescriptor: "XID", Handle: var handle } ? handle : IntPtr.Zero;
        dropTarget = XdndTarget.Create(xid, rect, (_, _) => true,
            (_, _, _) => Dispatcher.UIThread.Post(() => card.Classes.Set("targeted", true)),
            () => Dispatcher.UIThread.Post(() => card.Classes.Set("targeted", false)),
            (content, _, _) => Dispatcher.UIThread.Post(() => _ = DroppedAsync(content)),
            () => Dispatcher.UIThread.Post(() => SuccessToast.Notice(L10n.T(Copy.Unread)).Present()));
    }

    /// Files dropped on Float: added, then the wheel opens for them. Browser items are downloaded first.
    public async Task DroppedAsync(DropContent content)
    {
        card.Classes.Set("targeted", false);
        var files = content.Files;
        if (content.Web.Count > 0)
        {
            try { files = await BrowserDrop.SaveAsync(content.Web, BrowserDrop.DownloadFolder(), CancellationToken.None); }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TimeoutException)
            {
                SuccessToast.Notice(L10n.T(error.Message)).Present();
                return;
            }
        }
        Items.Add(files);
        if (files.Count > 0) OpenWheel(files);
    }

    private void Wheel()
    {
        if (Selection.Count > 0) OpenWheel(Selection);
    }

    private void RemoveSelected() => Items.Remove(list.SelectedItems?.Cast<ListBoxItem>().Select(i => (string)i.Tag!).ToList() ?? []);

    private async Task CopyAsync()
    {
        if (Selection.Count == 0) return;
        await CopyFiles(Selection);
        SuccessToast.Action(L10n.T(Copy.Copied), Selection.Count == 1 ? Path.GetFileName(Selection[0]) : L10n.F(Copy.Items, Selection.Count), null).Present();
    }

    private void Reveal()
    {
        if (Selection.Count > 0) FileActions.Share(Selection);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Delete: RemoveSelected(); break;
            case Key.A when ctrl: list.SelectAll(); break;
            case Key.C when ctrl: _ = CopyAsync(); break;
            case Key.Enter: Wheel(); break;
            case Key.Escape when (list.SelectedItems?.Count ?? 0) > 0: list.SelectedItems?.Clear(); break;
            case Key.Escape: SetCollapsed(true); break;
            default: return;
        }
        e.Handled = true;
    }
}
