using Avalonia.Controls;
using Avalonia.Input;
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
        public const string SetUpTools = "Set up tools";
        public const string ToolsReady = "Tools ready";
        public const string Problem = "Something went wrong: {0}";
    }

    internal static readonly Bitmap Mark = Load("NashiMark.png");
    internal static readonly Bitmap HappyMark = Load("NashiMarkHappy.png");

    private int lastSkipped;
    private bool dropTargeted;
    private string? notice;
    private readonly Toolchain tools;
    private readonly WheelFlow flow;

    /// Home's wheels: tools, formats and actions over this window.
    public WheelFlow Flow => flow;

    public Selection Selection { get; } = new();

    /// Closing hides the window instead (the background instance keeps running).
    public bool HideOnClose { get; init; }

    /// Choose files: the system picker; the checks swap in their own.
    public Func<Task<IReadOnlyList<string>>> PickFiles { get; set; }

    public MainWindow() : this(AppPaths.CreateToolchain()) { }

    public MainWindow(Toolchain tools)
    {
        InitializeComponent();
        Desktop.Translucent(this);
        this.tools = tools;
        flow = new WheelFlow(tools, this, Notify, ShowToolSetupAsync);
        PickFiles = async () => LocalPaths(await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = L10n.T(Copy.PickerTitle), AllowMultiple = true })).ToList();
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
            var picked = await PickFiles();
            if (picked.Count > 0) AddFiles(picked);
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
        if (e.Key != Key.Space && (e.Key != Key.V || Clipboard is null)) return;
        e.Handled = true;
        try
        {
            if (e.Key == Key.Space) { await ShowWheelAsync(flow.ShowTools); return; }
            AddFiles(await Desktop.ClipboardFilesAsync(Clipboard!));
        }
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

    private void RefreshToolsButton() => SetUpToolsButton.Content = AppPaths.AllReady(tools) ? L10n.T(Copy.ToolsReady) : L10n.T(Copy.SetUpTools);
    private async void OnConvert(object? sender, RoutedEventArgs e) => await ShowWheelAsync(flow.ShowFormats);
    private async void OnTools(object? sender, RoutedEventArgs e) => await ShowWheelAsync(flow.ShowTools);
    private async void OnActions(object? sender, RoutedEventArgs e) => await ShowWheelAsync(flow.ShowActions);

    /// The format wheel (Convert chip), the tools wheel (Tools chip, Ctrl+Space) or the actions wheel (Actions chip).
    /// With nothing selected yet, the file picker opens first, then the wheel.
    private async Task ShowWheelAsync(Action<IReadOnlyList<string>> show)
    {
        try
        {
            if (flow.Busy) return;
            if (Selection.Files.Count == 0)
            {
                var picked = await PickFiles();
                if (picked.Count == 0) return;
                AddFiles(picked);
                if (Selection.Files.Count == 0) return;
            }
            show(Selection.Files);
        }
        catch (Exception error) { Notify(L10n.F(Copy.Problem, L10n.T(error.Message))); }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (!HideOnClose || e.CloseReason != WindowCloseReason.WindowClosing) return;
        e.Cancel = true;
        Hide();
    }
}
