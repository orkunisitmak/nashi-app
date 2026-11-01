using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using ImageMagick;
using Nashi.Core;
using Path = System.IO.Path;

namespace Nashi.Linux;

/// A borderless, transparent window that holds one wheel over the main window, with up to two of the files as
/// cards beside it, or bare at the pointer. It opens with a short scale and fade (unless motion is reduced), and
/// closes on Esc, on a click outside the petals, when it loses focus, and after a choice.
/// At the pointer (ShowAt) it also grabs input on X11 and closes when abandoned.
public sealed partial class WheelWindow : Window
{
    /// The room one file card takes beside the wheel.
    public const double CardColumn = 150;

    public WheelWindow() : this("", []) { }

    public WheelWindow(string title, IReadOnlyList<WheelItem> items, IReadOnlyList<string>? cards = null)
    {
        InitializeComponent();
        Ring.Title = title;
        Ring.Items = items;
        void Describe()
        {
            DescriptionText.Text = Ring.Description;
            DescriptionCard.IsVisible = Ring.Description.Length > 0;
        }
        Ring.DescriptionChanged += Describe;
        Describe();
        Ring.Chosen += _ => Close();
        Ring.Dismissed += Close;
        Opened += (_, _) => Ring.Focus();
        Deactivated += (_, _) => { if (IsVisible) Close(); };

        var shown = (cards ?? []).Take(2).ToArray();
        var loads = new List<Task>();
        // Room around the ring for a group's outer ring.
        Height = Width = Wheel.Size + 2 * Wheel.SubRingSpace;
        if (shown.Length > 0) Width = Wheel.Size + 2 * CardColumn;
        // The outer ring reaches past the wheel control: the pointer there is passed on to it.
        AddHandler(PointerMovedEvent, (_, e) => { if (Ring.OpenGroupIndex >= 0 && !Ring.IsPointerOver) Ring.HighlightAt(e.GetPosition(Ring)); }, RoutingStrategies.Tunnel);
        if (shown.Length > 0) LeftCard.Content = FileCard(shown[0], loads);
        if (shown.Length > 1) RightCard.Content = FileCard(shown[1], loads);
        Thumbnails = Task.WhenAll(loads);

        if (!WheelFeedback.MotionEnabled) return;
        // playground.css wheel-in, at the macOS length: from 94 % and transparent to full size, ease-out.
        Root.Opacity = 0;
        Root.RenderTransform = TransformOperations.Parse("scale(0.94)");
        Root.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = WheelFeedback.Opening, Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = WheelFeedback.Opening, Easing = new CubicEaseOut() },
        ];
        Opened += (_, _) =>
        {
            Root.Opacity = 1;
            Root.RenderTransform = TransformOperations.Parse("scale(1)");
        };
    }

    public Wheel Wheel => Ring;

    /// Completes when the cards' thumbnails are decoded (or replaced by type icons).
    public Task Thumbnails { get; }

    // The playground's selected file (.demo-file): a thumbnail with a white border, or a sheet with the type icon,
    // above the name in a pill.
    private Control FileCard(string path, List<Task> loads)
    {
        var holder = new Panel { Width = 94, Height = 85 };
        holder.Children.Add(Sheet(path));
        if (FeatureCatalog.IsImage(path)) loads.Add(LoadThumbnail(holder, path));
        var name = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = Path.GetFileName(path), FontSize = 11, MaxWidth = 113, TextTrimming = TextTrimming.CharacterEllipsis },
        };
        name.Bind(Border.BackgroundProperty, this.GetResourceObservable("NashiChip"));
        var card = new Border
        {
            Width = 126,
            Padding = new Thickness(9),
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Spacing = 9, Children = { holder, name } },
        };
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("NashiWheelPetal"));
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("NashiBorder"));
        return card;
    }

    private Control Sheet(string path)
    {
        var icon = new Avalonia.Controls.Shapes.Path { Data = WheelIcons.ForFile(path), StrokeThickness = WheelIcons.Stroke, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
        var label = new TextBlock { Text = FeatureCatalog.Extension(path).ToUpperInvariant(), FontSize = 11, FontWeight = Avalonia.Media.FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        icon.Bind(Shape.StrokeProperty, this.GetResourceObservable("NashiTextSecondary"));
        label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("NashiTextSecondary"));
        var sheet = new Border
        {
            Width = 67,
            Height = 83,
            CornerRadius = new CornerRadius(4, 14, 4, 4),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { new Viewbox { Width = 26, Height = 26, Child = new Canvas { Width = 24, Height = 24, Children = { icon } } }, label },
            },
        };
        sheet.Bind(Border.BackgroundProperty, this.GetResourceObservable("NashiSheet"));
        sheet.Bind(Border.BorderBrushProperty, this.GetResourceObservable("NashiBorder"));
        return sheet;
    }

    // Decoded off the UI thread; a file that will not decode keeps its sheet.
    private static async Task LoadThumbnail(Panel holder, string path)
    {
        byte[] png;
        try
        {
            png = await Task.Run(() =>
            {
                using var image = PhotoCodec.Read(path);
                image.AutoOrient();
                image.Thumbnail(new MagickGeometry(172, 128)); // 2× the 86×64 picture inside the white border
                return image.ToByteArray(MagickFormat.Png);
            });
        }
        catch (Exception error) when (error is MagickException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException) { return; }
        using var stream = new MemoryStream(png);
        holder.Children.Clear();
        holder.Children.Add(new Border
        {
            Width = 94,
            Height = 72,
            Background = Brushes.White,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(5),
            BoxShadow = BoxShadows.Parse("0 8 8 0 #26000000"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Image { Source = new Bitmap(stream), Stretch = Stretch.UniformToFill },
        });
    }

    private Action? releaseGrab;
    private DispatcherTimer? idle;
    private DateTime lastTouch;
    private bool entered;

    /// A pointer wheel that sees no pointer or key for this long closes (it may never have got focus).
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    /// The time source for IdleTimeout; the checks swap in their own.
    public Func<DateTime> Clock { get; init; } = () => DateTime.UtcNow;

    /// Closes a pointer wheel nobody touched for IdleTimeout (polled every second).
    public void CloseIfIdle()
    {
        if (idle is not null && IsVisible && Clock() - lastTouch >= IdleTimeout) Close();
    }

    /// Without an owner: centered on the pointer (or on the primary screen), kept on screen, topmost, activated as a
    /// pager would with the launcher's `timestamp`, and grabbed like a popup menu on X11 so keys always reach it.
    /// Without a grab it also closes on pointer leave (after an enter) and after IdleTimeout.
    public void ShowAt(PixelPoint? pointer, uint timestamp = 0)
    {
        Place(pointer);
        Opened += (_, _) => Engage(timestamp);
        Show();
    }

    /// During a drag (the drag gesture): at the pointer and topmost, but not activated or grabbed, since the drag's
    /// source holds the pointer. The petals come with the first drag over it (DropTarget); after a drop that stays
    /// open (on a side or page petal) Engage makes it an ordinary pointer wheel.
    public void ShowForDrop(PixelPoint pointer)
    {
        Place(pointer);
        ShowActivated = false;
        Show();
    }

    /// The window's place on screen in physical pixels, and its scaling.
    public (PixelRect Rect, double Scaling) ScreenRect()
    {
        var scaling = Desktop.ScreenAt(this, Position)?.Scaling ?? 1;
        return (new PixelRect(Position, PixelSize.FromSize(new Size(Width, Height), scaling)), scaling);
    }

    private void Place(PixelPoint? pointer)
    {
        if (Desktop.ScreenAt(this, pointer) is { } screen)
        {
            var area = screen.WorkingArea;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = Desktop.Place(pointer ?? area.Center, PixelSize.FromSize(new Size(Width, Height), screen.Scaling), area);
        }
        Topmost = true;
    }

    private bool engaged;

    /// Activates and grabs the shown pointer wheel, with its idle and leave rules.
    public void Engage(uint timestamp = 0)
    {
        if (engaged) return;
        engaged = true;
        Activate();
        Desktop.Activate(this, timestamp);
        TryGrab(5);
        Closed += (_, _) =>
        {
            try { releaseGrab?.Invoke(); }
            finally { releaseGrab = null; idle?.Stop(); }
        };
        lastTouch = Clock();
        idle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        idle.Tick += (_, _) => CloseIfIdle();
        idle.Start();
        void Touch() => lastTouch = Clock();
        AddHandler(PointerMovedEvent, (_, _) => Touch(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, _) => Touch(), RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerEntered += (_, _) => entered = true;
        PointerExited += (_, _) => { if (entered && releaseGrab is null && !IsActive) Close(); };
    }

    /// A point relative to this window in physical pixels (what XDND positions become) in the wheel's coordinates.
    public Point ToWheel(int x, int y)
    {
        var (_, scaling) = ScreenRect();
        return this.TranslatePoint(new Point(x / scaling, y / scaling), Ring) ?? default;
    }

    private Func<Point, bool> petals = _ => false;
    private int dwell;

    /// Makes this window an XDND drop target (XdndTarget.Create): a drag passing over highlights the petal under it
    /// (and, resting on a side or page petal, turns it), and a drop on a petal goes to `dropped` with the point.
    /// Points off the petals refuse the drop, so it goes back to the source unchanged. `entered` gets the content
    /// once it is known; it fills the petals.
    public IDisposable DropTarget(Action<DropContent> entered, Action<DropContent, Point> dropped, Action? left = null, Action? refused = null)
    {
        enteredHandler = entered;
        void Snapshot() => petals = Ring.DropSnapshot();
        Ring.Turned += Snapshot;
        Snapshot();
        var (rect, scaling) = ScreenRect();
        // Any thread: pure geometry around the window's centre (the wheel's), on the petal snapshot; points are
        // relative to the window (XdndProtocol), so where the window manager really put it doesn't matter.
        bool Accepts(int x, int y)
        {
            var wheel = new Point(Wheel.Size / 2 + (x - rect.Width / 2.0) / scaling, Wheel.Size / 2 + (y - rect.Height / 2.0) / scaling);
            return petals(wheel);
        }
        var xid = TryGetPlatformHandle() is { HandleDescriptor: "XID", Handle: var handle } ? handle : IntPtr.Zero;
        return XdndTarget.Create(xid, rect, Accepts,
            (content, x, y) => Dispatcher.UIThread.Post(() =>
            {
                if (!IsVisible) return;
                Know(content);
                Hover(ToWheel(x, y));
            }),
            () => Dispatcher.UIThread.Post(() => { dwell++; Ring.HighlightedIndex = -1; left?.Invoke(); }),
            (content, x, y) => Dispatcher.UIThread.Post(() =>
            {
                dwell++;
                Know(content);
                dropped(content, ToWheel(x, y));
            }),
            () => Dispatcher.UIThread.Post(() => refused?.Invoke()));
    }

    private Action<DropContent>? enteredHandler;

    /// Whether the dragged content is known (and the petals filled).
    public bool Known { get; private set; }

    /// The dragged content, once: from the drag source when the gesture fires, else from the first XDND position.
    public void Know(DropContent content)
    {
        if (Known || !content.Any || enteredHandler is null) return;
        Known = true;
        enteredHandler(content);
    }

    /// A drag over the wheel: highlights the petal; resting 0.7 s on a side or page petal turns the wheel.
    public void Hover(Point at)
    {
        var before = Ring.HighlightedIndex;
        Ring.HighlightAt(at);
        var index = Ring.HighlightedIndex;
        if (index == before) return;
        var mine = ++dwell;
        if (index >= 0 && Ring.Segments[index].Id is var id && (id.StartsWith("side:", StringComparison.Ordinal) || id.StartsWith("page:", StringComparison.Ordinal)))
            Desktop.After(DwellTime, () => { if (mine == dwell && IsVisible) Ring.ChooseAt(at); });
    }

    /// How long a drag rests on a side or page petal before it turns.
    public static TimeSpan DwellTime { get; set; } = TimeSpan.FromMilliseconds(700);

    // The grab fails while another client holds one (AlreadyGrabbed) or before the map lands (NotViewable): retry over ~200 ms.
    private void TryGrab(int attempts)
    {
        if (!IsVisible) return;
        releaseGrab = Desktop.Grab(this);
        if (releaseGrab is null && attempts > 1) DispatcherTimer.RunOnce(() => TryGrab(attempts - 1), TimeSpan.FromMilliseconds(40));
    }

    /// A click beside the wheel (on a card or the clear space around it) closes it, like one outside the petals.
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!e.Handled && e.InitialPressMouseButton == MouseButton.Left && Ring.SubAt(e.GetPosition(Ring)) is not null) { Ring.ChooseAt(e.GetPosition(Ring)); return; }
        if (!e.Handled && e.InitialPressMouseButton == MouseButton.Left) Close();
    }

    /// While grabbed, presses anywhere on screen arrive here; one outside the window closes it like a menu.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        if (p.X < 0 || p.Y < 0 || p.X > Bounds.Width || p.Y > Bounds.Height) Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
