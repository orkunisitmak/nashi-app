using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Nashi.Core;

namespace Nashi.Linux;

/// Tools wheel ("Convert" first) → format wheel → OperationRunner with a progress toast → success card; and the
/// Actions wheel → FileActions → success card. Home passes itself as `owner` and gets the wheels over its window,
/// with the files as cards; the background mode passes null and gets bare wheels at the pointer.
/// `notify` reports cancels and failures; missing media tools call `setUpTools`.
public sealed class WheelFlow(Toolchain tools, Window? owner, Action<string> notify, Func<Task> setUpTools)
{
    private static class Copy
    {
        public const string ConvertWheel = "Convert";
        public const string ToolsWheel = "Tools";
        public const string ActionsWheel = "Actions";
        public const string CopyTo = "Copy to…";
        public const string MoveTo = "Move to…";
        public const string Copied = "Copied.";
        public const string Moved = "Moved.";
        public const string PathCopied = "Path copied.";
        public const string PathsCopied = "File paths copied.";
        public const string TerminalOpened = "Terminal opened.";
        public const string NoTerminal = "No terminal app found.";
        public const string Shared = "Shown in your file manager. Share it from there.";
        public const string NoFileManager = "Couldn’t open your file manager.";
        public const string Files = "{0} files";
        public const string AlreadyThere = "Already there";
        public const string NoPicker = "Couldn’t open the folder picker.";
        public const string NotLocal = "Choose a folder on this computer.";
        public const string ConvertTo = "Convert to {0}";
        public const string NoFormats = "No conversions for this selection yet.";
        public const string NoFormatsFor = "No conversions for {0} files yet.";
        public const string NoTools = "No tools for this selection yet.";
        public const string NoToolsFor = "No tools for {0} files yet.";
        public const string SaveTo = "Save new files to…";
        public const string Cancelled = "Cancelled. Originals kept.";
        public const string Failed = "Couldn’t finish: {0}";
        public const string Problem = "Something went wrong: {0}";
        public const string NothingMade = "Nothing was created.";
        public const string Waiting = "Waiting for the current task to finish…";
        public const string DropHere = "Drop on a petal";
        public const string MissedPetal = "Dropped between the petals: nothing was changed. Drop on a petal to use it.";
        public const string Unreadable = "Nashi couldn’t read what was dropped. Drop files from your file manager or browser.";
        public const string Downloading = "Downloading…";
        public const string SideTo = "{0} ›";
        public const string QuickWheel = "Quick tools";
        public const string Rotate = "Rotate 90°";
        public const string Flip = "Flip";
        public const string Grayscale = "Grayscale";
        public const string RemoveBackground = "Remove Background";
        public const string Zip = "Pack into ZIP";
        public const string Images = "Images";
        public const string Video = "Video";
        public const string Audio = "Audio";
        public const string Documents = "Documents";
        public const string Archives = "Archives";
    }

    private readonly OperationRunner runner = new(tools);
    private IReadOnlyList<string> files = [];
    private PixelPoint? pointer;

    /// A run or action is working (others chosen meanwhile wait their turn, with a card saying so).
    public bool Busy { get; private set; }

    /// Where Copy and Move go: the folder picker over `host`; null when cancelled. It throws
    /// InvalidOperationException with the message to show when it can't open or the folder isn't local.
    /// The checks swap in their own.
    public Func<TopLevel, string, Task<string?>> PickFolder { get; set; } = PickLocalFolderAsync;

    public static async Task<string?> PickLocalFolderAsync(TopLevel host, string title)
    {
        if (!host.StorageProvider.CanPickFolder) throw new InvalidOperationException(Copy.NoPicker);
        IReadOnlyList<IStorageFolder> picked;
        try { picked = await host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title }); }
        catch (Exception error) when (error is not OperationCanceledException) { throw new InvalidOperationException(Copy.NoPicker, error); }
        if (picked.Count == 0) return null; // cancelled: nothing to say
        return picked[0].TryGetLocalPath() ?? throw new InvalidOperationException(Copy.NotLocal);
    }

    /// The launcher's X server time for activating the next pointer wheel (0 = CurrentTime); used once.
    public uint StartupTime { get; set; }

    /// The wheel this flow showed last (it may be closed by now).
    public WheelWindow? LastWheel { get; private set; }

    /// The progress card this flow showed last (it may be closed by now).
    public ProgressToast? LastToast { get; private set; }

    /// The QR reader this flow opened last (it may be closed by now).
    public Editors.QrReaderWindow? LastReader { get; private set; }

    /// The editor this flow opened last (it may be closed by now).
    public Editors.ToolEditorWindow? LastEditor { get; private set; }

    /// The faces of the one wheel every entry point opens, in their Tab order.
    public static readonly string[] Faces = ["convert", "quick", "tools", "actions"];

    /// The wheel on its front face, the formats (Open With, the right-click script, Ctrl+Alt+N, Ctrl+Space, Home's
    /// Convert chip, a drop on Float).
    public void ShowFormats(IReadOnlyList<string> selection) => Show(selection, "convert");

    /// The same wheel, opened on its Tools face (Home's Tools chip).
    public void ShowTools(IReadOnlyList<string> selection) => Show(selection, "tools");

    /// The same wheel, opened on its Actions face ("Nashi actions", Home's Actions chip).
    public void ShowActions(IReadOnlyList<string> selection) => Show(selection, "actions");

    /// The wheel for the files, opened on `face` (or the first face it has). Every way in builds the same faces;
    /// Tab, the mouse wheel and each face's last petal turn through them.
    public void Show(IReadOnlyList<string> selection, string face = "convert")
    {
        Begin(selection);
        if (files.Count == 0) return;
        var sides = Sides(files);
        if (sides.Count == 0) { SuccessToast.Notice(Empty(Copy.NoTools, Copy.NoToolsFor)).Present(); return; }
        var wheel = LastWheel = new WheelWindow("", [], owner is not null ? files : null);
        wheel.Wheel.Sides = sides;
        wheel.Wheel.StartAt(Math.Max(0, sides.ToList().FindIndex(s => s.Key == face)));
        wheel.Wheel.Chosen += OnChosen;
        if (owner is not null) wheel.Show(owner);
        else wheel.ShowAt(pointer, StartupTime);
        StartupTime = 0;
    }

    private static List<WheelItem> ActionItems() =>
        FileActions.Ids.Select(id => new WheelItem("action:" + id, L10n.T(FileActions.Title(id)), FileActions.Available(id))).ToList();

    private static string ToolName(string id) => L10n.T(id.StartsWith("quick:", StringComparison.Ordinal) ? QuickNames[id] : FeatureCatalog.ToolNames.GetValueOrDefault(id, id));

    private static readonly Dictionary<string, string> QuickNames = new()
    {
        ["quick:rotate"] = Copy.Rotate, ["quick:flip"] = Copy.Flip, ["quick:grayscale"] = Copy.Grayscale,
        ["quick:removeBackground"] = Copy.RemoveBackground, ["quick:zip"] = Copy.Zip,
    };

    /// The formats, grouped when they don't fit on one face with its turn petal: a group petal (Images, Video,
    /// Audio, Documents, Archives) fans out a second ring of its formats.
    private static List<WheelItem> FormatItems(IReadOnlyList<string> selection)
    {
        static WheelItem Format(string f) => new("convert:" + f, f == "gz" ? "GZIP" : f.ToUpperInvariant());
        var formats = Capabilities.Formats(selection);
        if (formats.Length <= 7) return [.. formats.Select(Format)];
        static string Category(string f) => FeatureCatalog.IsImage("x." + f) ? Copy.Images : FeatureCatalog.IsVideo("x." + f) ? Copy.Video
            : FeatureCatalog.IsAudio("x." + f) ? Copy.Audio : FeatureCatalog.IsArchive("x." + f) ? Copy.Archives : Copy.Documents;
        return [.. formats.GroupBy(Category).Select(g => g.Count() == 1 ? Format(g.First())
            : new WheelItem("group:" + g.Key.ToLowerInvariant(), L10n.T(g.Key), Group: [.. g.Select(Format)]))];
    }

    /// The faces for these files, in Tab order: Convert (the formats), Quick tools (crop, rotate, flip, grayscale,
    /// remove background, one PDF, one ZIP), Tools (every catalog tool), Actions. Empty faces are left out, and each
    /// face ends with a petal naming the next ("Quick tools ›", "Tools ›", "Actions ›", "Convert ›").
    public static IReadOnlyList<WheelSide> Sides(IReadOnlyList<string> selection)
    {
        var faces = new[]
        {
            ("convert", L10n.T(Copy.ConvertWheel), FormatItems(selection)),
            ("quick", L10n.T(Copy.QuickWheel), Capabilities.QuickTools(selection).Select(id => new WheelItem(id, ToolName(id))).ToList()),
            ("tools", L10n.T(Copy.ToolsWheel), Capabilities.Tools(selection).Select(id => new WheelItem(id, ToolName(id))).ToList()),
            ("actions", L10n.T(Copy.ActionsWheel), ActionItems()),
        }.Where(f => f.Item3.Count > 0).ToList();
        return faces.Select((f, i) => new WheelSide(f.Item2, faces.Count < 2 ? f.Item3
            : [.. f.Item3, new WheelItem("side:" + (i + 1) % faces.Count, L10n.F(Copy.SideTo, faces[(i + 1) % faces.Count].Item2))], f.Item1)).ToList();
    }

    private IDisposable? dropTarget;
    private WheelWindow? dragWheel;
    private bool dropped;

    /// Feeds Tab presses during a drag (the X11 poll); on while a drag wheel is up.
    public PointerSampler? Sampler { get; set; }

    /// The drag gesture fired: an empty wheel at the pointer that fills with the dragged files' sides when the drag
    /// reaches it. A drop on a petal runs it for those files (browser items are downloaded first); a drop on a side
    /// or page petal turns the wheel and leaves it up for a click. A drop anywhere else is the source's own.
    public void ShowForDrag(PixelPoint at)
    {
        EndDragWheel();
        files = [];
        pointer = at;
        dropped = false;
        IReadOnlyList<WebItem> web = [];
        var wheel = dragWheel = LastWheel = new WheelWindow(L10n.T(Copy.DropHere), []);
        wheel.Wheel.Chosen += OnChosen;
        wheel.Closed += (_, _) => { if (dragWheel == wheel) EndDragWheel(); };
        wheel.ShowForDrop(at);
        dropTarget = wheel.DropTarget(content =>
        {
            web = content.Web;
            var downloads = BrowserDrop.DownloadFolder();
            files = content.Files.Count > 0 ? content.Files : [.. web.Select(w => Path.Combine(downloads, w.Name))];
            wheel.Wheel.Sides = Sides(files);
        }, async (content, point) =>
        {
            dropped = true;
            dropTarget?.Dispose();
            dropTarget = null;
            if (Sampler is { } sampler) sampler.WatchFlip = false;
            try
            {
                if (content.Web.Count > 0)
                {
                    if (await DownloadAsync(content.Web) is not { } saved) { wheel.Close(); return; }
                    files = saved;
                }
                else files = content.Files;
                if (!wheel.Wheel.ChooseAt(point) && wheel.IsVisible) wheel.Engage();
            }
            catch (Exception error) { wheel.Close(); notify(L10n.F(Copy.Problem, L10n.T(error.Message))); }
        }, refused: () => notify(L10n.T(wheel.Known ? Copy.MissedPetal : Copy.Unreadable)));
        if (Sampler is { } s) s.WatchFlip = true;
        // The petals come at once, from the drag source itself; the first XDND position over the wheel is the fallback.
        _ = Task.Run(() => XdndTarget.ReadDrag(TimeSpan.FromMilliseconds(300))).ContinueWith(read =>
        {
            if (read.Result is { } content) Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (dragWheel == wheel) wheel.Know(content); });
        }, TaskScheduler.Default);
    }

    /// The drag's button came up: a wheel nothing was dropped on closes (a drop arrives around the release).
    public void DragEnded()
    {
        if (Sampler is { } sampler) sampler.WatchFlip = false;
        var wheel = dragWheel;
        Desktop.After(TimeSpan.FromMilliseconds(300), () => { if (wheel == dragWheel && !dropped) EndDragWheel(); });
    }

    /// Tab during a drag: the drag wheel turns to its next page or side.
    public void FlipDrag()
    {
        if (dragWheel is { IsVisible: true } wheel && !dropped) wheel.Wheel.Flip(1);
    }

    private void EndDragWheel()
    {
        dropTarget?.Dispose();
        dropTarget = null;
        var wheel = dragWheel;
        dragWheel = null;
        if (wheel is { IsVisible: true } && !dropped) wheel.Close();
    }

    /// The browser items into the download folder, with a cancellable card; null when cancelled or failed (said).
    private async Task<IReadOnlyList<string>?> DownloadAsync(IReadOnlyList<WebItem> items)
    {
        using var cancellation = new CancellationTokenSource();
        var toast = LastToast = new ProgressToast(L10n.T(Copy.Downloading), cancellation.Cancel);
        toast.Show();
        toast.Update(new(null, items.Count == 1 ? items[0].Name : L10n.F(Copy.Files, items.Count)));
        try { return await BrowserDrop.SaveAsync(items, BrowserDrop.DownloadFolder(), cancellation.Token); }
        catch (OperationCanceledException) { notify(L10n.T(Copy.Cancelled)); return null; }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TimeoutException)
        {
            notify(L10n.F(Copy.Failed, L10n.T(error.Message)));
            return null;
        }
        finally { toast.Close(); }
    }

    private void Begin(IReadOnlyList<string> selection)
    {
        files = selection.ToArray();
        pointer = owner is null ? Desktop.Pointer() : null;
    }


    /// "No conversions for PDF files yet." when the files share a type; the selection wording otherwise.
    private string Empty(string any, string named)
    {
        var types = files.Select(FeatureCatalog.Extension).Distinct().ToArray();
        return types is [{ Length: > 0 } type] ? L10n.F(named, type.ToUpperInvariant()) : L10n.T(any);
    }

    private async void OnChosen(WheelItem item)
    {
        try
        {
            if (item.Id.StartsWith("action:", StringComparison.Ordinal)) await ActAsync(item.Id["action:".Length..]);
            else await RunAsync(item);
        }
        catch (Exception error) { notify(L10n.F(Copy.Problem, L10n.T(error.Message))); }
    }

    /// The run in progress (or the last one); a saved editor's job waits for it, then runs. The checks set it.
    internal Task Current { get; set; } = Task.CompletedTask;

    private async Task RunAsync(WheelItem item)
    {
        if (files.Count == 0) return;
        var selection = files;
        // One editor at a time, as on macOS: choosing another tool while one is open brings that one back.
        if (LastEditor is { IsVisible: true } open && (item.Id == "readQRCode" || Editors.ToolEditorWindow.Opens(item.Id, selection)))
        {
            open.Activate();
            Desktop.Activate(open, 0);
            return;
        }
        if (item.Id == "readQRCode")
        {
            (LastReader = new Editors.QrReaderWindow(selection)).Present(owner);
            return;
        }
        Editors.EditorJob? job = null;
        // An image tool opens its editor first; Save hands back the job, Cancel or Esc ends here with nothing made.
        Editors.EditorPanel? panel;
        try { panel = Editors.ToolEditorWindow.For(item.Id, selection, tools); }
        catch (MissingToolsException) { await setUpTools(); return; }
        if (panel is not null)
        {
            var editor = LastEditor = new Editors.ToolEditorWindow(panel);
            job = await editor.RunAsync(owner, 0);
            if (job is null) return;
            selection = panel.Inputs;
        }
        // Never dropped: when a run is busy this one waits for it, with its progress card up meanwhile.
        await QueuedAsync(previous => ExecuteAsync(item, selection, job, previous));
    }

    /// Runs `work` after everything chosen before it. Busy goes false only when nothing else is queued, and before
    /// the next waiter resumes (it resumes asynchronously), so no run can slip in between.
    private async Task QueuedAsync(Func<Task, Task> work)
    {
        var previous = Current;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Current = done.Task;
        try { await work(previous); }
        finally
        {
            // A link never completes before its predecessor: a wait cancelled from its card still holds its place
            // until the run before it ends, so nothing queued behind it can start alongside that run.
            await Task.WhenAny(previous);
            if (Current == done.Task) Busy = false;
            done.SetResult();
        }
    }

    /// A card while `previous` finishes; false when its Cancel was pressed.
    private async Task<bool> WaitTurnAsync(Task previous, string title)
    {
        if (previous.IsCompleted) return true;
        using var cancellation = new CancellationTokenSource();
        var toast = LastToast = new ProgressToast(title, cancellation.Cancel);
        if (owner is not null) toast.Show(owner);
        else toast.Show();
        toast.Update(new(null, L10n.T(Copy.Waiting)));
        await Task.WhenAny(previous, Task.Delay(Timeout.Infinite, cancellation.Token));
        toast.Close();
        return !cancellation.IsCancellationRequested;
    }

    private async Task ExecuteAsync(WheelItem item, IReadOnlyList<string> selection, Editors.EditorJob? job, Task previous)
    {
        var title = item.Id.StartsWith("convert:", StringComparison.Ordinal) ? L10n.F(Copy.ConvertTo, item.Title) : item.Title;
        using var cancellation = new CancellationTokenSource();
        var toast = LastToast = new ProgressToast(title, cancellation.Cancel);
        if (owner is not null) toast.Show(owner);
        else toast.Show();
        try
        {
            if (!previous.IsCompleted)
            {
                toast.Update(new(null, L10n.T(Copy.Waiting)));
                await previous;
                cancellation.Token.ThrowIfCancellationRequested();
            }
            Busy = true;
            var outputs = await runner.RunBesideSourcesAsync(item.Id, selection, folder => DestinationAsync(folder, owner ?? toast), new Progress<OperationProgress>(toast.Update), cancellation.Token, job);
            toast.Close();
            if (outputs is null) return;
            if (outputs.Count == 0) SuccessToast.Notice(L10n.T(Copy.NothingMade)).Present();
            else SuccessToast.Done(outputs, Path.GetDirectoryName(outputs[0])!, runner.Note).Present();
        }
        catch (Exception error)
        {
            toast.Close();
            if (error is MissingToolsException) await setUpTools();
            else notify(error is OperationCanceledException ? L10n.T(Copy.Cancelled) : L10n.F(Copy.Failed, L10n.T(error.Message)));
        }
    }

    private Task ActAsync(string id)
    {
        if (files.Count == 0) return Task.CompletedTask;
        var selection = files;
        return QueuedAsync(async previous =>
        {
            if (!await WaitTurnAsync(previous, L10n.T(FileActions.Title(id)))) { notify(L10n.T(Copy.Cancelled)); return; }
            Busy = true;
            await ActOnAsync(id, selection);
        });
    }

    private async Task ActOnAsync(string id, IReadOnlyList<string> files)
    {
        // Background mode has no window: a tiny one at the pointer owns the picker and the clipboard while they work.
        var host = owner ?? PickerHost();
        try
        {
            var name = files.Count == 1 ? Path.GetFileName(files[0]) : L10n.F(Copy.Files, files.Count);
            switch (id)
            {
                case "copy" or "move":
                    var move = id == "move";
                    if (await PickFolder(host, L10n.T(move ? Copy.MoveTo : Copy.CopyTo)) is not { } folder) return;
                    var selection = files;
                    if (!move)
                    {
                        await Task.Run(() => FileActions.CopyTo(selection, folder));
                        SuccessToast.Action(L10n.T(Copy.Copied), name, folder).Present();
                        break;
                    }
                    var moved = await Task.Run(() => FileActions.MoveTo(selection, folder));
                    var movedName = moved.Files.Count == 1 ? Path.GetFileName(moved.Files[0]) : L10n.F(Copy.Files, moved.Files.Count);
                    SuccessToast.Action(L10n.T(moved.Files.Count == 0 ? Copy.AlreadyThere : Copy.Moved), moved.Files.Count == 0 ? name : movedName, folder).Present();
                    break;
                case "copyPath":
                    var text = await FileActions.CopyPathsAsync(host.Clipboard, files);
                    SuccessToast.Action(L10n.T(files.Count == 1 ? Copy.PathCopied : Copy.PathsCopied), files.Count == 1 ? text : name, null).Present();
                    break;
                case "terminal":
                    var first = files[0];
                    if (await Task.Run(() => FileActions.OpenTerminal(first))) SuccessToast.Action(L10n.T(Copy.TerminalOpened), Path.GetDirectoryName(files[0]), null).Present();
                    else notify(L10n.T(Copy.NoTerminal));
                    break;
                case "float":
                    FloatShelf.Open(files, ShowFormats);
                    break;
                case "share":
                    var selected = files;
                    if (await Task.Run(() => FileActions.Share(selected))) SuccessToast.Action(L10n.T(Copy.Shared), name, null).Present();
                    else notify(L10n.T(Copy.NoFileManager));
                    break;
            }
        }
        catch (InvalidOperationException error) { notify(L10n.T(error.Message)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { notify(L10n.F(Copy.Failed, L10n.T(error.Message))); }
        finally
        {
            if (owner is null) host.Close();
        }
    }

    /// A 1×1 transparent, borderless, topmost window at the pointer, activated like the pointer wheel, so the
    /// folder picker has a real parent window. ActAsync closes it.
    private Window PickerHost()
    {
        var host = new Window
        {
            Width = 1, Height = 1, CanResize = false, SystemDecorations = SystemDecorations.None, ShowInTaskbar = false, Topmost = true,
            Background = Avalonia.Media.Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            WindowStartupLocation = WindowStartupLocation.Manual, Title = "Nashi",
        };
        if (pointer is { } at) host.Position = at;
        host.Opened += (_, _) => { host.Activate(); Desktop.Activate(host, 0); };
        host.Show();
        return host;
    }

    /// The source folder when Nashi may write there; otherwise the user picks one (null when cancelled).
    private static async Task<string?> DestinationAsync(string folder, TopLevel host)
    {
        if (Writable(folder)) return folder;
        var picked = await host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L10n.T(Copy.SaveTo) });
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
