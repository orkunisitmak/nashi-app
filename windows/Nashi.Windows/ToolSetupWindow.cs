using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Nashi.Core;
namespace Nashi.Windows;

public sealed class ToolSetupWindow : Window
{
    private readonly Func<ToolSetupStatus> probe;
    private readonly Func<ToolGroup, IProgress<OperationProgress>, CancellationToken, Task> install;
    private readonly Action<bool> workingChanged;
    private readonly StackPanel groups = new();
    private readonly TextBlock status = Label("Download or refresh the verified tools for conversions, previews and web downloads. Tools managed on your PATH are kept.");
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, Height = 6, Visibility = Visibility.Collapsed, Margin = new(0, 12, 0, 12) };
    private readonly Button next, cancel;
    private CancellationTokenSource? cancellation;
    internal bool Working { get; private set; }
    private bool previouslyReady;
    internal ToolSetupStatus Status => probe();
    internal Button ContinueButton => next;
    public ToolSetupWindow(Window owner, Toolchain media, OfficeRuntime office, Action<bool> workingChanged)
        : this(owner, () => new(media.Ready, office.Executable is not null),
            (group, report, token) => Task.Run(() => group == ToolGroup.Media ? media.InstallAsync(report, token) : office.InstallAsync(report, token),token), workingChanged) { }
    internal ToolSetupWindow(Window owner, Func<ToolSetupStatus> probe,
        Func<ToolGroup, IProgress<OperationProgress>, CancellationToken, Task> install, Action<bool>? workingChanged = null)
    {
        this.probe = probe; this.install = install; this.workingChanged = workingChanged ?? (_ => { });
        Owner = owner; Theme.Attach(this); Title = "Set up Nashi tools"; Width = 646; Height = 690; MinWidth = 590; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L10n.Bind(this,TitleProperty,"Set up Nashi tools");
        var root = new DockPanel(); Content = root;
        var footer = new StackPanel { Margin = new(24) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(status); footer.Children.Add(progress);
        var actions = new DockPanel { Margin = new(0, 14, 0, 0) }; footer.Children.Add(actions);
        next = MainWindow.Primary("Download or update tools", async () => { if (Status.Ready) Close(); else await StartSetupAsync(); }); next.IsDefault = true;
        AutomationProperties.SetAutomationId(next, "SetupContinue"); DockPanel.SetDock(next, Dock.Right); actions.Children.Add(next);
        cancel = MainWindow.Button("Cancel setup", () => cancellation?.Cancel()); AutomationProperties.SetAutomationId(cancel, "SetupCancel"); actions.Children.Add(cancel);
        var content = new StackPanel { Margin = new(28) }; root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        content.Children.Add(Label("FIRST-TIME SETUP", 11)); content.Children.Add(Label("A few tools. More possibilities.", 28));
        content.Children.Add(Label("One download per tool group. No account or GitHub token.", 14)); content.Children.Add(groups);
        content.Children.Add(MainWindow.Button("Upstream versions, licenses and source links", () => MainWindow.OpenUrl("https://github.com/orkunisitmak/nashi-app/blob/main/docs/dependencies.md")));
        Closing += (_, e) => { if (Working) { e.Cancel = true; cancellation?.Cancel(); } };
        Activated += (_, _) => { if (!Working) Refresh(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { if (Working) cancellation?.Cancel(); else Close(); e.Handled = true; } };
        Refresh();
    }
    private static TextBlock Label(string text, double size = 13) => L10n.Label(new TextBlock { Text = text, FontSize = size, FontWeight = size >= 18 ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 8) });
    private void Refresh()
    {
        var ready = Status; groups.Children.Clear();
        Group("Media tools", "FFmpeg, ffprobe, yt-dlp and Deno enable media conversion, previews and web downloads. Approximately 180 MB. FFmpeg and yt-dlp use GPL licenses; Deno uses MIT.", ready.MediaReady, ToolGroup.Media);
        Group("Office tools", "LibreOffice 26.8.1 enables Office document conversions. Approximately 360 MB to download. MPL 2.0 and its included third-party licenses.", ready.OfficeReady, ToolGroup.Office);
        L10n.Bind(next, ContentControl.ContentProperty, ready.Ready ? "Continue to Nashi" : "Download or update tools"); next.IsEnabled = !Working;
        cancel.Visibility = Working ? Visibility.Visible : Visibility.Collapsed; progress.Visibility = Working ? Visibility.Visible : Visibility.Collapsed;
        if (ready.Ready && !Working) L10n.SetText(status, "Ready. You can go back to your files.");
        else if(previouslyReady&&!Working)L10n.SetText(status,"Download or refresh the verified tools for conversions, previews and web downloads. Tools managed on your PATH are kept.");
        previouslyReady=ready.Ready;
    }
    private void Group(string title, string detail, bool ready, ToolGroup group)
    {
        var row = new StackPanel { Margin = new(14) }; row.Children.Add(Label(title, 18)); row.Children.Add(Label(detail));
        if (ready) row.Children.Add(Label("Already available on your computer"));
        else
        {
            var download = MainWindow.Button(group == ToolGroup.Media ? "Download media tools" : "Download office tools", async () => await StartSetupAsync(group));
            download.IsEnabled = !Working; AutomationProperties.SetAutomationId(download, group == ToolGroup.Media ? "SetupMedia" : "SetupOffice"); row.Children.Add(download);
        }
        var card = new Border { Child = row, CornerRadius = new(16), BorderThickness = new(1), Margin = new(0, 12, 0, 8) };
        card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Line"); groups.Children.Add(card);
    }
    internal async Task StartSetupAsync(ToolGroup? selected = null)
    {
        if (Working) return;
        var pending = Status.Missing.Where(g => selected is null || g == selected).ToArray(); if (pending.Length == 0) { Refresh(); return; }
        Working = true; cancellation = new(); workingChanged(true); Refresh(); progress.IsIndeterminate = true;
        var token = cancellation.Token;
        try
        {
            for (var i = 0; i < pending.Length; i++)
            {
                token.ThrowIfCancellationRequested(); var index = i;
                var report = new Progress<OperationProgress>(p => { if (!Working || token.IsCancellationRequested) return; progress.IsIndeterminate = p.Fraction is null; if (p.Fraction is {} value) progress.Value = (index + value) / pending.Length; L10n.SetText(status, p.Label); });
                await install(pending[i], report, token); Refresh();
            }
            if (Status.Missing.Any(pending.Contains)) throw new InvalidDataException("The downloaded tools are incomplete. Please try setup again.");
            L10n.SetText(status, Status.Ready ? "Ready. You can go back to your files." : "One tool group is ready. Download the remaining tools to finish setup.");
        }
        catch (OperationCanceledException) { L10n.SetText(status, "Setup cancelled. Existing tools were kept."); }
        catch (Exception error) { L10n.SetText(status, error.Message); }
        finally { Working = false; cancellation.Dispose(); cancellation = null; workingChanged(false); Refresh(); }
    }
}
