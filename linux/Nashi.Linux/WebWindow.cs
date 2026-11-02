using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nashi.Core;

namespace Nashi.Linux;

public sealed class WebWindow : Window
{
    internal TextBox Link { get; } = new() { Watermark = "Paste a public media or image URL" };
    private readonly ComboBox kind = new() { ItemsSource = new[] { "video", "audio", "image", "photos", "thumbnail" }, SelectedIndex = 0 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox destination = new() { IsReadOnly = true, Text = BrowserDrop.DownloadFolder() };
    private readonly Button download = new() { Content = "Download", Classes = { "primary" } };
    private readonly Button use = new() { Content = "Use downloaded files", IsEnabled = false };
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? current;
    private IReadOnlyList<string> saved = [];
    public bool Busy => current is not null;
    internal Func<string, string, string, IProgress<OperationProgress>, CancellationToken, Task<IReadOnlyList<string>>> Fetch { get; set; }

    public WebWindow(Toolchain tools, Action<IReadOnlyList<string>> reuse, string? url = null)
    {
        Fetch = (link, kind, folder, progress, token) => new WebDownload(tools).DownloadAsync(link, kind, folder, progress, token);
        Title = "Download a link · Nashi"; Width = 610; Height = 480; MinWidth = 500; MinHeight = 420;
        Desktop.Translucent(this); Link.Text = url;
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "Download a link", FontSize = 24, FontWeight = FontWeight.Bold });
        body.Children.Add(new TextBlock { Text = "Video, audio, direct images, Instagram photos or a thumbnail. Choose what to save.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(Link); body.Children.Add(kind); body.Children.Add(destination);
        var browse = new Button { Content = "Choose download folder…" };
        browse.Click += async (_, _) => { try { if (await WheelFlow.PickLocalFolderAsync(this, "Download folder") is { } folder) destination.Text = folder; } catch (Exception error) { status.Text = error.Message; } };
        body.Children.Add(browse);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var cancel = new Button { Content = "Cancel download", IsEnabled = false };
        cancel.Click += (_, _) => current?.Cancel(); actions.Children.Add(download); actions.Children.Add(cancel); body.Children.Add(actions);
        body.Children.Add(status); body.Children.Add(use); Content = new ScrollViewer { Content = body };
        use.Click += (_, _) => { reuse(saved); Close(); };
        download.Click += async (_, _) =>
        {
            if (current is not null) return;
            current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            download.IsEnabled = use.IsEnabled = Link.IsEnabled = kind.IsEnabled = browse.IsEnabled = false;
            cancel.IsEnabled = true; saved = []; status.Text = "Preparing download…";
            try
            {
                saved = await Fetch(Link.Text ?? "", (string)kind.SelectedItem!, destination.Text!, new Progress<OperationProgress>(p => status.Text = p.Label), current.Token);
                History.Record("Download", saved); status.Text = $"Saved {saved.Count} file(s) to {destination.Text}"; use.IsEnabled = saved.Count > 0;
            }
            catch (OperationCanceledException) { status.Text = "Download cancelled."; }
            catch (Exception error) { status.Text = error is FileNotFoundException ? "A download tool is missing. Open Set up tools on Home, then try again." : error.Message; }
            finally
            {
                current.Dispose(); current = null;
                download.IsEnabled = Link.IsEnabled = kind.IsEnabled = browse.IsEnabled = true; cancel.IsEnabled = false;
            }
        };
        Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
    }
}
