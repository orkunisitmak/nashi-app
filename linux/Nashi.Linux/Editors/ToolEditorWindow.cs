using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nashi.Core;
using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;

namespace Nashi.Linux.Editors;

/// The one window every image tool opens in (macOS ToolEditor): the tool's icon, title and hint over a large preview
/// with the options beside it, and Reset · Original stays safe · Cancel · Save at the bottom. Return (or Ctrl+S)
/// saves, Esc cancels. Previews render on a smaller copy, 120 ms after the last change, off the UI thread; Save
/// closes the window and hands back the full-size job.
public sealed class ToolEditorWindow : Window
{
    private static class Copy
    {
        public const string Preview = "PREVIEW";
        public const string MakeItYours = "MAKE IT YOURS";
        public const string Safe = "Original stays safe";
        public const string Reset = "Reset";
        public const string Cancel = "Cancel";
        public const string Original = "Original";
        public const string Result = "Result";
        public const string Preparing = "Preparing your file…";
        public const string Files = "{0} files";
        public const string Close = "Close";
    }

    /// The editor for a wheel choice on these files, or null when the choice runs without one.
    public static EditorPanel? For(string tool, IReadOnlyList<string> files)
    {
        if (files.Count == 0 || !files.All(FeatureCatalog.IsImage)) return null;
        return tool switch
        {
            "cropImage" => new CropEditor(files),
            "editImage" => new PhotoEditor(files),
            "frameImage" => new FrameEditor(files),
            "redactImage" => new RedactEditor(files),
            "compress" => new CompressEditor(files),
            "removeMetadata" => AppPaths.Metadata().Executable is null ? throw new MissingToolsException(MissingToolsException.Metadata) : new MetadataEditor(files),
            "annotateImage" => new AnnotateEditor(files),
            _ => null,
        };
    }

    private readonly EditorPanel panel;
    private readonly ImageCanvas canvas = new() { MinHeight = 320 };
    private readonly ContentControl options = new();
    private readonly TextBlock message = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, IsVisible = false, Foreground = Ui.Gold };
    private readonly TextBlock dimensions = new() { FontSize = 10, FontFamily = new FontFamily("monospace"), Classes = { "secondary" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button save = new() { Classes = { "primary" }, IsEnabled = false };
    private readonly Segments compare = new([Copy.Original, Copy.Result], Copy.Result) { IsVisible = false };
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private Bitmap? original;
    private Bitmap? result;
    private Size resultSize;
    private long generation;
    private bool ready;

    public EditorPanel Panel => panel;
    /// The job Save handed back; null when cancelled.
    public EditorJob? Job { get; private set; }
    /// Done once the source is loaded and the options are built.
    public Task Loading { get; }
    /// The latest preview render.
    public Task Rendering { get; private set; } = Task.CompletedTask;
    internal ImageCanvas Canvas => canvas;
    internal Button SaveButton => save;

    public ToolEditorWindow(EditorPanel panel)
    {
        this.panel = panel;
        Desktop.Translucent(this);
        var name = FeatureCatalog.ToolNames.GetValueOrDefault(panel.Tool, panel.Tool);
        Title = L10n.T(name) + " · Nashi";
        Width = panel.SideBySide ? 1000 : 640;
        Height = panel.SideBySide ? 760 : 640;
        MinWidth = panel.SideBySide ? 900 : 480;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { RowDefinitions = new RowDefinitions("82,Auto,*,Auto,Auto") };
        root.Children.Add(Header(name));
        root.Children.Add(Divider(1));
        var body = Body();
        Grid.SetRow(body, 2);
        root.Children.Add(body);
        root.Children.Add(Divider(3));
        var footer = Footer();
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);
        Content = root;

        canvas.Panel = panel;
        panel.PickImage ??= PickImageAsync;
        panel.Changed += OnChanged;
        panel.Message += text => { message.Text = text; message.IsVisible = text is not null; };
        compare.Chosen += _ => ShowPreview();
        debounce.Tick += (_, _) => { debounce.Stop(); Render(); };
        KeyDown += OnKey;
        Closed += (_, _) => { debounce.Stop(); lifetime.Cancel(); panel.Source?.Preview.Dispose(); };
        Loading = LoadAsync();
    }

    /// Shows the editor (over `owner` from Home; activated on its own from the pointer flow) and completes with the
    /// job Save hands back, or null.
    public Task<EditorJob?> RunAsync(Window? owner, uint timestamp = 0)
    {
        var done = new TaskCompletionSource<EditorJob?>();
        Closed += (_, _) => done.TrySetResult(Job);
        if (owner is not null) Show(owner);
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Opened += (_, _) => { Activate(); Desktop.Activate(this, timestamp); };
            Show();
        }
        return done.Task;
    }

    private Control Header(string name)
    {
        var close = new Button { Classes = { "quiet" }, Padding = new Thickness(5), CornerRadius = new CornerRadius(13), Content = new PathIcon { Data = (Geometry)Application.Current!.FindResource("IconClose")!, Width = 12, Height = 12 }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(close, L10n.T(Copy.Close));
        close.Click += (_, _) => Close();
        var icon = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(14), 
            Child = new Path { Data = WheelIcons.ForId(panel.Tool), StrokeThickness = WheelIcons.Stroke, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Width = 22, Height = 22, Stretch = Stretch.Uniform },
        };
        icon.Bind(Border.BackgroundProperty, icon.GetResourceObservable("NashiSand"));
        icon.Child.Bind(Shape.StrokeProperty, icon.Child.GetResourceObservable("NashiGoldInk"));
        var titles = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = L10n.T(name), FontSize = 21, FontWeight = FontWeight.Bold, LetterSpacing = -0.5 });
        titles.Children.Add(new TextBlock { Text = L10n.T(panel.Hint), FontSize = 11, Classes = { "secondary" } });
        var pill = new Border
        {
            Classes = { "pill" }, HorizontalAlignment = HorizontalAlignment.Right,
            Child = Ui.Row(new PathIcon { Data = (Geometry)Application.Current!.FindResource("IconDocument")! }, new TextBlock { Text = panel.Inputs.Count > 1 ? L10n.F(Copy.Files, panel.Inputs.Count) : FeatureCatalog.Extension(panel.Inputs[0]).ToUpperInvariant() }),
        };
        ((StackPanel)pill.Child).Spacing = 5;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(20, 17), ColumnSpacing = 13 };
        row.Children.Add(close);
        Grid.SetColumn(icon, 1);
        row.Children.Add(icon);
        Grid.SetColumn(titles, 2);
        row.Children.Add(titles);
        Grid.SetColumn(pill, 3);
        row.Children.Add(pill);
        row.PointerPressed += (_, e) => { if (e.Source == row && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        row.Background = Brushes.Transparent;
        return row;
    }

    private static Border Divider(int row)
    {
        var line = new Border { Height = 1 };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("NashiBorder"));
        Grid.SetRow(line, row);
        return line;
    }

    private Control Body()
    {
        var side = new StackPanel { Spacing = 18, Margin = new Thickness(24) };
        side.Children.Add(Ui.Section(panel.SideBySide ? Copy.MakeItYours : System.IO.Path.GetFileName(panel.Inputs[0])));
        side.Children.Add(options);
        options.Content = new TextBlock { Text = L10n.T(Copy.Preparing), FontSize = 11, Classes = { "secondary" } };
        side.Children.Add(message);
        var scroller = new ScrollViewer { Content = side, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        if (!panel.SideBySide) return scroller;

        var preview = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 18, Margin = new Thickness(24) };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 10 };
        var label = Ui.Row(new Path { Data = WheelIcons.ForId("crop"), StrokeThickness = 2, Width = 10, Height = 10, Stretch = Stretch.Uniform }, Ui.Section(Copy.Preview));
        label.Spacing = 6;
        label.Children[0].Bind(Shape.StrokeProperty, label.Children[0].GetResourceObservable("NashiTextSecondary"));
        ((TextBlock)label.Children[1]).Margin = new Thickness(0);
        top.Children.Add(label);
        Grid.SetColumn(compare, 2);
        top.Children.Add(compare);
        Grid.SetColumn(dimensions, 3);
        top.Children.Add(dimensions);
        preview.Children.Add(top);
        Grid.SetRow(canvas, 1);
        preview.Children.Add(canvas);
        var file = Ui.Row(new PathIcon { Data = (Geometry)Application.Current!.FindResource("IconDocument")!, Width = 10, Height = 10 }, new TextBlock { Text = System.IO.Path.GetFileName(panel.Inputs[0]), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        file.Opacity = .6;
        Grid.SetRow(file, 2);
        preview.Children.Add(file);
        var left = new Border { Child = preview };
        left.Bind(Border.BackgroundProperty, left.GetResourceObservable("NashiSurface"));

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,380") };
        body.Children.Add(left);
        var line = new Border { Width = 1 };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("NashiBorder"));
        Grid.SetColumn(line, 1);
        body.Children.Add(line);
        Grid.SetColumn(scroller, 2);
        body.Children.Add(scroller);
        return body;
    }

    private Control Footer()
    {
        var reset = new Button { Classes = { "quiet" }, Content = Ui.Row(new PathIcon { Data = Geometry.Parse("M8,2 A6,6 0 1 1 2.4,6 H4.2 A4.3,4.3 0 1 0 8,3.7 V6 L4.5,3 L8,0 Z"), Width = 12, Height = 12 }, new TextBlock { Text = L10n.T(Copy.Reset) }) };
        reset.Click += (_, _) => { panel.Reset(); panel.Say(null); BuildOptions(); OnChanged(); };
        var safe = Ui.Row(new PathIcon { Data = (Geometry)Application.Current!.FindResource("IconLock")!, Width = 9, Height = 9 }, new TextBlock { Text = L10n.T(Copy.Safe), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        safe.Opacity = .6;
        safe.Spacing = 5;
        safe.VerticalAlignment = VerticalAlignment.Center;
        var cancel = new Button { Classes = { "secondary" }, Content = L10n.T(Copy.Cancel) };
        cancel.Click += (_, _) => Close();
        save.Content = Ui.Row(new Path { Data = Geometry.Parse("M4 12L12 4 M6 4h6v6"), Stroke = new SolidColorBrush(Color.Parse("#2A2620")), StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, Width = 12, Height = 12, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = L10n.T(panel.Primary) });
        save.Click += (_, _) => Save();
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 12, Margin = new Thickness(20, 15) };
        row.Children.Add(reset);
        Grid.SetColumn(safe, 2);
        row.Children.Add(safe);
        Grid.SetColumn(cancel, 3);
        row.Children.Add(cancel);
        Grid.SetColumn(save, 4);
        row.Children.Add(save);
        return row;
    }

    private async Task LoadAsync()
    {
        try
        {
            var token = lifetime.Token;
            var (image, scale, width, height) = await Task.Run(() => ImageTools.PreviewCopy(panel.Inputs[0]), token);
            await panel.LoadAsync(token);
            var pixels = await Task.Run(() => ImageCanvas.Pixels(image), token);
            if (token.IsCancellationRequested) { image.Dispose(); return; }
            panel.Source = new EditorSource(panel.Inputs[0], image, scale, width, height);
            original = ImageCanvas.Bitmap(pixels);
            canvas.Kept = original;
            ready = true;
            BuildOptions();
            OnChanged();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { message.Text = L10n.T(error.Message); message.IsVisible = true; options.Content = null; }
    }

    /// Builds the options again from the panel's settings (after the checks set them directly).
    internal void Rebuild() => BuildOptions();

    private void BuildOptions()
    {
        if (!ready) return;
        options.Content = panel.Options();
    }

    private void OnChanged()
    {
        if (!ready) return;
        save.IsEnabled = panel.CanSave;
        compare.IsVisible = panel.SideBySide && panel.ComparesResult;
        debounce.Stop();
        debounce.Start();
        ShowPreview();
    }

    /// Renders now rather than after the debounce (the checks use it).
    internal Task RenderNow()
    {
        debounce.Stop();
        Render();
        return Rendering;
    }

    private void Render()
    {
        if (!ready || !panel.SideBySide) return;
        var work = panel.Preview();
        var id = ++generation;
        if (work is null) { result = null; ShowPreview(); return; }
        var source = panel.Source!;
        Rendering = RenderAsync(work, id, source);
    }

    private async Task RenderAsync(Func<ImageMagick.MagickImage> work, long id, EditorSource source)
    {
        try
        {
            // One render at a time: the panels share the preview copy, and a newer change makes this one moot.
            await gate.WaitAsync(lifetime.Token);
            (byte[] Pixels, int Width, int Height) pixels;
            try
            {
                if (id != generation) return;
                pixels = await Task.Run(() => { using var image = work(); return ImageCanvas.Pixels(image); }, lifetime.Token);
            }
            finally { gate.Release(); }
            if (id != generation) return;
            var old = result;
            result = ImageCanvas.Bitmap(pixels);
            resultSize = new Size(pixels.Width * source.Scale, pixels.Height * source.Scale);
            ShowPreview();
            if (old is not null && !ReferenceEquals(old, original)) old.Dispose();
            panel.Say(null);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (id == generation) { panel.Say(L10n.T(error.Message)); }
        catch (Exception) { } // a later change replaced this preview
    }

    private void ShowPreview()
    {
        if (!ready || panel.Source is not { } source) return;
        var showResult = result is not null && (!panel.ComparesResult || compare.Value == Copy.Result);
        canvas.Editing = panel.Interactive && (!showResult || resultSize == source.Size);
        if (showResult) canvas.Show(result, resultSize);
        else canvas.Show(original, source.Size);
        var size = showResult ? resultSize : source.Size;
        dimensions.Text = $"{Math.Round(size.Width):0} × {Math.Round(size.Height):0}";
    }

    private static class Picker
    {
        public const string Title = "Choose an image";
        public const string Images = "Images";
    }

    private async Task<string?> PickImageAsync()
    {
        if (!StorageProvider.CanOpen) return null;
        var picked = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = L10n.T(Picker.Title),
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType(L10n.T(Picker.Images)) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.heic", "*.avif", "*.tiff", "*.tif", "*.bmp", "*.gif"] }],
        });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    private void Save()
    {
        if (!ready || !panel.CanSave) return;
        if (panel.Validate() is { } refused) { panel.Say(refused); return; }
        Job = panel.Job();
        Close();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var focused = FocusManager?.GetFocusedElement();
        var typing = focused is TextBox;
        var multiline = focused is TextBox { AcceptsReturn: true };
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if ((e.Key is Key.Enter or Key.Return && e.KeyModifiers == KeyModifiers.None || e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control) && !multiline) { Save(); e.Handled = true; }
        else if (e.Key is Key.Delete or Key.Back && !typing && panel.Delete()) e.Handled = true;
    }
}
