using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nashi.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nashi.Linux;

/// <summary>
/// "Download a link" page: paste a public link, find what it holds, pick a kind and quality, download.
/// </summary>
internal sealed class WebPanel : StackPanel
{
    /// <summary>
    /// Called by MainWindow to run the download with its shared progress bar, cancel button and Library.
    /// Arguments: title for the Library ("Download video"), and the work to run (destination folder is chosen by MainWindow).
    /// </summary>
    public Func<string, Func<string, IProgress<OperationProgress>, CancellationToken, Task<IReadOnlyList<string>>>, Task>? Run { get; set; }

    public TextBox Link { get; }
    public ComboBox Kind { get; }
    public ComboBox Quality { get; }
    public TextBlock Summary { get; }
    public StackPanel Photos { get; }

    private CancellationTokenSource _inspectionCts = new();
    private WebMediaPayload? _payload;
    private HashSet<int> _selectedPhotos = new();
    private string? _inspected;
    /// Progress and results from MainWindow's shared runner.
    public string Status { set => Summary.Text = value; }

    public WebPanel()
    {
        Spacing = 10;
        Margin = new Thickness(20);

        // Title
        var title = new TextBlock
        {
            Text = "Download from the web",
            FontWeight = FontWeight.Bold,
            FontSize = 26,
        };
        Children.Add(title);

        // Subtitle
        var subtitle = new TextBlock
        {
            Text = "A link. A file. Yours to work with.",
            Opacity = 0.7,
            FontSize = 12,
        };
        Children.Add(subtitle);

        // Link row
        var linkRow = new DockPanel { LastChildFill = true };
        Link = new TextBox
        {
            PlaceholderText = "Paste a public video, audio or image link",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Kind = new ComboBox { MinWidth = 130 };
        DockPanel.SetDock(Kind, Dock.Right);
        // A DockPanel fills with its last child, so the docked Kind goes first.
        Kind.Margin = new Thickness(8, 0, 0, 0);
        linkRow.Children.Add(Kind);
        linkRow.Children.Add(Link);
        Children.Add(linkRow);

        // Button row
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var pasteBtn = new Button { Content = "Paste link" };
        pasteBtn.Click += async (_, __) => await PasteLink();
        var findBtn = new Button { Content = "Find media" };
        findBtn.Click += async (_, __) => await FindAsync();
        var downloadBtn = new Button { Content = "Download" };
        downloadBtn.Click += async (_, __) => await DownloadAsync();
        downloadBtn.Classes.Add("primary");pasteBtn.Classes.Add("pill");findBtn.Classes.Add("pill");
        buttonRow.Children.Add(pasteBtn);
        buttonRow.Children.Add(findBtn);
        buttonRow.Children.Add(downloadBtn);
        Children.Add(buttonRow);

        // Summary
        Summary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
        };
        Children.Add(Summary);

        // Quality row
        var qualityPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        var qualityLabel = new TextBlock { Text = "Video quality", Opacity = 0.7, FontSize = 12 };
        Quality = new ComboBox { MinWidth = 160, SelectedIndex = 0, Margin = new Thickness(10, 0, 0, 0) };
        Quality.ItemsSource = new[] { "Best available", "Up to 1080p", "Up to 720p" };
        Quality.SelectedIndex = 0;
        qualityPanel.VerticalAlignment = VerticalAlignment.Center;
        qualityLabel.VerticalAlignment = VerticalAlignment.Center;
        qualityPanel.Children.Add(qualityLabel);
        qualityPanel.Children.Add(Quality);
        Children.Add(qualityPanel);
        _quality = qualityPanel;
        Kind.SelectionChanged += (_, _) => _quality.IsVisible = Kind.SelectedItem as string == "video";

        // Photos panel
        Photos = new StackPanel { Spacing = 5, Margin = new Thickness(0, 5, 0, 0) };
        Children.Add(Photos);

        // Bottom note
        var note = new TextBlock
        {
            Text = "Instagram photo posts open a photo selection. Reel links offer video and audio. Public downloadable media only.",
            Opacity = 0.7,
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0),
        };
        Children.Add(note);

        // Set up events
        Link.TextChanged += (_, _) => OnLinkTextChanged();
        SetKinds(["video", "audio", "image"]);
        Summary.Text = "Paste a public media link to begin.";
    }

    private readonly StackPanel _quality;
    private void ApplyPrimaryStyle(Button button)
    {
        button.Background = NashiTheme.Primary;
        button.Foreground = new SolidColorBrush(Color.Parse("#243B2B"));
        button.FontWeight = FontWeight.SemiBold;
    }

    private async Task PasteLink()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is { } clipboard)
        {
            var txt = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
            if (!string.IsNullOrWhiteSpace(txt))
                Link.Text = txt;
        }
    }

    private void OnLinkTextChanged()
    {
        _inspectionCts.Cancel();
        _inspectionCts = new CancellationTokenSource();
        Summary.Text = "Choose Find media to inspect this link.";
        _inspected = null;
        Photos.Children.Clear();
        _selectedPhotos.Clear();
        _payload = WebMediaPayload.Parse(Link.Text);
        var kindList = new List<string>();
        switch (_payload?.Kind)
        {
            case "youtube":
                kindList.AddRange(new[] { "video", "audio", "thumbnail" });
                break;
            case "instagramPhotos":
                kindList.Add("photos");
                break;
            case "instagramReel":
                kindList.AddRange(new[] { "video", "audio" });
                break;
            case "image":
                kindList.Add("image");
                break;
            case null:
                kindList.AddRange(new[] { "video", "audio", "image" });
                break;
            default:
                kindList.AddRange(new[] { "video", "audio", "image" });
                break;
        }
        SetKinds(kindList);
    }
    // Keep the chosen kind when the new link offers it too.
    private void SetKinds(IReadOnlyList<string> kinds)
    {
        var chosen = Kind.SelectedItem as string;
        Kind.ItemsSource = kinds.ToArray();
        Kind.SelectedIndex = chosen is not null && kinds.Contains(chosen) ? kinds.ToList().IndexOf(chosen) : 0;
        _quality.IsVisible = Kind.SelectedItem as string == "video";
    }

    /// Inspect the link: Instagram posts list their photos, other media report title, site and length.
    public async Task FindAsync()
    {
        var input = Link.Text ?? "";
        _payload = WebMediaPayload.Parse(input);
        if (_payload is null) { Summary.Text = "Paste one complete public media link."; return; }
        _inspectionCts.Cancel();
        var pending = new CancellationTokenSource();
        _inspectionCts = pending;
        Photos.Children.Clear();
        _selectedPhotos.Clear();
        Summary.Text = "Finding the media…";
        try
        {
            var engine = new WebDownload(Operations.Toolset);
            if (_payload.Kind == "instagramPhotos")
            {
                if (!Operations.Toolset.Ready) { Summary.Text = "Set up media tools first (Settings)."; return; }
                var photos = await engine.InspectPhotosAsync(_payload.Url, pending.Token);
                if (input != Link.Text || pending.IsCancellationRequested) return;
                Summary.Text = Localizer.Pick($"{photos.Length} photos · full-frame images · saved in post order", $"{photos.Length} fotoğraf · tam kare görseller · gönderi sırasıyla kaydedilir");
                var boxes = new List<CheckBox>();
                var all = new Button { Content = "All" }; all.Click += (_, _) => { foreach (var b in boxes) b.IsChecked = true; };
                var none = new Button { Content = "None" }; none.Click += (_, _) => { foreach (var b in boxes) b.IsChecked = false; };
                Photos.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { all, none } });
                foreach (var photo in photos)
                {
                    _selectedPhotos.Add(photo.Id);
                    var box = new CheckBox { Content = Localizer.Pick("Photo ", "Fotoğraf ") + $"{photo.Id}  ·  {photo.Width} × {photo.Height}", IsChecked = true };
                    var id = photo.Id;
                    box.IsCheckedChanged += (_, _) => { if (box.IsChecked == true) _selectedPhotos.Add(id); else _selectedPhotos.Remove(id); };
                    boxes.Add(box); Photos.Children.Add(box);
                }
            }
            else if (_payload.Kind == "image" || Kind.SelectedItem as string == "thumbnail")
                Summary.Text = _payload.Kind == "image" ? "Original image · ready to download" : "YouTube thumbnail · best available full-size image";
            else
            {
                if (!Operations.Toolset.Ready) { Summary.Text = "Set up media tools first (Settings)."; return; }
                var info = await engine.InspectAsync(_payload.Url, pending.Token);
                if (input != Link.Text || pending.IsCancellationRequested) return;
                Summary.Text = info.Title + "\n" + info.Site + (info.Duration is { } seconds ? " · " + TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss") : "")
                    + (info.Heights.Length > 0 && !info.AudioOnly ? " · up to " + info.Heights.Max() + "p" : "");
                if (info.AudioOnly) SetKinds(["audio"]);
            }
            _inspected = input;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or System.Net.Http.HttpRequestException)
        { if (!pending.IsCancellationRequested) Summary.Text = ex.Message; }
    }

    /// Download through MainWindow's runner, into the download folder from Settings.
    public async Task DownloadAsync()
    {
        if (Run is null) return;
        var input = Link.Text ?? "";
        _payload = WebMediaPayload.Parse(input);
        var kind = Kind.SelectedItem as string ?? "video";
        if (kind == "photos" && _inspected != input) { await FindAsync(); return; }
        if (!Operations.Toolset.Ready && kind != "image" && _payload?.Kind != "image" && !(kind == "thumbnail" && _payload?.Kind == "youtube"))
        { Summary.Text = "Set up media tools first (Settings)."; return; }
        string url;
        try { url = WebDownload.Validate(_payload?.Url ?? input).AbsoluteUri; }
        catch (ArgumentException ex) { Summary.Text = ex.Message; return; }
        int? height = Quality.SelectedIndex switch { 1 => 1080, 2 => 720, _ => null };
        var photos = kind == "photos" ? new HashSet<int>(_selectedPhotos) : null;
        await Run("Download " + kind, (destination, progress, token) => new WebDownload(Operations.Toolset).DownloadAsync(url, kind, destination, progress, token, kind == "video" ? height : null, photos));
    }
}
