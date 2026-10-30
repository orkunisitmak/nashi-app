using Avalonia.Controls;
using Avalonia.Interactivity;
using Nashi.Core;

namespace Nashi.Linux;

/// One card for a running job: title, progress, message and Cancel, where the success card will appear.
/// The flow closes it when the work ends (WheelFlow).
public sealed partial class ProgressToast : Window
{
    private static class Copy
    {
        public const string Starting = "A fresh file is on its way…";
        public const string Cancel = "Cancel";
        public const string Cancelling = "Cancelling…";
    }

    private readonly Action? cancel;

    public ProgressToast() : this("", null) { }

    public ProgressToast(string title, Action? cancel)
    {
        InitializeComponent();
        this.cancel = cancel;
        TitleText.Text = title;
        MessageText.Text = L10n.T(Copy.Starting);
        ActionButton.Content = L10n.T(Copy.Cancel);
        Opened += (_, _) => Desktop.PlaceAtBottom(this);
    }

    public void Update(OperationProgress progress)
    {
        MessageText.Text = L10n.T(progress.Label);
        Bar.IsIndeterminate = progress.Fraction is null;
        if (progress.Fraction is { } fraction) Bar.Value = Math.Clamp(fraction, 0, 1);
    }

    private void OnAction(object? sender, RoutedEventArgs e)
    {
        cancel?.Invoke();
        ActionButton.IsEnabled = false;
        MessageText.Text = L10n.T(Copy.Cancelling);
    }
}
