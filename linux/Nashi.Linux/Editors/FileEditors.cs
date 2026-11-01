using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Nashi.Core;
using Nashi.Linux.Editors.Media;

namespace Nashi.Linux.Editors;

/// Passwords for locked PDFs and archives: the probe that says one is needed, the check that it opens, and the field.
public static class Locks
{
    private static class Copy
    {
        public const string Pdf = "PDF password (if needed)";
        public const string Archive = "Archive password (if needed)";
        public const string Wrong = "The password is missing or incorrect.";
        public const string Locked = "{0} is password-protected. Enter its password.";
    }

    /// Whether the PDF opens without (or with) this password.
    public static bool PdfOpens(string path, string password = "")
    {
        try
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(path, new UglyToad.PdfPig.ParsingOptions { Password = password });
            return true;
        }
        catch (UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException) { return false; }
    }

    /// Whether the archive has encrypted entries (or headers) that need a password.
    public static bool ArchiveLocked(string path) => !ArchiveOpens(path, null);

    /// Whether the archive's first encrypted entry decrypts with this password (true when nothing is encrypted).
    public static bool ArchiveOpens(string path, string? password)
    {
        try
        {
            using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(path, new SharpCompress.Readers.ReaderOptions { Password = password });
            var entry = archive.Entries.FirstOrDefault(e => e.IsEncrypted && !e.IsDirectory);
            if (entry is null) return true;
            if (password is null) return false;
            using var stream = entry.OpenEntryStream();
            stream.ReadByte();
            return true;
        }
        // A wrong password, or headers that are encrypted too.
        catch (Exception error) when (error is System.Security.Cryptography.CryptographicException || error.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase)) { return false; }
        // Anything else (a damaged archive) is for the extraction to report.
        catch (Exception error) when (error is InvalidDataException or IOException or NotSupportedException or InvalidOperationException or ArgumentException or SharpCompress.Common.SharpCompressException) { return true; }
    }

    public static string Refused => L10n.T(Copy.Wrong);
    public static string Says(string file) => L10n.F(Copy.Locked, Path.GetFileName(file));

    /// The password field with its label; `changed` gets every edit.
    public static Control Field(bool archive, string value, Action<string> changed)
    {
        var box = new TextBox { PasswordChar = '•', Text = value, FontSize = 12, Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        box.TextChanged += (_, _) => changed(box.Text ?? "");
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = L10n.T(archive ? Copy.Archive : Copy.Pdf), FontSize = 12 });
        stack.Children.Add(box);
        return stack;
    }
}

/// Compress for video, audio, PDF and mixed selections (macOS compressionControls): the original size, Balanced or
/// Strong, and an optional target in MB, KB or % smaller for the media. PDFs recompress their content streams (Core
/// has no quality setting for them). Images in the selection get the image compressor at the preset's quality.
public sealed class FileCompressEditor(IReadOnlyList<string> inputs, MediaTools? tools) : EditorPanel("compress", inputs)
{
    private static class Copy
    {
        public const string Hint = "Make some space. Keep what matters.";
        public const string Primary = "Compress & save";
        public const string Original = "Original size: {0}";
        public const string Originals = "Original size: {0} in {1} files";
        public const string Preset = "Compression preset";
        public const string Balanced = "Balanced";
        public const string Strong = "Strong";
        public const string BalancedHint = "Smaller files with less quality loss.";
        public const string StrongHint = "Smaller files with stronger compression.";
        public const string Target = "Compress to target file size";
        public const string Percent = "% reduction";
        public const string Positive = "Enter a positive, finite target size.";
        public const string Reduction = "Enter a reduction greater than 0% and less than 100%.";
        public const string Pdf = "PDFs are rewritten with compressed content streams; text and pages stay as they are.";
        public const string PdfOnly = "Nashi rewrites the PDF with compressed content streams. Text stays selectable and pages stay sharp.";
    }

    private string preset = Copy.Balanced;
    private bool target;
    private double amount = 1;
    private string unit = "MB";
    private string password = "";
    private bool locked;

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public override bool SideBySide => false;
    public override bool ImageSource => false;
    bool HasMedia => Inputs.Any(f => FeatureCatalog.IsAudio(f) || FeatureCatalog.IsVideo(f));

    public override Task LoadAsync(CancellationToken token)
    {
        locked = Inputs.Any(f => FeatureCatalog.Extension(f) == "pdf" && !Locks.PdfOpens(f));
        return Task.CompletedTask;
    }

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 16 };
        var bytes = Inputs.Sum(f => new FileInfo(f).Length);
        stack.Children.Add(new TextBlock { Text = Inputs.Count == 1 ? L10n.F(Copy.Original, CompressEditorSize(bytes)) : L10n.F(Copy.Originals, CompressEditorSize(bytes), Inputs.Count), FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        if (!HasMedia && Inputs.All(f => FeatureCatalog.Extension(f) == "pdf"))
            stack.Children.Add(Ui.Caption(Copy.PdfOnly));
        else
        {
            stack.Children.Add(Ui.Heading(Copy.Preset));
            var presets = new Segments([Copy.Balanced, Copy.Strong], preset);
            var hint = Ui.Caption(preset == Copy.Balanced ? Copy.BalancedHint : Copy.StrongHint);
            presets.Chosen += value => { preset = value; hint.Text = L10n.T(value == Copy.Balanced ? Copy.BalancedHint : Copy.StrongHint); Change(); };
            stack.Children.Add(presets);
            stack.Children.Add(hint);
            if (HasMedia)
            {
                var toggle = new CheckBox { Content = L10n.T(Copy.Target), IsChecked = target, FontSize = 12 };
                var value = new TextBox { Text = amount.ToString(CultureInfo.CurrentCulture), Width = 96, FontSize = 12 };
                var units = new Segments([("MB", "MB"), ("KB", "KB"), ("percent", Copy.Percent)], unit);
                var row = Ui.Row(value, units);
                row.IsVisible = target;
                toggle.IsCheckedChanged += (_, _) => { target = toggle.IsChecked == true; row.IsVisible = target; Change(); };
                value.TextChanged += (_, _) => { amount = double.TryParse(value.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var n) ? n : double.NaN; Change(); };
                units.Chosen += u => { unit = u; Change(); };
                stack.Children.Add(toggle);
                stack.Children.Add(row);
            }
            if (Inputs.Any(f => FeatureCatalog.Extension(f) == "pdf")) stack.Children.Add(Ui.Caption(Copy.Pdf));
        }
        if (locked) stack.Children.Add(Locks.Field(false, password, p => password = p));
        return stack;
    }

    static string CompressEditorSize(long bytes) => bytes switch
    {
        >= 1_000_000 => (bytes / 1_000_000.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB",
        >= 1_000 => (bytes / 1_000.0).ToString("0", CultureInfo.CurrentCulture) + " KB",
        _ => bytes + " B",
    };

    /// Sets the preset and target the way the controls do (the checks use them).
    public void Preset(string value) { preset = value; Change(); }
    public void Target(double value, string inUnit) { (target, amount, unit) = (true, value, inUnit); Change(); }
    public void Password(string value) => password = value;

    public MediaOptions MediaSettings => new()
    {
        Quality = preset == Copy.Strong ? .45 : .8,
        TargetBytes = target && unit != "percent" && double.IsFinite(amount) && amount > 0 ? (long)Math.Max(1, amount * (unit == "MB" ? 1_000_000 : 1_000)) : null,
        ReductionPercent = target && unit == "percent" ? amount : null,
    };

    public override string? Validate()
    {
        if (locked && Inputs.Any(f => FeatureCatalog.Extension(f) == "pdf" && !Locks.PdfOpens(f, password))) return Locks.Refused;
        if (!target) return null;
        if (!double.IsFinite(amount) || amount <= 0) return L10n.T(Copy.Positive);
        if (unit == "percent" && amount >= 100) return L10n.T(Copy.Reduction);
        return null;
    }

    public override void Reset() => (preset, target, amount, unit) = (Copy.Balanced, false, 1, "MB");

    public override EditorJob Job()
    {
        var media = MediaSettings;
        var image = new CompressOptions { Quality = preset == Copy.Strong ? 45 : 80 };
        var engine = tools?.Engine;
        var secret = password;
        return (files, destination, progress, token) => OperationRunner.CompressAsync(files, destination, progress, token, engine, image, media, secret);
    }
}

/// Metadata for video and audio (and mixed selections): the container's tags as fields, written by FFmpeg, and
/// Remove all (on by default, as on macOS), which writes the typed tags to a clean copy. GIFs offer Remove all only.
/// Images and PDFs in the selection go through ExifTool with Remove all.
public sealed class MediaMetadataEditor(IReadOnlyList<string> inputs, MediaTools tools) : EditorPanel("removeMetadata", inputs)
{
    private static class Copy
    {
        public const string Hint = "Choose what your file shares.";
        public const string Primary = "Save metadata copy";
        public const string RemoveAll = "Remove all metadata";
        public const string RemoveAllHint = "Chapters, cover art and every tag go; tags you change below are written to the clean copy.";
        public const string KeepHint = "Other tags stay; the tags below are written as typed.";
        public const string Gif = "GIF metadata can only be removed as a whole.";
        public const string Others = "Images and PDFs in this selection have all their metadata removed with ExifTool.";
        public const string Every = "These changes apply to every selected file.";
        public const string Value = "Value";
    }

    public static readonly string[] Common = ["title", "artist", "album", "date", "genre", "comment", "copyright"];
    private Dictionary<string, string> read = [];
    private readonly Dictionary<string, string> values = [];
    private bool removeAll = true;

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public override bool SideBySide => false;
    public override bool ImageSource => false;
    bool Gif => Inputs.All(f => FeatureCatalog.Extension(f) == "gif");
    public IReadOnlyDictionary<string, string> Values => values;

    public override async Task LoadAsync(CancellationToken token)
    {
        var media = Inputs.First(f => FeatureCatalog.IsAudio(f) || FeatureCatalog.IsVideo(f));
        var json = await ProcessRunner.RunAsync(tools.Ffprobe, ["-v", "error", "-show_entries", "format_tags", "-of", "json", media], token);
        using var doc = JsonDocument.Parse(json.Output);
        read = doc.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("tags", out var tags)
            ? tags.EnumerateObject().ToDictionary(p => p.Name.ToLowerInvariant(), p => p.Value.ToString())
            : [];
        Reset();
    }

    public override void Reset()
    {
        removeAll = true;
        values.Clear();
        foreach (var key in Common.Concat(read.Keys).Distinct()) values[key] = read.GetValueOrDefault(key, "");
    }

    public void RemoveAll(bool on) { removeAll = on; Change(); }
    public void Set(string key, string value) { values[key] = value; Change(); }

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 12 };
        var all = new CheckBox { Content = L10n.T(Copy.RemoveAll), IsChecked = removeAll, FontSize = 12, IsEnabled = !Gif };
        var note = Ui.Caption(removeAll ? Copy.RemoveAllHint : Copy.KeepHint);
        all.IsCheckedChanged += (_, _) => { RemoveAll(all.IsChecked == true); note.Text = L10n.T(removeAll ? Copy.RemoveAllHint : Copy.KeepHint); };
        stack.Children.Add(all);
        if (Gif) { stack.Children.Add(Ui.Caption(Copy.Gif)); return stack; }
        stack.Children.Add(note);
        foreach (var key in values.Keys.ToList())
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), ColumnSpacing = 8 };
            grid.Children.Add(new TextBlock { Text = key, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
            var field = new TextBox { Text = values[key], Watermark = L10n.T(Copy.Value), FontSize = 12 };
            string? shown = values[key];
            field.TextChanged += (_, _) => { if (shown is not null && field.Text == shown) return; shown = null; Set(key, field.Text ?? ""); };
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
            stack.Children.Add(grid);
        }
        if (Inputs.Any(f => !(FeatureCatalog.IsAudio(f) || FeatureCatalog.IsVideo(f)))) stack.Children.Add(Ui.Caption(Copy.Others));
        if (Inputs.Count > 1) stack.Children.Add(Ui.Caption(Copy.Every));
        return stack;
    }

    public MediaOptions Settings => new()
    {
        RemoveAllMetadata = removeAll || Gif,
        // Only changed tags are written: onto the clean copy with Remove all, over the kept ones without (where an
        // emptied field clears its tag).
        Metadata = Gif ? [] : values.Where(p => p.Value != read.GetValueOrDefault(p.Key, "") && (!removeAll || p.Value.Length > 0)).ToDictionary(p => p.Key, p => p.Value),
    };

    public override EditorJob Job()
    {
        var options = Settings;
        var engine = tools.Engine;
        return async (files, destination, progress, token) =>
        {
            var media = files.Where(f => FeatureCatalog.IsAudio(f) || FeatureCatalog.IsVideo(f)).ToList();
            var others = files.Except(media).ToList();
            var outputs = new List<string>(await engine.ToolAsync("removeMetadata", media, destination, options, progress, token));
            try { if (others.Count > 0) outputs.AddRange(await AppPaths.Metadata().WriteAsync(others, destination, true, null, null, progress, token)); }
            catch
            {
                foreach (var made in outputs) File.Delete(made);
                throw;
            }
            return outputs;
        };
    }
}

/// Extract Archive when the archive is password-protected (unprotected ones extract straight from the wheel): the
/// password, checked before Save, then ArchiveEngine.Extract into one folder beside the archive, with its traversal,
/// link and size protection.
public sealed class ArchiveEditor(IReadOnlyList<string> inputs) : EditorPanel("extractArchive", inputs)
{
    private static class Copy
    {
        public const string Hint = "Fine-tune it. Save a fresh copy.";
        public const string Primary = "Extract";
        public const string Where = "The files go into one new folder beside the archive.";
    }

    private string password = "";

    public override string Hint => Copy.Hint;
    public override string Primary => Copy.Primary;
    public override bool SideBySide => false;
    public override bool ImageSource => false;

    public override Control Options()
    {
        var stack = new StackPanel { Spacing = 14 };
        foreach (var file in Inputs.Where(Locks.ArchiveLocked)) stack.Children.Add(Ui.Caption(Locks.Says(file)));
        stack.Children.Add(Locks.Field(true, password, p => password = p));
        stack.Children.Add(Ui.Caption(Copy.Where));
        return stack;
    }

    public void Password(string value) => password = value;

    public override string? Validate() => Inputs.All(f => Locks.ArchiveOpens(f, password)) ? null : Locks.Refused;

    public override EditorJob Job()
    {
        var secret = password;
        return (files, destination, progress, token) => Task.Run<IReadOnlyList<string>>(() =>
        {
            using var tx = new ExportTransaction(destination);
            var outputs = files.SelectMany(f => { token.ThrowIfCancellationRequested(); return ArchiveEngine.Extract(f, tx.StagingDirectory, Locks.ArchiveLocked(f) ? secret : null, progress, token); }).ToList();
            return tx.Publish(outputs, token);
        }, token);
    }
}
