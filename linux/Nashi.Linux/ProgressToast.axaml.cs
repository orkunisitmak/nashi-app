using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Nashi.Core;

namespace Nashi.Linux;

/// One card for a running job: title, progress, message and Cancel; on success it says what was saved,
/// offers "Show in folder", and closes itself after 4 seconds.
public sealed partial class ProgressToast : Window
{
    private static class Copy
    {
        public const string Starting = "A fresh file is on its way…";
        public const string Cancel = "Cancel";
        public const string Cancelling = "Cancelling…";
        public const string SavedOne = "Saved 1 new file. Originals kept.";
        public const string SavedMany = "Saved {0} new files. Originals kept.";
        public const string ShowInFolder = "Show in folder";
    }

    private readonly Action? cancel;
    private string? folder;

    public ProgressToast() : this("", null) { }

    public ProgressToast(string title, Action? cancel)
    {
        InitializeComponent();
        this.cancel = cancel;
        TitleText.Text = title;
        MessageText.Text = Copy.Starting;
        ActionButton.Content = Copy.Cancel;
        Opened += (_, _) => PlaceOverOwner();
    }

    public void Update(OperationProgress progress)
    {
        if (folder is not null) return;
        MessageText.Text = progress.Label;
        Bar.IsIndeterminate = progress.Fraction is null;
        if (progress.Fraction is { } fraction) Bar.Value = Math.Clamp(fraction, 0, 1);
    }

    public void Complete(IReadOnlyList<string> outputs, string destination)
    {
        folder = destination;
        TitleText.Text = outputs.Count == 1 ? Copy.SavedOne : string.Format(Copy.SavedMany, outputs.Count);
        MessageText.Text = outputs.Count > 0 ? Path.GetFileName(outputs[0]) : destination;
        Bar.IsVisible = false;
        ActionButton.Content = Copy.ShowInFolder;
        ActionButton.IsEnabled = true;
        DispatcherTimer.RunOnce(Close, TimeSpan.FromSeconds(4));
    }

    private void OnAction(object? sender, RoutedEventArgs e)
    {
        if (folder is null)
        {
            cancel?.Invoke();
            ActionButton.IsEnabled = false;
            MessageText.Text = Copy.Cancelling;
            return;
        }
        try { Process.Start(new ProcessStartInfo("xdg-open", [folder]) { UseShellExecute = false })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { } // no xdg-open: nothing to show the folder with
        Close();
    }

    // Bottom-center over the main window, like the macOS toast over the screen.
    private void PlaceOverOwner()
    {
        if (Owner is not Window owner) return;
        var scale = owner.RenderScaling;
        var x = owner.Position.X + (int)((owner.Bounds.Width - Bounds.Width) / 2 * scale);
        var y = owner.Position.Y + (int)((owner.Bounds.Height - Bounds.Height - 28) * scale);
        Position = new PixelPoint(Math.Max(0, x), Math.Max(0, y));
    }
}
