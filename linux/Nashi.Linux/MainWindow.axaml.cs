using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Nashi.Core;

namespace Nashi.Linux;

public sealed record FileChip(string Path, string Name)
{
    public string RemoveTip => L10n.F("Remove {0} from selection", Name);
    public string RemoveName => L10n.F("Remove {0}", Name);
}

public sealed partial class MainWindow : Window
{
    // Every user-visible string this file produces; the static ones live in MainWindow.axaml.
    private static class Copy
    {
        public const string StartHere = "START HERE";
        public const string Ready = "READY WHEN YOU ARE";
        public const string Landing = "A perfect landing spot";
        public const string Empty = "Drop something good.";
        public const string EmptyDetail = "Images, videos, documents, and all the little things in between.";
        public const string Dropping = "Drop it like it’s… a file.";
        public const string Many = "{0} files, ready to go.";
        public const string ManyDetail = "Keep them together, or give each one a fresh start.";
        public const string Choose = "Choose files";
        public const string AddMore = "Add files";
        public const string PickerTitle = "Choose files";
        public const string OneReady = "1 file ready.";
        public const string ManyReady = "{0} files ready.";
        public const string Skipped = "Skipped {0}: only existing files can come in, not folders.";
        public const string ConvertWheel = "Convert";
        public const string ToolsWheel = "Tools";
        public const string ConvertTo = "Convert to {0}";
        public const string ChooseFirst = "Choose files first.";
        public const string NoFormats = "No conversions for this selection yet.";
        public const string NoTools = "No tools for this selection yet.";
        public const string SaveTo = "Save new files to…";
        public const string SavedOne = "Saved 1 new file. Originals kept.";
        public const string SavedMany = "Saved {0} new files. Originals kept.";
        public const string Cancelled = "Cancelled. Originals kept.";
        public const string SetUpTools = "Set up tools";
        public const string ToolsReady = "Tools ready";
        public const string Failed = "Couldn’t finish: {0}";
        public const string Problem = "Something went wrong: {0}";
    }

    private static readonly Bitmap Mark = Load("NashiMark.png");
    private static readonly Bitmap HappyMark = Load("NashiMarkHappy.png");

    private int lastSkipped;
    private bool dropTargeted;
    private bool busy;
    private string? notice;
    private readonly Toolchain tools;
    private readonly OperationRunner runner;

    public Selection Selection { get; } = new();

    public MainWindow() : this(AppPaths.CreateToolchain()) { }

    public MainWindow(Toolchain tools)
    {
        InitializeComponent();
        this.tools = tools;
        runner = new OperationRunner(tools);
        RefreshToolsButton();
        Selection.Changed += () => { notice = null; Refresh(); };
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropTargeted(false));
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static Bitmap Load(string name) => new(AssetLoader.Open(new Uri($"avares://Nashi/Assets/{name}")));

    private void AddFiles(IEnumerable<string> paths)
    {
        lastSkipped = Selection.Add(paths).Skipped;
        Refresh();
    }

    private void Refresh()
    {
        var files = Selection.Files;
        var any = files.Count > 0;
        StartPillText.Text = dropTargeted ? L10n.T(Copy.Landing) : any ? L10n.T(Copy.Ready) : L10n.T(Copy.StartHere);
        SelectionTitle.Text = dropTargeted ? L10n.T(Copy.Dropping)
            : files.Count == 0 ? L10n.T(Copy.Empty)
            : files.Count == 1 ? Path.GetFileName(files[0])
            : L10n.F(Copy.Many, files.Count);
        SelectionDetail.Text = files.Count == 0 ? L10n.T(Copy.EmptyDetail)
            : files.Count == 1 ? HomeRelative(Path.GetDirectoryName(files[0]) ?? "/")
            : L10n.T(Copy.ManyDetail);
        ChooseLabel.Text = any ? L10n.T(Copy.AddMore) : L10n.T(Copy.Choose);
        ChooseButton.Classes.Set("primary", !any);
        ChooseButton.Classes.Set("secondary", any);
        GardenMark.Source = any || dropTargeted ? HappyMark : Mark;
        StartCard.Classes.Set("targeted", dropTargeted);

        SelectionPanel.IsVisible = any;
        FileChips.ItemsSource = files.Select(f => new FileChip(f, Path.GetFileName(f))).ToArray();

        var status = new List<string>();
        if (any) status.Add(files.Count == 1 ? L10n.T(Copy.OneReady) : L10n.F(Copy.ManyReady, files.Count));
        if (lastSkipped > 0) status.Add(L10n.F(Copy.Skipped, lastSkipped));
        if (notice is not null) status.Add(notice);
        StatusLine.Text = string.Join(" ", status);
        StatusLine.IsVisible = status.Count > 0;
    }

    private static string HomeRelative(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path == home || path.StartsWith(home + "/", StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }

    private static IEnumerable<string> LocalPaths(IEnumerable<IStorageItem>? items) =>
        items?.Select(i => i.TryGetLocalPath()).OfType<string>() ?? [];

    private async void OnChooseFiles(object? sender, RoutedEventArgs e)
    {
        try
        {
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = L10n.T(Copy.PickerTitle), AllowMultiple = true });
            if (picked.Count > 0) AddFiles(LocalPaths(picked));
        }
        catch (Exception error) { Notify(L10n.F(Copy.Problem, L10n.T(error.Message))); }
    }

    private void OnRemoveFile(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path }) Selection.Remove(path);
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        lastSkipped = 0;
        Selection.Clear();
        Refresh();
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.Control) return;
        if (e.Key == Key.Space) { e.Handled = true; ShowToolsWheel(); return; }
        if (e.Key != Key.V || Clipboard is null) return;
        e.Handled = true;
        try { AddFiles(LocalPaths(await Clipboard.TryGetFilesAsync())); }
        catch (Exception error) { Notify(L10n.F(Copy.Problem, L10n.T(error.Message))); }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropTargeted(hasFiles);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        dropTargeted = false;
        AddFiles(LocalPaths(e.DataTransfer.TryGetFiles()));
    }

    private void SetDropTargeted(bool targeted)
    {
        if (dropTargeted == targeted) return;
        dropTargeted = targeted;
        Refresh();
    }

    private void Notify(string message)
    {
        notice = message;
        Refresh();
    }

    private void OnSetUpTools(object? sender, RoutedEventArgs e) => _ = ShowToolSetupAsync();

    /// Opens the setup window over this one; the header label follows whatever it left installed.
    private async Task ShowToolSetupAsync()
    {
        try { await new ToolSetupWindow(tools).ShowDialog(this); }
        catch (Exception error) { Notify(L10n.F(Copy.Problem, L10n.T(error.Message))); }
        RefreshToolsButton();
    }

    private void RefreshToolsButton() => SetUpToolsButton.Content = AppPaths.MediaReady(tools) ? L10n.T(Copy.ToolsReady) : L10n.T(Copy.SetUpTools);
    private void OnConvert(object? sender, RoutedEventArgs e) => ShowFormatWheel();
    private void OnTools(object? sender, RoutedEventArgs e) => ShowToolsWheel();
    private void OnActions(object? sender, RoutedEventArgs e) { }

    /// Tools, with Convert first when the selection has formats; Ctrl+Space opens it too.
    private void ShowToolsWheel()
    {
        var files = Selection.Files;
        var items = Capabilities.Tools(files).Select(id => new WheelItem(id, L10n.T(FeatureCatalog.ToolNames.GetValueOrDefault(id, id)))).ToList();
        if (Capabilities.Formats(files).Length > 0) items.Insert(0, new WheelItem("convert", L10n.T(Copy.ConvertWheel)));
        ShowWheel(L10n.T(Copy.ToolsWheel), items, L10n.T(Copy.NoTools));
    }

    private void ShowFormatWheel() =>
        ShowWheel(L10n.T(Copy.ConvertWheel), Capabilities.Formats(Selection.Files).Select(f => new WheelItem("convert:" + f, f == "gz" ? "GZIP" : f.ToUpperInvariant())).ToList(), L10n.T(Copy.NoFormats));

    private void ShowWheel(string title, IReadOnlyList<WheelItem> items, string empty)
    {
        if (busy) return;
        if (Selection.Files.Count == 0) { Notify(L10n.T(Copy.ChooseFirst)); return; }
        if (items.Count == 0) { Notify(empty); return; }
        var wheel = new WheelWindow(title, items);
        wheel.Wheel.Chosen += OnWheelChosen;
        wheel.Show(this);
    }

    private async void OnWheelChosen(WheelItem item)
    {
        try
        {
            if (item.Id == "convert") ShowFormatWheel();
            else await RunAsync(item);
        }
        catch (Exception error) { Notify(L10n.F(Copy.Problem, L10n.T(error.Message))); }
    }

    private async Task RunAsync(WheelItem item)
    {
        if (busy) return;
        busy = true;
        try
        {
            var files = Selection.Files.ToArray();
            if (files.Length == 0) return;
            var destination = await DestinationAsync(files[0]);
            if (destination is null) return;
            var title = item.Id.StartsWith("convert:", StringComparison.Ordinal) ? L10n.F(Copy.ConvertTo, item.Title) : item.Title;
            using var cancellation = new CancellationTokenSource();
            var toast = new ProgressToast(title, cancellation.Cancel);
            toast.Show(this);
            try
            {
                var outputs = await runner.RunAsync(item.Id, files, destination, new Progress<OperationProgress>(toast.Update), cancellation.Token);
                toast.Complete(outputs, destination);
                Notify(outputs.Count == 1 ? L10n.T(Copy.SavedOne) : L10n.F(Copy.SavedMany, outputs.Count));
            }
            catch (Exception error)
            {
                toast.Close();
                if (error is MissingToolsException) await ShowToolSetupAsync();
                else Notify(error is OperationCanceledException ? L10n.T(Copy.Cancelled) : L10n.F(Copy.Failed, L10n.T(error.Message)));
            }
        }
        finally { busy = false; }
    }

    /// The first file's folder when Nashi may write there; otherwise the user picks one (null when cancelled).
    private async Task<string?> DestinationAsync(string first)
    {
        var folder = Path.GetDirectoryName(first);
        if (folder is not null && Writable(folder)) return folder;
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L10n.T(Copy.SaveTo) });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    private static bool Writable(string folder)
    {
        try
        {
            using var probe = new FileStream(Path.Combine(folder, ".nashi-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
