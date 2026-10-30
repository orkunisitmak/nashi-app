using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Nashi.Linux;

/// A borderless, transparent window that holds one wheel over the main window.
/// It closes on Esc, on a click outside the petals, when it loses focus, and after a choice.
/// At the pointer (ShowAt) it also grabs input on X11 and closes when abandoned.
public sealed partial class WheelWindow : Window
{
    public WheelWindow() : this("", []) { }

    public WheelWindow(string title, IReadOnlyList<WheelItem> items)
    {
        InitializeComponent();
        Ring.Title = title;
        Ring.Items = items;
        Ring.Chosen += _ => Close();
        Ring.Dismissed += Close;
        Opened += (_, _) => Ring.Focus();
        Deactivated += (_, _) => { if (IsVisible) Close(); };
    }

    public Wheel Wheel => Ring;

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
        if (Desktop.ScreenAt(this, pointer) is { } screen)
        {
            var area = screen.WorkingArea;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = Desktop.Place(pointer ?? area.Center, PixelSize.FromSize(new Size(Width, Height), screen.Scaling), area);
        }
        Topmost = true;
        Opened += (_, _) =>
        {
            Activate();
            Desktop.Activate(this, timestamp);
            TryGrab(5);
        };
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
        Show();
    }

    // The grab fails while another client holds one (AlreadyGrabbed) or before the map lands (NotViewable): retry over ~200 ms.
    private void TryGrab(int attempts)
    {
        if (!IsVisible) return;
        releaseGrab = Desktop.Grab(this);
        if (releaseGrab is null && attempts > 1) DispatcherTimer.RunOnce(() => TryGrab(attempts - 1), TimeSpan.FromMilliseconds(40));
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
