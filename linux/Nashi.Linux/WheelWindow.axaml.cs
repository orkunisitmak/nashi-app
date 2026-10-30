using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Nashi.Linux;

/// A borderless, transparent window that holds one wheel over the main window.
/// It closes on Esc, on a click outside the petals, when it loses focus, and after a choice.
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

    /// Without an owner: centered on the pointer (or on the primary screen), kept on screen, topmost and focused.
    public void ShowAt(PixelPoint? pointer)
    {
        if (Desktop.ScreenAt(this, pointer) is { } screen)
        {
            var area = screen.WorkingArea;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = Desktop.Place(pointer ?? area.Center, PixelSize.FromSize(new Size(Width, Height), screen.Scaling), area);
        }
        Topmost = true;
        Opened += (_, _) => Activate();
        Show();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
