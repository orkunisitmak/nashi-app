using Avalonia.Controls;
using Avalonia.Interactivity;
using Nashi.Core;

namespace Nashi.Linux;

/// Media tool status plus a download of whatever is missing, with progress, Cancel, error text and Retry.
public sealed partial class ToolSetupWindow : Window
{
    private static class Copy
    {
        public const string Heading = "Set up tools";
        public const string HeadingReady = "Tools ready";
        public const string Detail = "Nashi uses FFmpeg for audio and video. Downloads come straight from the upstream projects and are checked before anything is installed.";
        public const string DetailReady = "Everything Nashi needs for audio and video is in place.";
        public const string Tool = "Media tools";
        public const string ToolDetail = "FFmpeg and FFprobe";
        public const string Ready = "Ready";
        public const string Missing = "Missing";
        public const string Download = "Download missing tools";
        public const string Retry = "Retry";
        public const string Cancel = "Cancel";
        public const string Cancelling = "Cancelling…";
        public const string Close = "Close";
        public const string Starting = "Getting started…";
        public const string Cancelled = "Cancelled. Nothing was installed.";
        public const string Failed = "Couldn’t set up tools: {0}";
        public const string StillMissing = "Media tools are still missing after the download.";
    }

    private readonly Toolchain tools;
    private readonly Func<IProgress<OperationProgress>, CancellationToken, Task> install;
    private CancellationTokenSource? cancellation;
    private string? error;

    public ToolSetupWindow() : this(AppPaths.CreateToolchain()) { }

    public ToolSetupWindow(Toolchain tools, Func<IProgress<OperationProgress>, CancellationToken, Task>? install = null)
    {
        InitializeComponent();
        this.tools = tools;
        this.install = install ?? ((progress, token) => tools.InstallAsync(progress, token));
        Title = L10n.T(Copy.Heading);
        ToolName.Text = L10n.T(Copy.Tool);
        ToolDetail.Text = L10n.T(Copy.ToolDetail);
        CancelButton.Content = L10n.T(Copy.Cancel);
        CloseButton.Content = L10n.T(Copy.Close);
        Closing += (_, _) => cancellation?.Cancel();
        Refresh();
    }

    private bool Installing => cancellation is not null;

    private void Refresh()
    {
        var ready = AppPaths.MediaReady(tools);
        HeadingText.Text = ready ? L10n.T(Copy.HeadingReady) : L10n.T(Copy.Heading);
        DetailText.Text = ready ? L10n.T(Copy.DetailReady) : L10n.T(Copy.Detail);
        StatusText.Text = ready ? L10n.T(Copy.Ready) : L10n.T(Copy.Missing);
        StatusPill.Classes.Set("gold", !ready);
        ProgressPanel.IsVisible = Installing;
        CancelButton.IsVisible = Installing;
        ActionButton.IsVisible = !ready && !Installing;
        ActionButton.Content = error is null ? L10n.T(Copy.Download) : L10n.T(Copy.Retry);
        ErrorText.IsVisible = error is not null;
        ErrorText.Text = error;
    }

    private async void OnAction(object? sender, RoutedEventArgs e)
    {
        if (Installing) return;
        error = null;
        cancellation = new CancellationTokenSource();
        ProgressText.Text = L10n.T(Copy.Starting);
        CancelButton.IsEnabled = true;
        Bar.IsIndeterminate = true;
        Refresh();
        try
        {
            await install(new Progress<OperationProgress>(Update), cancellation.Token);
            if (!AppPaths.MediaReady(tools)) error = L10n.T(Copy.StillMissing);
        }
        catch (OperationCanceledException) { error = L10n.T(Copy.Cancelled); }
        catch (Exception failure) { error = L10n.F(Copy.Failed, L10n.T(failure.Message)); }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
        }
        Refresh();
    }

    private void Update(OperationProgress progress)
    {
        if (!Installing) return;
        ProgressText.Text = L10n.T(progress.Label);
        Bar.IsIndeterminate = progress.Fraction is null;
        if (progress.Fraction is { } fraction) Bar.Value = Math.Clamp(fraction, 0, 1);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        ProgressText.Text = L10n.T(Copy.Cancelling);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
