using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Nashi.Core;

namespace Nashi.Linux;

/// Tools wheel ("Convert" first) → format wheel → OperationRunner with a progress toast → success card.
/// Home passes itself as `owner` and gets the wheels over its window; the background mode passes null and gets
/// them at the pointer. `notify` reports cancels and failures; missing media tools call `setUpTools`.
public sealed class WheelFlow(Toolchain tools, Window? owner, Action<string> notify, Func<Task> setUpTools)
{
    private static class Copy
    {
        public const string ConvertWheel = "Convert";
        public const string ToolsWheel = "Tools";
        public const string ConvertTo = "Convert to {0}";
        public const string NoFormats = "No conversions for this selection yet.";
        public const string NoFormatsFor = "No conversions for {0} files yet.";
        public const string NoTools = "No tools for this selection yet.";
        public const string NoToolsFor = "No tools for {0} files yet.";
        public const string SaveTo = "Save new files to…";
        public const string Cancelled = "Cancelled. Originals kept.";
        public const string Failed = "Couldn’t finish: {0}";
        public const string Problem = "Something went wrong: {0}";
    }

    private readonly OperationRunner runner = new(tools);
    private IReadOnlyList<string> files = [];
    private PixelPoint? pointer;

    public bool Busy { get; private set; }

    /// The wheel this flow showed last (it may be closed by now).
    public WheelWindow? LastWheel { get; private set; }

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
        var wheel = LastWheel = new WheelWindow(title, items);
        wheel.Wheel.Chosen += OnChosen;
        if (owner is not null) wheel.Show(owner);
        else wheel.ShowAt(pointer);
    }

    private async void OnChosen(WheelItem item)
    {
        try
        {
            if (item.Id == "convert") ShowFormats();
            else await RunAsync(item);
        }
        catch (Exception error) { notify(L10n.F(Copy.Problem, L10n.T(error.Message))); }
    }

    private async Task RunAsync(WheelItem item)
    {
        if (Busy || files.Count == 0) return;
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
                var destination = await DestinationAsync(files[0], owner ?? toast);
                if (destination is null) { toast.Close(); return; }
                var outputs = await runner.RunAsync(item.Id, files, destination, new Progress<OperationProgress>(toast.Update), cancellation.Token);
                toast.Close();
                SuccessToast.Done(outputs, destination).Present();
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

    /// The first file's folder when Nashi may write there; otherwise the user picks one (null when cancelled).
    private static async Task<string?> DestinationAsync(string first, TopLevel host)
    {
        var folder = Path.GetDirectoryName(first);
        if (folder is not null && Writable(folder)) return folder;
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
