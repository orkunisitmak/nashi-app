using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nashi.Linux;

public sealed class LibraryWindow : Window
{
    private readonly StackPanel rows = new() { Spacing = 12 };
    internal TextBox SearchBox { get; } = new() { Watermark = "Search files or actions", Name = "LibrarySearch" };
    private readonly Action<IReadOnlyList<string>> reuse;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };

    public LibraryWindow(Action<IReadOnlyList<string>> reuse)
    {
        this.reuse = reuse;
        Title = "Library · Nashi"; Width = 700; Height = 620; MinWidth = 520; MinHeight = 400;
        Desktop.Translucent(this);
        var body = new DockPanel { Margin = new Thickness(24) };
        var heading = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 20) };
        heading.Children.Add(new TextBlock { Text = "Library", FontSize = 26, FontWeight = FontWeight.Bold });
        heading.Children.Add(new TextBlock { Text = "Recent results, kept in their original folders. History keeps the last 50 tasks.", TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(SearchBox); DockPanel.SetDock(heading, Dock.Top); body.Children.Add(heading);
        DockPanel.SetDock(status, Dock.Bottom); body.Children.Add(status);
        body.Children.Add(new ScrollViewer { Content = rows }); Content = body;
        SearchBox.TextChanged += (_, _) => Refresh();
        History.Changed += Refresh;
        Closed += (_, _) => History.Changed -= Refresh;
        Activated += (_, _) => Refresh();
        Refresh();
    }

    internal void Refresh()
    {
        var store = History.Open();
        rows.Children.Clear();
        if ((History.Warning ?? store.RecoveryNotice) is { } warning)
            rows.Children.Add(new TextBlock { Text = warning, TextWrapping = TextWrapping.Wrap });
        var entries = store.Search(SearchBox.Text ?? "", L10n.Language).ToArray();
        if (entries.Length == 0)
            rows.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(SearchBox.Text) ? "Your completed tasks will appear here. Choose files on Home to get started." : "No matching results.", TextWrapping = TextWrapping.Wrap });
        foreach (var entry in entries)
        {
            var available = entry.AvailablePaths;
            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(new TextBlock { Text = entry.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(new TextBlock { Text = entry.Action + " · " + entry.Created.ToLocalTime().ToString("g"), FontSize = 12 });
            content.Children.Add(new TextBlock { Text = string.Join("\n", entry.Paths), FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis });
            if (available.Length != entry.Paths.Length)
                content.Children.Add(new TextBlock { Text = $"{entry.Paths.Length - available.Length} file(s) moved or deleted outside Nashi.", FontSize = 11 });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            Button Action(string title, Action run)
            {
                var button = new Button { Content = title, IsEnabled = available.Length > 0 };
                button.Classes.Add("secondary"); button.Click += (_, _) => run(); actions.Children.Add(button); return button;
            }
            Action("Use files", () => { reuse(available); Close(); });
            Action("Open", () => { if (!Desktop.Start("xdg-open", [available[0]], null)) status.Text = "Could not open the file. Check that a default application is installed."; });
            Action("Show in folder", () => { if (!FileActions.Share(available)) status.Text = "Could not open your file manager."; });
            content.Children.Add(actions);
            var card = new Border { Padding = new Thickness(16), Child = content }; card.Classes.Add("card"); rows.Children.Add(card);
        }
    }
}
