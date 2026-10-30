using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nashi.Core;

namespace Nashi.Windows;

/// Explicit, isolated visual audit. This never registers global hooks, starts the updater,
/// installs tools, touches the system clipboard, or loads the user's Library/preferences.
internal static class UIAudit
{
    internal static readonly string[] SceneIds =
    [
        "home.empty", "home.selected", "library", "settings.general", "settings.appearance", "settings.shortcuts", "settings.files", "settings.behavior", "settings.formats",
        "settings.updates", "wheel.pdf-multiple", "editor.crop-image", "editor.annotate-image", "editor.frame-image",
        "island.collapsed", "island.expanded.actions", "island.expanded.notes", "island.actions-popup", "island.file-picker"
    ];
    private static readonly string[] Appearances = ["light", "dark"];
    private static readonly string[] Languages = ["en", "tr"];
    private const string RenderTechnique = "wpf-render-target-bitmap-own-window";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// App dispatches --ui-audit OUTPUT here before SingleInstance, tray, hooks or updater startup.
    /// Filters select matrix dimensions; invalid/empty filters fail instead of producing a false pass.
    internal static int Run(string output, string[]? scenes = null, string[]? appearances = null, string[]? languages = null)
    {
        output = Path.GetFullPath(output);
        var marker = Path.Combine(output, ".nashi-ui-audit");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any() && !File.Exists(marker))
        {
            Console.Error.WriteLine("Choose an empty output directory; UI audit will not overwrite unrelated files.");
            return 1;
        }
        Directory.CreateDirectory(output);
        File.WriteAllText(marker, "Nashi UI audit v1\n");
        var captures = new List<Capture>();
        var globalIssues = new List<string>();
        var requestedScenes = scenes ?? SceneIds;
        var requestedAppearances = appearances ?? Appearances;
        var requestedLanguages = languages ?? Languages;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousLanguage = L10n.Language;
        var previousPreferences = WheelPresentation.Preferences;
        var previousFeedback = Feedback.Enabled;
        var previousShutdown = Application.Current.ShutdownMode;
        string? stateDirectory = null;
        try
        {
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            ValidateFilter(requestedScenes, SceneIds, "scene");
            ValidateFilter(requestedAppearances, Appearances, "appearance");
            ValidateFilter(requestedLanguages, Languages, "language");
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Feedback.Enabled = false;
            // A fresh data directory on every run prevents stale settings or history influencing a capture.
            stateDirectory = Path.Combine(output, "isolated-data", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stateDirectory);
            var fixtures = CreateFixtures(Path.Combine(stateDirectory, "fixtures"));
            foreach (var appearance in requestedAppearances)
            foreach (var language in requestedLanguages)
            foreach (var scene in requestedScenes)
            {
                var capture = new Capture(scene, appearance, language);
                captures.Add(capture); // Even construction/readiness failures remain in the manifest.
                try { CaptureScene(output, stateDirectory, fixtures, capture); }
                catch (Exception error)
                {
                    capture.Status = "failed";
                    capture.Issues.Add(error.GetType().Name + ": " + error.Message);
                }
            }
        }
        catch (Exception error)
        {
            var issue = error.GetType().Name + ": " + error.Message;
            globalIssues.Add(issue);
            // A fixture/setup failure must not silently omit any requested scene from the report.
            foreach (var appearance in requestedAppearances)
            foreach (var language in requestedLanguages)
            foreach (var scene in requestedScenes)
            {
                if (captures.Any(c => c.Scene == scene && c.Appearance == appearance && c.Language == language)) continue;
                var capture = new Capture(scene, appearance, language);
                capture.Issues.Add("Audit could not initialize this scene: " + issue);
                captures.Add(capture);
            }
        }
        finally
        {
            Feedback.Enabled = previousFeedback;
            WheelPresentation.Preferences = previousPreferences;
            L10n.Language = previousLanguage;
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            Application.Current.ShutdownMode = previousShutdown;
        }
        var expected = requestedScenes.Length * requestedAppearances.Length * requestedLanguages.Length;
        var passed = globalIssues.Count == 0 && captures.Count == expected && captures.All(c => c.Status == "captured");
        var manifest = new
        {
            schemaVersion = 1,
            platform = "Windows",
            renderTechnique = RenderTechnique,
            realScreenCapture = false,
            limitations = new[]
            {
                "Own-window WPF render, not a desktop screenshot or proof of real pointer/keyboard gestures.",
                "Native title bars, desktop composition, shadows and glass are outside RenderTargetBitmap; wheel uses solid finish.",
                "Windows shell file associations and read-only launch-at-login status may vary by test machine."
            },
            isolation = new
            {
                dataDirectory = stateDirectory is null ? null : Relative(output, stateDirectory),
                clipboard = "Private DataObject fixtures only; system clipboard is never read or written.",
                globalHooks = false, tray = false, updater = false, toolInstallation = false,
                userLibrary = false, userPreferences = false, registryWrites = false
            },
            requested = new { scenes = requestedScenes, appearances = requestedAppearances, languages = requestedLanguages, count = expected },
            fixtureNotes = "Generated image pixels and Unicode filenames are fixed. Library timestamps are fixed UTC; localized date formatting follows this Windows machine's time zone.",
            captures, issues = globalIssues, passed
        };
        try { File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Json), new UTF8Encoding(false)); }
        catch (Exception error) { Console.Error.WriteLine("UI audit manifest could not be written: " + error.Message); return 1; }
        Console.WriteLine($"UI audit: {captures.Count(c => c.Status == "captured")}/{expected} captures passed. {Path.Combine(output, "manifest.json")}");
        return passed ? 0 : 1;
    }

    private static void ValidateFilter(string[] selected, string[] valid, string name)
    {
        if (selected.Length == 0 || selected.Distinct(StringComparer.Ordinal).Count() != selected.Length || selected.Any(value => !valid.Contains(value, StringComparer.Ordinal)))
            throw new ArgumentException($"Invalid {name} filter. Choose: {string.Join(", ", valid)}.");
    }

    private static void CaptureScene(string output, string stateRoot, Fixtures fixtures, Capture capture)
    {
        var culture = CultureInfo.GetCultureInfo(capture.Language == "tr" ? "tr-TR" : "en-US");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        L10n.Language = capture.Language;
        var directory = Path.Combine(stateRoot, capture.Appearance, capture.Language, capture.Scene);
        Directory.CreateDirectory(directory);
        var entries = capture.Scene == "home.empty" ? Array.Empty<LibraryEntry>() : fixtures.Entries;
        File.WriteAllText(Path.Combine(directory, "library.json"), JsonSerializer.Serialize(new
        {
            Appearance = capture.Appearance, Entries = entries, WindowManagementEnabled = false
        }));
        var preferences = new PreferencesStore(directory);
        preferences.Save(new Preferences
        {
            Language = capture.Language, Enabled = false, AutomaticUpdateChecks = false,
            KeepInTray = false, RevealFinished = false, Feedback = false, Animations = false,
            PillModeEnabled = capture.Scene.StartsWith("island.", StringComparison.Ordinal), PillOnboardingCompleted = true,
            WheelFinish = "solid", OutputMode = "folder", OutputFolder = fixtures.Outputs, DownloadFolder = fixtures.Outputs
        });
        var library = new LibraryStore(directory);
        var initial = capture.Scene switch
        {
            "home.selected" => new[] { fixtures.Image },
            "wheel.pdf-multiple" => fixtures.Pdfs,
            _ => null
        };
        var owner = new MainWindow(initial,
            library, checkForUpdates: false, offerToolSetup: false);
        Window? subject = null;
        try
        {
            if (capture.Scene == "library") owner.UIAuditShowWorkspace(true);
            ShowAtMinimum(owner);
            if (capture.Scene.StartsWith("island.", StringComparison.Ordinal))
            {
                var expanded = capture.Scene != "island.collapsed" && capture.Scene != "island.actions-popup";
                var tab = capture.Scene == "island.expanded.notes" ? "notes" : "actions";
                owner.UIAuditPresentIsland(expanded, tab);
                if (capture.Scene == "island.actions-popup") owner.UIAuditShowActionsPopup();
            }
            subject = capture.Scene switch
            {
                "home.empty" or "home.selected" or "library" or "island.collapsed" or
                    "island.expanded.actions" or "island.expanded.notes" or "island.actions-popup" => owner,
                "island.file-picker" => owner.UIAuditCreateFilePicker(fixtures.PickerFolder),
                "settings.general" or "settings.appearance" or "settings.shortcuts" or "settings.files" or "settings.behavior" or "settings.formats" or "settings.updates" => Settings(owner, preferences, library, capture.Scene),
                "wheel.pdf-multiple" => new WheelWindow(owner, owner.UIAuditToolChoices, _ => { }),
                "editor.crop-image" => new ToolEditor(owner, "crop", "Crop Image", fixtures.Image, preview: fixtures.Bitmap),
                "editor.annotate-image" => new ToolEditor(owner, "annotate", "Annotate", fixtures.Image, preview: fixtures.Bitmap),
                "editor.frame-image" => new ToolEditor(owner, "frameImage", "Frame Image", fixtures.Image, preview: fixtures.Bitmap),
                _ => throw new ArgumentException("Unknown scene: " + capture.Scene)
            };
            if (!ReferenceEquals(subject, owner)) ShowAtMinimum(subject);
            var ready = WaitUntil(() => Ready(subject) && (capture.Scene != "island.actions-popup" ||
                owner.UIAuditActionsPopupOpen && owner.UIAuditActionsPopupSurface is { ActualWidth: > 0, ActualHeight: > 0 }) &&
                (capture.Scene != "island.file-picker" || subject is NashiFilePickerWindow picker && picker.UIAuditReady), TimeSpan.FromSeconds(8));
            capture.Checks["loadedAndArranged"] = ready;
            capture.Checks["privateLibrary"] = Path.GetFullPath(library.DirectoryPath).StartsWith(Path.GetFullPath(stateRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            capture.Checks["language"] = L10n.Language == capture.Language;
            capture.Checks["appearance"] = Theme.Dark == (capture.Appearance == "dark");
            AssertActualScene(subject, owner, fixtures, capture);
            if (!ready) capture.Issues.Add("Scene did not reach loaded, arranged and artwork-ready state within 8 seconds.");
            subject.UpdateLayout();
            capture.Width = capture.Scene == "island.actions-popup" && owner.UIAuditActionsPopupSurface is { } measuredPopup
                ? measuredPopup.ActualWidth : subject.ActualWidth;
            capture.Height = capture.Scene == "island.actions-popup" && owner.UIAuditActionsPopupSurface is { } measuredPopupHeight
                ? measuredPopupHeight.ActualHeight : subject.ActualHeight;
            capture.MinimumWidth = subject.MinWidth;
            capture.MinimumHeight = subject.MinHeight;
            capture.Checks["minimumSize"] = capture.Scene == "island.actions-popup"
                ? owner.UIAuditActionsPopupSurface is { ActualWidth: > 100, ActualHeight: > 40 }
                : subject is MainWindow && capture.Scene == "island.collapsed"
                    ? subject.ActualWidth >= 160 && subject.ActualHeight >= 26
                : subject is MainWindow
                    ? subject.ActualWidth >= 390 && subject.ActualHeight >= 60
                    : subject is WheelWindow ||
                (Math.Abs(subject.ActualWidth - subject.MinWidth) <= 2 && Math.Abs(subject.ActualHeight - subject.MinHeight) <= 2);
            if (!capture.Checks["minimumSize"]) capture.Issues.Add("Window did not retain its supported minimum audit size.");
            if (capture.Scene != "island.actions-popup") ValidateLayout(subject, capture);
            if (subject is WheelWindow wheel) ValidateWheel(wheel, capture);
            if (subject is ToolEditor editor) ValidateFooter(editor, capture);
            var image = Path.Combine(output, "captures", capture.Appearance, capture.Language, capture.Scene + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(image)!);
            var rendered = capture.Scene == "island.actions-popup" && owner.UIAuditActionsPopupSurface is { } popupSurface
                ? Render(popupSurface, image)
                : Render(subject, image);
            capture.RenderedPixels = rendered.VisiblePixels;
            capture.RenderedColors = rendered.Colors;
            capture.Checks["nonBlankOwnWindowRender"] = rendered.VisiblePixels > 100 && rendered.Colors >= 3;
            if (!capture.Checks["nonBlankOwnWindowRender"]) capture.Issues.Add("The own-window render is blank or contains insufficient visible scene content.");
            capture.Image = Relative(output, image);
            capture.Checks["imageWritten"] = File.Exists(image) && new FileInfo(image).Length > 100;
            if (!capture.Checks["imageWritten"]) capture.Issues.Add("The own-window render did not produce a PNG.");
            capture.Status = capture.Issues.Count == 0 && capture.Checks.Values.All(value => value) ? "captured" : "failed";
        }
        finally
        {
            if (subject is { IsVisible: true } && !ReferenceEquals(subject, owner)) subject.Close();
            if (owner.IsVisible) owner.Close();
            // Process unload/Closed callbacks (including editor timers), without sending OS input.
            DrainDispatcher();
        }
    }

    private static SettingsWindow Settings(MainWindow owner, PreferencesStore preferences, LibraryStore library, string scene)
    {
        var settings = new SettingsWindow(owner, preferences, () => { }, () => Task.CompletedTask, library);
        var list = Descendants<ListBox>(settings).FirstOrDefault();
        // The visual tree is not yet materialized, so fall back to the logical tree for the page selector.
        list ??= LogicalDescendants<ListBox>(settings).FirstOrDefault();
        if (list is null) throw new InvalidOperationException("Settings navigation is missing.");
        list.SelectedItem = scene switch { "settings.shortcuts" => "Shortcuts", "settings.files" => "Files", "settings.behavior" => "Behavior", "settings.formats" => "Formats", "settings.updates" => "Updates", _ => "Appearance" };
        return settings;
    }

    private static void ShowAtMinimum(Window window)
    {
        if (window is not WheelWindow and not MainWindow)
        {
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
        }
        window.ShowInTaskbar = false;
        window.Show();
        window.UpdateLayout();
    }

    private static bool Ready(Window window)
    {
        if (!window.IsLoaded || !window.IsVisible || window.Content is not FrameworkElement content ||
            !content.IsMeasureValid || !content.IsArrangeValid || content.ActualWidth < 1 || content.ActualHeight < 1) return false;
        return (window is not ToolEditor editor || editor.UIAuditPreviewReady)
            && Descendants<Image>(window).All(image => image.Source is not BitmapImage bitmap || !bitmap.IsDownloading)
            && ArtworkReady(window);
    }

    private static bool ArtworkReady(Window window) => Descendants<Image>(window)
        .Where(image => image.GetValue(FileArtwork.PathProperty) is string path && path.Length > 0)
        .All(FileArtwork.GetIsReady);

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            DrainDispatcher();
            if (condition()) return true;
            // Only a dispatcher wake-up interval; readiness is always checked from actual scene state.
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        return condition();
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void AssertActualScene(Window subject, MainWindow owner, Fixtures fixtures, Capture capture)
    {
        capture.Checks["actualScene"] = capture.Scene switch
        {
            "home.empty" => subject == owner && !owner.LibraryVisible && owner.SelectedFiles.Count == 0,
            "home.selected" => subject == owner && !owner.LibraryVisible && owner.SelectedFiles.SequenceEqual(new[] { fixtures.Image }),
            "library" => subject == owner && owner.LibraryVisible && Descendants<ListBox>(owner).Any() && owner.SearchField.IsVisible,
            "island.collapsed" => subject == owner && !owner.UIAuditIslandExpanded,
            "island.expanded.actions" => subject == owner && owner.UIAuditIslandExpanded && owner.UIAuditIslandTab == "actions",
            "island.expanded.notes" => subject == owner && owner.UIAuditIslandExpanded && owner.UIAuditIslandTab == "notes",
            "island.actions-popup" => subject == owner && owner.UIAuditActionsPopupOpen && IsOpaqueBlack(owner.UIAuditActionsPopupSurface),
            "island.file-picker" => subject is NashiFilePickerWindow picker && picker.UIAuditReady && picker.UIAuditHasSyntheticFiles && IsOpaqueBlack(picker.UIAuditSurface),
            "settings.general" or "settings.appearance" or "settings.shortcuts" or "settings.files" or "settings.behavior" or "settings.formats" or "settings.updates" => subject is SettingsWindow &&
                Descendants<ListBox>(subject).Any(list => Equals(list.SelectedItem, capture.Scene.Split('.')[1] switch { "shortcuts" => "Shortcuts", "files" => "Files", "behavior" => "Behavior", "formats" => "Formats", "updates" => "Updates", _ => "Appearance" })),
            "wheel.pdf-multiple" => subject is WheelWindow && owner.SelectedFiles.SequenceEqual(fixtures.Pdfs) &&
                Descendants<System.Windows.Shapes.Path>(subject).Count(path => path.Data is StreamGeometry) == FeatureCatalog.Tools(fixtures.Pdfs).Length,
            "editor.crop-image" => subject is ToolEditor crop && crop.FieldForTest("Width").Text == fixtures.Bitmap.PixelWidth.ToString(CultureInfo.CurrentCulture),
            "editor.annotate-image" => subject is ToolEditor && Descendants<ComboBox>(subject).Any(combo => Equals(combo.SelectedItem, "Marker")),
            "editor.frame-image" => subject is ToolEditor frame && frame.UIAuditPreviewReady &&
                Descendants<Image>(subject).Any(image => image.Source is BitmapSource source && source.PixelWidth == fixtures.Bitmap.PixelWidth + 160 && source.PixelHeight == fixtures.Bitmap.PixelHeight + 160),
            _ => false
        };
        if (!capture.Checks["actualScene"]) capture.Issues.Add("The requested scene's semantic state was not present.");
    }

    private static void ValidateLayout(Window window, Capture capture)
    {
        var root = (FrameworkElement)window.Content;
        var viewport = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
        var checkedCount = 0;
        foreach (var control in Descendants<Control>(root).Where(control => control.IsVisible && control is ButtonBase or TextBoxBase or ComboBox or Slider or ListBox))
        {
            if (control.ActualWidth <= 0 || control.ActualHeight <= 0) continue;
            if (HasAncestor<ScrollBar>(control, root)) continue;
            // Vertical offscreen content is reachable by scrolling. Still check horizontal
            // containment when horizontal scrolling is disabled, so clipped forms cannot pass.
            var scroll = Ancestor<ScrollViewer>(control, root);
            if (scroll is not null)
            {
                checkedCount++;
                if (scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled) continue;
                var local = control.TransformToAncestor(scroll).TransformBounds(new Rect(control.RenderSize));
                var width = scroll.ViewportWidth > 0 ? scroll.ViewportWidth : scroll.ActualWidth;
                if (local.Left < -1.5 || local.Right > width + 1.5)
                    capture.Issues.Add("Interactive control exceeds its scroll viewport width: " + Describe(control));
                continue;
            }
            var bounds = control.TransformToAncestor(root).TransformBounds(new Rect(control.RenderSize));
            checkedCount++;
            if (!Contains(viewport, bounds)) capture.Issues.Add("Interactive control exceeds its content bounds: " + Describe(control));
        }
        capture.InteractiveControlsChecked = checkedCount;
        capture.Checks["interactiveControlsContained"] = !capture.Issues.Any(issue => issue.StartsWith("Interactive control exceeds", StringComparison.Ordinal));
    }

    private static bool IsOpaqueBlack(FrameworkElement? surface) =>
        surface is Border { Background: SolidColorBrush brush } && brush.Opacity >= 0.99 &&
        brush.Color.R <= 8 && brush.Color.G <= 8 && brush.Color.B <= 8;

    private static void ValidateFooter(ToolEditor editor, Capture capture)
    {
        var root = (Grid)editor.Content;
        var footer = root.Children.OfType<DockPanel>().SingleOrDefault(panel => Grid.GetRow(panel) == 2);
        var visible = footer is { IsVisible: true, ActualHeight: > 0 } && Contains(new Rect(root.RenderSize), footer.TransformToAncestor(root).TransformBounds(new Rect(footer.RenderSize)));
        capture.Checks["editorFooterVisible"] = visible;
        if (!visible) capture.Issues.Add("Editor footer is clipped at the supported minimum window size.");
    }

    private static void ValidateWheel(WheelWindow wheel, Capture capture)
    {
        var canvas = Descendants<Canvas>(wheel).FirstOrDefault();
        var petals = canvas?.Children.OfType<System.Windows.Shapes.Path>().Where(path => path.Data is StreamGeometry).ToArray() ?? [];
        var captions = canvas?.Children.OfType<Button>().Take(petals.Length).ToArray() ?? [];
        var fits = petals.Length == 5 && captions.Length == petals.Length;
        if (canvas is not null)
        for (var index = 0; index < captions.Length; index++)
        {
            var bounds = captions[index].TransformToAncestor(canvas).TransformBounds(new Rect(captions[index].RenderSize));
            if (new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight }.All(point => petals[index].Data.FillContains(point))) continue;
            fits = false; capture.Issues.Add("Wheel caption exceeds its petal: " + Describe(captions[index]));
        }
        capture.Checks["wheelCaptionsContained"] = fits;
    }

    private static bool Contains(Rect outer, Rect inner)
    {
        outer.Inflate(1.5, 1.5);
        return !inner.IsEmpty && double.IsFinite(inner.X) && double.IsFinite(inner.Y) && outer.Contains(inner);
    }

    private static bool HasAncestor<T>(DependencyObject value, DependencyObject stop) where T : DependencyObject
        => Ancestor<T>(value, stop) is not null;

    private static T? Ancestor<T>(DependencyObject value, DependencyObject stop) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(value); parent is not null && !ReferenceEquals(parent, stop); parent = VisualTreeHelper.GetParent(parent))
            if (parent is T found) return found;
        return null;
    }

    private static string Describe(Control control)
    {
        var id = AutomationProperties.GetAutomationId(control);
        var text = control is ContentControl { Content: string label } ? label : control.GetType().Name;
        return control.GetType().Name + (string.IsNullOrEmpty(id) ? "" : "#" + id) + " (" + text + ")";
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed) yield return typed;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T typed) yield return typed;
            foreach (var nested in LogicalDescendants<T>(child)) yield return nested;
        }
    }

    private static (int VisiblePixels, int Colors) Render(FrameworkElement window, string destination)
    {
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var pixels = new byte[checked(width * height * 4)];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var visible = 0; var colors = new HashSet<uint>();
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] == 0) continue;
            visible++;
            if (colors.Count < 64) colors.Add((uint)(pixels[offset] | pixels[offset + 1] << 8 | pixels[offset + 2] << 16 | pixels[offset + 3] << 24));
        }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(destination); encoder.Save(stream);
        return (visible, colors.Count);
    }

    private static Fixtures CreateFixtures(string directory)
    {
        Directory.CreateDirectory(directory);
        var outputs = Path.Combine(directory, "outputs"); Directory.CreateDirectory(outputs);
        var pickerFolder = Path.Combine(directory, "picker-fixtures"); Directory.CreateDirectory(pickerFolder);
        // Fixed pixels, no external images/fonts/codecs or network requests.
        const int width = 640, height = 400;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            var gold = x is > 330 and < 560 && y is > 85 and < 315;
            var leaf = (x - 210) * (x - 210) + (y - 200) * (y - 200) < 100 * 100;
            pixels[offset] = gold ? (byte)89 : leaf ? (byte)91 : (byte)38;
            pixels[offset + 1] = gold ? (byte)184 : leaf ? (byte)132 : (byte)58;
            pixels[offset + 2] = gold ? (byte)227 : leaf ? (byte)111 : (byte)43;
            pixels[offset + 3] = 255;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); bitmap.Freeze();
        var image = Path.Combine(directory, "İstanbul — çalışma örneği, özgün görsel ve uzun dosya adı.png");
        var text = Path.Combine(directory, "Türkçe notlar — ışık, çığ, ölçü ve güvenli kopyalar.txt");
        var pdf1 = Path.Combine(directory, "Birinci çalışma — çoklu PDF örneği.pdf");
        var pdf2 = Path.Combine(directory, "İkinci çalışma — çoklu PDF örneği.pdf");
        // Exercise the injectable clipboard reader with a private data object, never Clipboard.*.
        var privateText = new DataObject(); privateText.SetData(DataFormats.UnicodeText, "Nashi denetimi: I ı İ i, çığ, ölçü, güvenli kopya.\nThe original stays safe.");
        var privateImage = new DataObject(); privateImage.SetData(DataFormats.Bitmap, bitmap);
        if (privateText.GetData(DataFormats.UnicodeText) is not string fixtureText || privateImage.GetData(DataFormats.Bitmap) is not BitmapSource)
            throw new InvalidOperationException("Private clipboard fixture creation failed.");
        var clipboardLibrary = new LibraryStore(Path.Combine(directory, "private-clipboard"));
        var textImport = ClipboardImport.Read(privateText, clipboardLibrary);
        var imageImport = ClipboardImport.Read(privateImage, clipboardLibrary);
        if (textImport.Link is not null || textImport.Label != "Pasted text" || imageImport.Link is not null || imageImport.Label != "Pasted image")
            throw new InvalidOperationException("Private clipboard imports did not preserve their text/image kind.");
        // Rename the private reader's generated import names to deterministic UTF-8 scene fixtures.
        File.Move(textImport.Paths.Single(), text); File.Move(imageImport.Paths.Single(), image);
        if (File.ReadAllText(text) != fixtureText || ImageEngine.Load(image) is not { PixelWidth: width, PixelHeight: height })
            throw new InvalidOperationException("Private clipboard text/image import roundtrip failed.");
        ImageEngine.Save(bitmap, pdf1, "pdf"); ImageEngine.Save(bitmap, pdf2, "pdf");
        File.Copy(image, Path.Combine(pickerFolder, "Example image.png"));
        File.Copy(text, Path.Combine(pickerFolder, "Example notes.txt"));
        var created = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        return new Fixtures(image, text, [pdf1, pdf2], outputs, pickerFolder, bitmap,
        [
            new(new Guid("11111111-1111-1111-1111-111111111111"), created, "Convert", [image]),
            new(new Guid("22222222-2222-2222-2222-222222222222"), created.AddMinutes(-1), "Pasted text", [text]),
            new(new Guid("33333333-3333-3333-3333-333333333333"), created.AddMinutes(-2), "Convert", [pdf1]),
            new(new Guid("44444444-4444-4444-4444-444444444444"), created.AddMinutes(-3), "Convert", [Path.Combine(directory, "Taşınmış veya silinmiş — eksik dosya.png")])
        ]);
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    private sealed record Fixtures(string Image, string Text, string[] Pdfs, string Outputs, string PickerFolder, BitmapSource Bitmap, LibraryEntry[] Entries);
    private sealed class Capture(string scene, string appearance, string language)
    {
        public string Scene { get; } = scene;
        public string Appearance { get; } = appearance;
        public string Language { get; } = language;
        public string? Image { get; set; }
        public string Status { get; set; } = "failed";
        public List<string> Issues { get; } = [];
        public string RenderTechnique { get; } = UIAudit.RenderTechnique;
        public bool RealScreenCapture { get; } = false;
        public Dictionary<string, bool> Checks { get; } = [];
        public double Width { get; set; }
        public double Height { get; set; }
        public double MinimumWidth { get; set; }
        public double MinimumHeight { get; set; }
        public int InteractiveControlsChecked { get; set; }
        public int RenderedPixels { get; set; }
        public int RenderedColors { get; set; }
    }
}
