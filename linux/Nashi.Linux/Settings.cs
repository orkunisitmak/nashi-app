using System.Text.Json;

namespace Nashi.Linux;

/// A watched folder: new files landing in it run `Route` ("convert:<fmt>" or a one-click tool id) beside themselves.
public sealed record WatchFolder(string Path, string Route, bool Enabled = true);

/// The Linux app's own settings (settings.json under AppPaths.Data). Format defaults live in Core's PreferencesStore
/// beside it (preferences.json).
public sealed record LinuxSettings
{
    /// "system", "en" or "tr".
    public string Language { get; init; } = "system";
    /// "system", "light" or "dark".
    public string Appearance { get; init; } = "system";
    /// The primary action and selection colour, six hex digits.
    public string Accent { get; init; } = "E3B859";
    /// The pointer wheel's opacity, 0.6 to 1.
    public double WheelOpacity { get; init; } = 1;
    public bool Sound { get; init; } = true;
    /// The tick's volume, 0 to 1.
    public double TickVolume { get; init; } = .65;
    /// Held while dragging files, it opens the wheel at the pointer: "Ctrl", "Shift", "Alt", two of them with "+", or "Off".
    public string GestureKey { get; init; } = "Ctrl";
    /// "beside" (the source's folder), "ask" (a folder picker every time) or "folder" (OutputFolder).
    public string OutputMode { get; init; } = "beside";
    public string OutputFolder { get; init; } = "";
    /// How new files are named: "plain" ("photo.jpg", numbered when taken) or "nashi" ("photo (Nashi).jpg").
    public string Suffix { get; init; } = "plain";
    public bool WaylandNoticeShown { get; init; }
    public List<WatchFolder> WatchFolders { get; init; } = [];
    /// The Float shelf's files, kept across restarts.
    public List<string> Shelf { get; init; } = [];
    /// The shelf's place: the screen edge it is pinned to ("right", "left", "top", "bottom") and whether it is a pill.
    public string ShelfEdge { get; init; } = "right";
    public bool ShelfCollapsed { get; init; }

    public static readonly string[] Keys = ["Ctrl", "Shift", "Alt", "Ctrl+Shift", "Ctrl+Alt", "Shift+Alt", "Off"];

    /// The same settings with every value inside its range (a hand-edited file can't break the app).
    public LinuxSettings Valid() => this with
    {
        Language = Language is "en" or "tr" ? Language : "system",
        Appearance = Appearance is "light" or "dark" ? Appearance : "system",
        Accent = Accent is { Length: 6 } a && a.All(Uri.IsHexDigit) ? a.ToUpperInvariant() : "E3B859",
        WheelOpacity = double.IsFinite(WheelOpacity) ? Math.Clamp(WheelOpacity, .6, 1) : 1,
        TickVolume = double.IsFinite(TickVolume) ? Math.Clamp(TickVolume, 0, 1) : .65,
        GestureKey = Keys.Contains(GestureKey) ? GestureKey : "Ctrl",
        OutputMode = OutputMode is "ask" or "folder" ? OutputMode : "beside",
        OutputFolder = OutputFolder ?? "",
        Suffix = Suffix is "nashi" ? Suffix : "plain",
        WatchFolders = [.. (WatchFolders ?? []).Where(w => w is { Path.Length: > 0, Route.Length: > 0 } && System.IO.Path.IsPathFullyQualified(w.Path)).Take(50)],
        Shelf = [.. (Shelf ?? []).Where(p => p is { Length: > 0 } && System.IO.Path.IsPathFullyQualified(p)).Distinct().Take(500)],
        ShelfEdge = ShelfEdge is "left" or "top" or "bottom" ? ShelfEdge : "right",
    };
}

/// Loads and saves LinuxSettings atomically; a damaged file gives the defaults.
public static class Settings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static LinuxSettings? current;

    public static string File => Path.Combine(AppPaths.Data, "settings.json");

    public static LinuxSettings Current => current ??= Load();

    /// Something changed the settings (any thread that saved).
    public static event Action<LinuxSettings>? Changed;

    public static LinuxSettings Load()
    {
        try
        {
            if (System.IO.File.Exists(File) && new FileInfo(File).Length < 1_000_000)
                return (JsonSerializer.Deserialize<LinuxSettings>(System.IO.File.ReadAllText(File), Json) ?? new()).Valid();
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { }
        return new();
    }

    public static void Save(LinuxSettings value)
    {
        value = value.Valid();
        Directory.CreateDirectory(AppPaths.Data);
        var temporary = File + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
            System.IO.File.Move(temporary, File, true);
        }
        finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
        current = value;
        Changed?.Invoke(value);
    }

    public static void Update(Func<LinuxSettings, LinuxSettings> change) => Save(change(Current));

    /// Forgets the cached settings (the checks point AppPaths.Data elsewhere).
    public static void Reload() => current = null;
}
