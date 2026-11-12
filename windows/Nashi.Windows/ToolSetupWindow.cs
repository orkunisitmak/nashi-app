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
    private readonly TextBlock status = Label("Download the missing tools to enable all conversions, previews and web downloads. Tools already found on your computer are kept.", 12, "Muted");
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new(0, 8, 0, 4) };
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
        var footer = new StackPanel { Margin = new(24, 4, 24, 20) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var actions = new DockPanel { Margin = new(0, 12, 0, 0) }; footer.Children.Add(actions);
        next = MainWindow.Primary("Download missing tools", async () => { if (Status.Ready) Close(); else await StartSetupAsync(); }); next.IsDefault = true; next.Margin = new(0);
        AutomationProperties.SetAutomationId(next, "SetupContinue"); DockPanel.SetDock(next, Dock.Right); actions.Children.Add(next);
        var later = MainWindow.Button("Later", Close); later.Style = Ui.Style("Plain"); later.Margin = new(0, 0, 8, 0); DockPanel.SetDock(later, Dock.Right); actions.Children.Add(later);
        cancel = MainWindow.Button("Cancel setup", () => cancellation?.Cancel()); cancel.Margin = new(0); cancel.HorizontalAlignment = HorizontalAlignment.Left; AutomationProperties.SetAutomationId(cancel, "SetupCancel"); actions.Children.Add(cancel);
        var content = new StackPanel { Margin = new(24, 10, 24, 8) }; root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        // The app icon beside a plain title and what the download is for.
        var hero = new DockPanel { Margin = new(2, 0, 2, 18) }; var icon = new Image { Source = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/NashiIcon.png")), Width = 60, Height = 60, Margin = new(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top }; DockPanel.SetDock(icon, Dock.Left); hero.Children.Add(icon);
        var intro = new StackPanel(); intro.Children.Add(Label("Set Up Tools", 24)); intro.Children.Add(Label("Some conversions use free, open-source tools. Download them once. Tools already on your computer are kept.", 13, "Muted")); hero.Children.Add(intro);
        content.Children.Add(hero); var box = new Border { Child = groups, CornerRadius = new(16), Padding = new(16, 2, 16, 2), BorderThickness = new(1) }; box.SetResourceReference(Border.BackgroundProperty, "Well"); box.SetResourceReference(Border.BorderBrushProperty, "Line"); content.Children.Add(box);
        var licenses = MainWindow.Button("Versions and Licenses", () => MainWindow.OpenUrl("https://github.com/orkunisitmak/nashi-app/blob/main/docs/dependencies.md")); licenses.Style = Ui.Style("Plain"); licenses.FontSize = 12; licenses.MinHeight = 26; licenses.Margin = new(0); licenses.VerticalAlignment = VerticalAlignment.Center;
        var state = new DockPanel { Margin = new(4, 16, 4, 0) }; DockPanel.SetDock(licenses, Dock.Right); state.Children.Add(licenses); state.Children.Add(status); content.Children.Add(state); content.Children.Add(progress);
        Closing += (_, e) => { if (Working) { e.Cancel = true; cancellation?.Cancel(); } };
        Activated += (_, _) => { if (!Working) Refresh(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { if (Working) cancellation?.Cancel(); else Close(); e.Handled = true; } };
        Refresh();
    }
    private static TextBlock Label(string text, double size = 13, string brush = "Ink")
    {
        var label = Ui.Text(text, size, brush, size >= 20 ? FontWeights.Bold : FontWeights.Normal); label.Margin = new(0, 0, 0, size >= 20 ? 5 : 0); return label;
    }
    private void Refresh()
    {
        var ready = Status; groups.Children.Clear();
        Group("Media tools", "FFmpeg, ffprobe, yt-dlp and Deno · about 180 MB", "wave", ready.MediaReady, ToolGroup.Media);
        Group("Office tools", "LibreOffice 26.8.1 · about 360 MB", "doc", ready.OfficeReady, ToolGroup.Office);
        L10n.Bind(next, ContentControl.ContentProperty, ready.Ready ? "Continue to Nashi" : "Download missing tools"); next.IsEnabled = !Working;
        cancel.Visibility = Working ? Visibility.Visible : Visibility.Collapsed; progress.Visibility = Working ? Visibility.Visible : Visibility.Collapsed;
        if (ready.Ready && !Working) L10n.SetText(status, "Ready. You can go back to your files.");
        else if(previouslyReady&&!Working)L10n.SetText(status,"Download the missing tools to enable all conversions, previews and web downloads. Tools already found on your computer are kept.");
        previouslyReady=ready.Ready;
    }
    // One row per tool group: icon, name and size, then its state or a download button.
    private void Group(string title, string detail, string glyph, bool ready, ToolGroup group)
    {
        if (groups.Children.Count > 0) { var line = new Border { Height = 1 }; line.SetResourceReference(Border.BackgroundProperty, "Line"); groups.Children.Add(line); }
        var row = new DockPanel { MinHeight = 58, Margin = new(0, 9, 0, 9) }; var icon = Ui.TileIcon(glyph); icon.Margin = new(0, 0, 12, 0); DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
        FrameworkElement state;
        if (ready) { var done = new StackPanel { Orientation = Orientation.Horizontal }; done.Children.Add(Icons.Make("check", 13, "Muted")); var text = Label("Already available on your computer", 12, "Muted"); text.Margin = new(6, 0, 0, 0); done.Children.Add(text); state = done; }
        else
        {
            var download = MainWindow.Button(group == ToolGroup.Media ? "Download media tools" : "Download office tools", async () => await StartSetupAsync(group));
            download.IsEnabled = !Working; download.MinHeight = 28; download.FontSize = 12; download.Margin = new(0); Ui.SetIcon(download, "download", 14); AutomationProperties.SetAutomationId(download, group == ToolGroup.Media ? "SetupMedia" : "SetupOffice"); state = download;
        }
        state.VerticalAlignment = VerticalAlignment.Center; state.Margin = new(12, 0, 0, 0); DockPanel.SetDock(state, Dock.Right); row.Children.Add(state);
        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var name = Label(title, 13); name.FontWeight = FontWeights.SemiBold; meta.Children.Add(name); var sub = Label(detail, 11.5, "Muted"); sub.Margin = new(0, 2, 0, 0); meta.Children.Add(sub); row.Children.Add(meta); groups.Children.Add(row);
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
