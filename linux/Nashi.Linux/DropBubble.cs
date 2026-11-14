using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Layout;

namespace Nashi.Linux;

/// An ordinary drop-target window, usable without global pointer hooks. Drop first, then choose on the wheel.
public sealed class DropBubble : Window
{
    public DropBubble(Action<IReadOnlyList<string>> open, Action home)
    {
        Title = "Nashi drop target"; Width = 86; Height = 86; CanResize = false;
        ShowInTaskbar = false; Topmost = true; SystemDecorations = SystemDecorations.None;
        Desktop.Translucent(this); DragDrop.SetAllowDrop(this, true);
        var body = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(new Image { Source = MainWindow.Mark, Width = 48, Height = 48 });
        body.Children.Add(new TextBlock { Text = "Drop files", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center });
        Content = body;
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var selection = new Selection();
            selection.Add(e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>() ?? []);
            if (selection.Files.Count > 0) open(selection.Files);
            e.Handled = true;
        });
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount > 1) home(); else BeginMoveDrag(e);
        };
        var menu = new ContextMenu();
        var show = new MenuItem { Header = "Open Nashi" }; show.Click += (_, _) => home();
        var hide = new MenuItem { Header = "Hide drop target" }; hide.Click += (_, _) => Settings.Update(s => s with { BubbleEnabled = false });
        menu.Items.Add(show); menu.Items.Add(hide); ContextMenu = menu;
    }
}
