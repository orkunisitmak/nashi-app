#if DEBUG
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Nashi.Windows;

internal sealed class PillStyleGallery : Window
{
    private static readonly Brush Gold = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D3B46B"));
    private static readonly Brush Secondary = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6A6AD"));

    internal PillStyleGallery()
    {
        Title = "Nashi · Pill Style Gallery";
        Width = 1180;
        Height = 790;
        MinWidth = 1000;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111114"));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI Variable");
        var root = new DockPanel { Margin = new Thickness(30) };
        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        title.Children.Add(new TextBlock { Text = "NASHI PILL · STYLE GALLERY", FontSize = 11, Foreground = Gold, FontWeight = FontWeights.SemiBold });
        title.Children.Add(new TextBlock { Text = "One shape. Quiet by default.", FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        title.Children.Add(new TextBlock { Text = "Synthetic preview · no personal files, media sessions, or camera data", FontSize = 13, Foreground = Secondary, Margin = new Thickness(0, 5, 0, 0) });
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);
        var grid = new UniformGrid { Columns = 2, Rows = 2 };
        grid.Children.Add(Sample("01 · COLLAPSED", PillGeometry.CollapsedWidth, PillGeometry.CollapsedHeight, Collapsed()));
        grid.Children.Add(Sample("02 · NOW · MEDIA SESSION", PillGeometry.ExpandedWidth, PillGeometry.ExpandedHeight, Now(true)));
        grid.Children.Add(Sample("03 · NOW · NO MEDIA", PillGeometry.ExpandedWidth, PillGeometry.ExpandedHeight, Now(false)));
        grid.Children.Add(Sample("04 · DOCK STATES", PillGeometry.ExpandedWidth, PillGeometry.ExpandedHeight, DockSample()));
        root.Children.Add(grid);
        Content = root;
    }

    internal void Capture(string path)
    {
        UpdateLayout();
        if (ActualWidth < 1 || ActualHeight < 1) throw new InvalidOperationException("Pill style gallery is not arranged yet.");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static Border Sample(string heading, double width, double height, UIElement content)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = heading, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Secondary, Margin = new Thickness(0, 0, 0, 12) });
        var shape = new Grid { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Center, Clip = PillGeometry.Create(width, height) };
        shape.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#050507"));
        shape.Children.Add(content);
        AutomationProperties.SetName(shape, heading);
        stack.Children.Add(shape);
        return new Border { Margin = new Thickness(7), Padding = new Thickness(16), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#19191D")), CornerRadius = new CornerRadius(16), Child = stack };
    }

    private static UIElement Collapsed()
    {
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 0, 12, 0) };
        var mark = new TextBlock { Text = "◉", FontSize = 16, Foreground = Gold, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(mark, "Nashi mark"); row.Children.Add(mark);
        var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var h in new[] { 5d, 11d, 7d }) status.Children.Add(new Border { Width = 3, Height = h, CornerRadius = new CornerRadius(2), Background = Gold, Margin = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(status, Dock.Right); row.Children.Add(status); return row;
    }

    private static UIElement Now(bool media)
    {
        var grid = new Grid { Margin = new Thickness(20, 16, 20, 12) };
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = media ? new GridLength(278) : new GridLength(0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = media ? new GridLength(20) : new GridLength(0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        if (media)
        {
            var player = new Grid { VerticalAlignment = VerticalAlignment.Center };
            player.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) }); player.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); player.ColumnDefinitions.Add(new ColumnDefinition());
            player.RowDefinitions.Add(new RowDefinition()); player.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            player.Children.Add(new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(12), Background = new LinearGradientBrush(Color.FromRgb(65, 40, 92), Color.FromRgb(207, 126, 82), 40), Child = new TextBlock { Text = "N", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = "Loser", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            info.Children.Add(new TextBlock { Text = "Tame Impala", FontSize = 12, Foreground = Secondary, Margin = new Thickness(0, 3, 0, 4) });
            info.Children.Add(new ProgressBar { Value = 48, Maximum = 100, Height = 3, Foreground = Gold, Background = new SolidColorBrush(Color.FromRgb(57, 57, 62)) });
            Grid.SetColumn(info, 2); player.Children.Add(info);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var (glyph, primary) in new[] { ("|◀", false), ("Ⅱ", true), ("▶|", false) })
            {
                var button = new Button { Content = glyph, Width = primary ? 40 : 34, Height = primary ? 40 : 34, Margin = new Thickness(4, 0, 4, 0), Background = primary ? Brushes.White : Brushes.Transparent, Foreground = primary ? Brushes.Black : Brushes.White, BorderThickness = new Thickness(0), FontSize = 15, FontWeight = FontWeights.SemiBold };
                AutomationProperties.SetName(button, glyph); controls.Children.Add(button);
            }
            Grid.SetRow(controls, 1); Grid.SetColumnSpan(controls, 3); player.Children.Add(controls);
            Grid.SetColumn(player, 0); grid.Children.Add(player);
        }
        var drop = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        drop.Children.Add(new Rectangle { RadiusX = 16, RadiusY = 16, Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } });
        var prompt = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        prompt.Children.Add(new TextBlock { Text = "↓", FontSize = 24, Foreground = Gold, HorizontalAlignment = HorizontalAlignment.Center });
        prompt.Children.Add(new TextBlock { Text = "Drop file", FontSize = 13, Foreground = Brushes.White });
        drop.Children.Add(prompt); Grid.SetColumn(drop, 2); grid.Children.Add(drop);
        var dock = DockSample(); Grid.SetRow(dock, 1); Grid.SetColumnSpan(dock, 3); grid.Children.Add(dock);
        return grid;
    }

    private static UIElement DockSample()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (glyph, label, selected) in new[] { ("◉", "Now", true), ("▤", "Tray", false), ("♫", "Media", false), ("✎", "Notes", false), ("☑", "To-do", false), ("⌕", "", false) })
        {
            var item = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, Width = 48 };
            item.Children.Add(new TextBlock { Text = glyph, FontSize = 19, Foreground = selected ? Brushes.White : Secondary, HorizontalAlignment = HorizontalAlignment.Center });
            item.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = selected ? Gold : Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
            AutomationProperties.SetName(item, string.IsNullOrEmpty(label) ? "Search" : label);
            row.Children.Add(item);
        }
        return row;
    }
}
#endif
