using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace Nashi.Linux;

public sealed record FileChip(string Path, string Name);

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
    }

    private static readonly Bitmap Mark = Load("NashiMark.png");
    private static readonly Bitmap HappyMark = Load("NashiMarkHappy.png");

    private int lastSkipped;
    private bool dropTargeted;

    public Selection Selection { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        Selection.Changed += Refresh;
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
        StartPillText.Text = dropTargeted ? Copy.Landing : any ? Copy.Ready : Copy.StartHere;
        SelectionTitle.Text = dropTargeted ? Copy.Dropping
            : files.Count == 0 ? Copy.Empty
            : files.Count == 1 ? Path.GetFileName(files[0])
            : string.Format(Copy.Many, files.Count);
        SelectionDetail.Text = files.Count == 0 ? Copy.EmptyDetail
            : files.Count == 1 ? HomeRelative(Path.GetDirectoryName(files[0]) ?? "/")
            : Copy.ManyDetail;
        ChooseLabel.Text = any ? Copy.AddMore : Copy.Choose;
        ChooseButton.Classes.Set("primary", !any);
        ChooseButton.Classes.Set("secondary", any);
        GardenMark.Source = any || dropTargeted ? HappyMark : Mark;
        StartCard.Classes.Set("targeted", dropTargeted);

        SelectionPanel.IsVisible = any;
        FileChips.ItemsSource = files.Select(f => new FileChip(f, Path.GetFileName(f))).ToArray();

        var status = new List<string>();
        if (any) status.Add(files.Count == 1 ? Copy.OneReady : string.Format(Copy.ManyReady, files.Count));
        if (lastSkipped > 0) status.Add(string.Format(Copy.Skipped, lastSkipped));
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
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Copy.PickerTitle, AllowMultiple = true });
        if (picked.Count > 0) AddFiles(LocalPaths(picked));
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
        if (e.Handled || e.Key != Key.V || e.KeyModifiers != KeyModifiers.Control || Clipboard is null) return;
        e.Handled = true;
        AddFiles(LocalPaths(await Clipboard.TryGetFilesAsync()));
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

    // Hooks for later tasks: tool setup and the wheel connect here.
    private void OnSetUpTools(object? sender, RoutedEventArgs e) { }
    private void OnConvert(object? sender, RoutedEventArgs e) { }
    private void OnTools(object? sender, RoutedEventArgs e) { }
    private void OnActions(object? sender, RoutedEventArgs e) { }
}
