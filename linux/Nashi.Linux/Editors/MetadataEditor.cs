using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nashi.Core;

namespace Nashi.Linux.Editors;

/// Metadata for images (macOS metadataControls) through ExifTool: the tags of the first file by group, a search, an
/// editable field for each of MetadataService's EXIF and XMP tags, the rest read-only, and Remove All. The edits apply
/// to every selected image.
public sealed class MetadataEditor(IReadOnlyList<string> inputs) : EditorPanel("removeMetadata", inputs)
{
    private static class Copy
    {
        public const string Hint = "Choose what your file shares.";
        public const string Primary = "Save metadata copy";
        public const string Search = "Search metadata";
        public const string Value = "Value";
        public const string RemoveAll = "Remove All";
        public const string Removing = "Every tag will be removed. Encoded pixels, color profile and display orientation are preserved.";
        public const string Preserved = "Encoded pixels, color profile and display orientation are preserved.";
        public const string Every = "These changes apply to every selected image.";
        public const string Clear = "Clear";
        public const string None = "No metadata found.";
    }

    /// The tags this editor writes: MetadataService's EXIF and XMP ones.
    public static readonly string[] Editable = [.. MetadataService.EditableTags.Where(k => k.StartsWith("EXIF:", StringComparison.Ordinal) || k.StartsWith("XMP:", StringComparison.Ordinal))];

    private IReadOnlyDictionary<string, string> read = new Dictionary<string, string>();
    private readonly Dictionary<string, string> values = [];
    private bool removeAll;
    private string search = "";
    private readonly StackPanel list = new() { Spacing = 14 };
    private TextBlock? note;

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public override bool SideBySide => false;
    public IReadOnlyDictionary<string, string> Values => values;
    public bool RemovesAll => removeAll;

    public override async Task LoadAsync(CancellationToken token)
    {
        read = await AppPaths.Metadata().ReadAsync(Inputs[0], token);
        Reset();
    }

    public override void Reset()
    {
        removeAll = false;
        values.Clear();
        foreach (var key in Editable) values[key] = read.GetValueOrDefault(key, "");
    }

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 14 };
        var box = new TextBox { Watermark = L10n.T(Copy.Search), Text = search, FontSize = 12 };
        box.TextChanged += (_, _) => { search = box.Text ?? ""; Fill(); };
        stack.Children.Add(box);
        stack.Children.Add(list);
        note = Ui.Caption(Copy.Preserved);
        stack.Children.Add(Ui.Row(Ui.Button(Copy.RemoveAll, RemoveAll)));
        stack.Children.Add(note);
        if (Inputs.Count > 1) stack.Children.Add(Ui.Caption(Copy.Every));
        Fill();
        return stack;
    }

    /// macOS Remove All: every tag goes; typed fields after it are written to the clean copy.
    public void RemoveAll()
    {
        removeAll = true;
        foreach (var key in Editable) values[key] = "";
        Fill();
        Change();
    }

    public void Set(string key, string value)
    {
        values[key] = value;
        Change();
    }

    public void Search(string text)
    {
        search = text;
        Fill();
    }

    private bool Shows(string key, string value) => search.Length == 0 || key.Contains(search, StringComparison.OrdinalIgnoreCase) || value.Contains(search, StringComparison.OrdinalIgnoreCase);

    // The groups ("EXIF", "XMP", "File"…), each with its tags: fields for the editable ones, text for the rest.
    private void Fill()
    {
        list.Children.Clear();
        if (note is not null) note.Text = L10n.T(removeAll ? Copy.Removing : Copy.Preserved);
        var rows = values.Select(p => (Key: p.Key, Value: p.Value, Editable: true))
            .Concat(removeAll ? [] : read.Where(p => p.Key != "SourceFile" && !values.ContainsKey(p.Key)).Select(p => (Key: p.Key, Value: p.Value, Editable: false)))
            .Where(r => Shows(r.Key, r.Value)).ToList();
        if (rows.Count == 0) { list.Children.Add(Ui.Caption(Copy.None)); return; }
        foreach (var group in rows.GroupBy(r => r.Key.Split(':')[0]).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            list.Children.Add(Ui.Heading(group.Key));
            foreach (var row in group.OrderBy(r => r.Key, StringComparer.Ordinal))
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*,Auto"), ColumnSpacing = 8 };
                grid.Children.Add(new SelectableTextBlock { Text = row.Key[(row.Key.IndexOf(':') + 1)..], FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                Control value;
                if (row.Editable)
                {
                    var key = row.Key;
                    var field = new TextBox { Text = row.Value, Watermark = L10n.T(Copy.Value), FontSize = 12, Tag = key };
                    // Avalonia raises TextChanged for the initial text later; only typed text counts.
                    string? shown = row.Value;
                    field.TextChanged += (_, _) =>
                    {
                        if (shown is not null && field.Text == shown) return;
                        shown = null;
                        Set(key, field.Text ?? "");
                    };
                    var clear = new Button { Classes = { "quiet", "compact" }, Content = "−", Padding = new Thickness(7, 3) };
                    ToolTip.SetTip(clear, L10n.T(Copy.Clear));
                    clear.Click += (_, _) => field.Text = "";
                    Grid.SetColumn(clear, 2);
                    grid.Children.Add(clear);
                    value = field;
                }
                else value = new SelectableTextBlock { Text = row.Value, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Classes = { "secondary" } };
                Grid.SetColumn(value, 1);
                grid.Children.Add(value);
                list.Children.Add(grid);
            }
        }
    }

    public override EditorJob Job()
    {
        var all = removeAll;
        var updates = values.Where(p => p.Value.Length > 0 && (all || p.Value != read.GetValueOrDefault(p.Key, ""))).ToDictionary(p => p.Key, p => p.Value);
        var removals = all ? [] : values.Where(p => p.Value.Length == 0 && read.GetValueOrDefault(p.Key, "").Length > 0).Select(p => p.Key).ToList();
        return (files, destination, progress, token) => AppPaths.Metadata().WriteAsync(files, destination, all, updates, removals, progress, token);
    }
}
