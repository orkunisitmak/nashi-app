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
    }

    private readonly OperationRunner runner = new(tools);
    private IReadOnlyList<string> files = [];
    private PixelPoint? pointer;

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

    /// The editor this flow opened last (it may be closed by now).
    public Editors.ToolEditorWindow? LastEditor { get; private set; }

    /// Tools for the files, with Convert first when they have formats.
    public void ShowTools(IReadOnlyList<string> selection)
    {
        Begin(selection);
        var items = Capabilities.Tools(files).Select(id => new WheelItem(id, L10n.T(FeatureCatalog.ToolNames.GetValueOrDefault(id, id)))).ToList();
        if (Capabilities.Formats(files).Length > 0) items.Insert(0, new WheelItem("convert", L10n.T(Copy.ConvertWheel)));
        ShowWheel(L10n.T(Copy.ToolsWheel), items, Empty(Copy.NoTools, Copy.NoToolsFor));
    }

    /// The format wheel straight away (Home's Convert chip).
    public void ShowFormats(IReadOnlyList<string> selection)
    {
        Begin(selection);
        ShowFormats();
    }

    /// The Actions wheel: Share, Float (not yet), Copy, Move, Copy Path, Terminal.
    public void ShowActions(IReadOnlyList<string> selection)
    {
        Begin(selection);
        ShowWheel(L10n.T(Copy.ActionsWheel), FileActions.Ids.Select(id => new WheelItem("action:" + id, L10n.T(FileActions.Title(id)), FileActions.Available(id))).ToList(), "");
    }

    private void Begin(IReadOnlyList<string> selection)
    {
        files = selection.ToArray();
        pointer = owner is null ? Desktop.Pointer() : null;
    }

    private void ShowFormats() =>
        ShowWheel(L10n.T(Copy.ConvertWheel), Capabilities.Formats(files).Select(f => new WheelItem("convert:" + f, f == "gz" ? "GZIP" : f.ToUpperInvariant())).ToList(), Empty(Copy.NoFormats, Copy.NoFormatsFor));

    /// "No conversions for PDF files yet." when the files share a type; the selection wording otherwise.
    private string Empty(string any, string named)
    {
        var types = files.Select(FeatureCatalog.Extension).Distinct().ToArray();
        return types is [{ Length: > 0 } type] ? L10n.F(named, type.ToUpperInvariant()) : L10n.T(any);
    }

    private void ShowWheel(string title, IReadOnlyList<WheelItem> items, string empty)
    {
        if (Busy || files.Count == 0) return;
        if (items.Count == 0) { SuccessToast.Notice(empty).Present(); return; }
        var wheel = LastWheel = new WheelWindow(title, items, owner is not null ? files : null);
        wheel.Wheel.Chosen += OnChosen;
        if (owner is not null) wheel.Show(owner);
        else wheel.ShowAt(pointer, StartupTime);
        StartupTime = 0;
    }

    private async void OnChosen(WheelItem item)
    {
        try
        {
            if (item.Id == "convert") ShowFormats();
            else if (item.Id.StartsWith("action:", StringComparison.Ordinal)) await ActAsync(item.Id["action:".Length..]);
            else await RunAsync(item);
        }
        catch (Exception error) { notify(L10n.F(Copy.Problem, L10n.T(error.Message))); }
    }

    private async Task RunAsync(WheelItem item)
    {
        if (Busy || files.Count == 0) return;
        var selection = files;
        Editors.EditorJob? job = null;
        // An image tool opens its editor first; Save hands back the job, Cancel or Esc ends here with nothing made.
        Editors.EditorPanel? panel;
        try { panel = Editors.ToolEditorWindow.For(item.Id, selection); }
        catch (MissingToolsException) { await setUpTools(); return; }
        if (panel is not null)
        {
            var editor = LastEditor = new Editors.ToolEditorWindow(panel);
            job = await editor.RunAsync(owner, 0);
            if (job is null || Busy) return;
            selection = panel.Inputs;
        }
        Busy = true;
        try
        {
            var title = item.Id.StartsWith("convert:", StringComparison.Ordinal) ? L10n.F(Copy.ConvertTo, item.Title) : item.Title;
            using var cancellation = new CancellationTokenSource();
            var toast = new ProgressToast(title, cancellation.Cancel);
            if (owner is not null) toast.Show(owner);
            else toast.Show();
            try
            {
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
        finally { Busy = false; }
    }

    private async Task ActAsync(string id)
    {
        if (Busy || files.Count == 0) return;
        Busy = true;
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
            Busy = false;
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
