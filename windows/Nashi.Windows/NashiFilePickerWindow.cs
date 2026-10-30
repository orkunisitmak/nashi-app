using Nashi.Core;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nashi.Windows;

/// <summary>A Nashi-styled, multi-select file browser for the island's Add files action.</summary>
internal sealed class NashiFilePickerWindow : Window
{
    private sealed record Entry(string Path, string Name, string Detail, bool IsDirectory);

    private readonly ObservableCollection<Entry> visibleEntries = [];
    private readonly List<Entry> allEntries = [];
    private readonly ListView fileList = new();
    private readonly TextBox address = new();
    private readonly TextBox search = new();
    private readonly TextBlock locationLabel = new();
    private readonly TextBlock selectionLabel = new();
    private readonly TextBlock statusLabel = new();
    private readonly HashSet<string>? allowedExtensions;
    private readonly Button backButton;
    private readonly Button forwardButton;
    private readonly Button upButton;
    private readonly Button addButton;
    private string currentPath;
    private readonly Stack<string> backHistory = [];
    private readonly Stack<string> forwardHistory = [];
    private int loadGeneration;

    internal IReadOnlyList<string> SelectedPaths { get; private set; } = [];
    internal bool UIAuditReady => IsLoaded && allEntries.Count > 0 && fileList.Items.Count > 0;
    internal bool UIAuditHasSyntheticFiles => allEntries.Any(entry => entry.Name == "Example image.png") && allEntries.Any(entry => entry.Name == "Example notes.txt");
    internal bool UIAuditSingleSelection => fileList.SelectionMode == SelectionMode.Single;
    internal bool UIAuditFirstItemIsFile => visibleEntries.Count > 0 && !visibleEntries[0].IsDirectory;
    internal bool UIAuditHasVisibleFile(string name) => visibleEntries.Any(entry => !entry.IsDirectory && entry.Name == name);
    internal IReadOnlyList<string> UIAuditAllowedExtensions => allowedExtensions?.Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    internal static IReadOnlyList<string> UIAuditSortPaths(IEnumerable<string> files, IEnumerable<string> folders, bool downloads)
    {
        var entries = files.Select(path => new Entry(path, Path.GetFileName(path), "", false))
            .Concat(folders.Select(path => new Entry(path, Path.GetFileName(path), "", true)));
        return SortEntries(entries, downloads).Select(entry => entry.Name).ToArray();
    }
    internal FrameworkElement? UIAuditSurface => Content as FrameworkElement;

    internal NashiFilePickerWindow(Window owner, string? initialDirectory = null, IEnumerable<string>? fileExtensions = null, bool allowMultiple = true, string purpose = "generic")
    {
        allowedExtensions = fileExtensions?.Select(extension => extension.TrimStart('.')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pdfPurpose = purpose == "pdf";
        Owner = owner;
        AutomationProperties.SetAutomationId(this, "NashiFilePickerWindow");
        Title = L10n.Text(pdfPurpose ? (allowMultiple ? "Choose PDF files to edit" : "Choose a PDF to edit") : "Choose files for Nashi");
        Width = Math.Min(820, Math.Max(650, SystemParameters.WorkArea.Width - 48));
        Height = Math.Min(610, Math.Max(460, SystemParameters.WorkArea.Height - 72));
        MinWidth = 620;
        MinHeight = 440;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        currentPath = string.IsNullOrWhiteSpace(initialDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : initialDirectory;
        if (!Directory.Exists(currentPath)) currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var shell = new Border
        {
            Background = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 57, 63)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Opacity = .42, BlurRadius = 26, ShadowDepth = 10 }
        };
        Content = shell;
        var layout = new Grid { Margin = new Thickness(19, 13, 19, 14) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.Child = layout;

        var heading = new Grid { Margin = new Thickness(2, 1, 1, 14) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(Color.FromRgb(31, 35, 43)),
            Child = new Image { Source = FileGarden.Artwork("NashiMark"), Width = 22, Height = 22, Stretch = Stretch.Uniform }
        };
        heading.Children.Add(mark);
        var titleStack = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(Label(pdfPurpose ? (L10n.Language == "tr" ? "PDF seç" : "Choose a PDF") : "Choose files", 15, FontWeights.SemiBold));
        titleStack.Children.Add(Label(pdfPurpose ? (L10n.Language == "tr" ? "Yalnızca PDF dosyaları · Bir PDF seç." : "PDF files only · Select one PDF to edit.") : "Select one or more files to add to Nashi.", 10, FontWeights.Normal, muted: true));
        Grid.SetColumn(titleStack, 1);
        heading.Children.Add(titleStack);
        var closeButton = IconButton("×", "Close", () => DialogResult = false);
        Grid.SetColumn(closeButton, 2);
        heading.Children.Add(closeButton);
        titleStack.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Grid.SetRow(heading, 0);
        layout.Children.Add(heading);

        var navigation = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        backButton = IconButton("‹", "Back", GoBack);
        backButton.Margin = new Thickness(0, 0, 6, 0);
        navigation.Children.Add(backButton);
        forwardButton = IconButton("›", "Forward", GoForward);
        forwardButton.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(forwardButton, 1);
        navigation.Children.Add(forwardButton);
        upButton = IconButton("↑", "Up one folder", GoUp);
        upButton.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(upButton, 2);
        navigation.Children.Add(upButton);
        address.Height = 36;
        address.Padding = new Thickness(12, 7, 12, 7);
        address.Background = new SolidColorBrush(Color.FromRgb(22, 23, 26));
        address.Foreground = Brushes.White;
        address.BorderBrush = new SolidColorBrush(Color.FromRgb(54, 56, 61));
        address.BorderThickness = new Thickness(1);
        address.VerticalContentAlignment = VerticalAlignment.Center;
        address.Text = currentPath;
        AutomationProperties.SetAutomationId(address, "NashiFilePickerAddress");
        AutomationProperties.SetName(address, L10n.Text("Current folder path"));
        address.KeyDown += (_, e) => { if (e.Key == Key.Enter) { _ = NavigateAsync(address.Text); e.Handled = true; } };
        Grid.SetColumn(address, 3);
        navigation.Children.Add(address);
        search.Height = 36;
        search.Margin = new Thickness(9, 0, 0, 0);
        search.Padding = new Thickness(12, 7, 12, 7);
        search.Background = new SolidColorBrush(Color.FromRgb(22, 23, 26));
        search.Foreground = Brushes.White;
        search.BorderBrush = new SolidColorBrush(Color.FromRgb(54, 56, 61));
        search.BorderThickness = new Thickness(1);
        search.VerticalContentAlignment = VerticalAlignment.Center;
        search.ToolTip = L10n.Text("Search this folder");
        search.TextChanged += (_, _) => ApplyFilter();
        AutomationProperties.SetAutomationId(search, "NashiFilePickerSearch");
        AutomationProperties.SetName(search, L10n.Text("Search this folder"));
        var searchBox = new Grid();
        searchBox.Children.Add(search);
        var searchPlaceholder = Label("Search this folder", 10, FontWeights.Normal, muted: true);
        searchPlaceholder.Margin = new Thickness(21, 0, 0, 0);
        searchPlaceholder.VerticalAlignment = VerticalAlignment.Center;
        searchPlaceholder.IsHitTestVisible = false;
        searchPlaceholder.Visibility = Visibility.Visible;
        search.TextChanged += (_, _) => searchPlaceholder.Visibility = string.IsNullOrEmpty(search.Text) ? Visibility.Visible : Visibility.Collapsed;
        searchBox.Children.Add(searchPlaceholder);
        Grid.SetColumn(search, 4);
        Grid.SetColumn(searchBox, 4);
        navigation.Children.Add(searchBox);
        Grid.SetRow(navigation, 1);
        layout.Children.Add(navigation);

        var body = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(154) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sidebar = new StackPanel { Margin = new Thickness(0, 4, 11, 0) };
        AddPlace(sidebar, "Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "⌂");
        AddPlace(sidebar, "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "▦");
        AddPlace(sidebar, "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "▤");
        AddPlace(sidebar, "Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "↓");
        AddPlace(sidebar, "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "▧");
        AddPlace(sidebar, "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "♫");
        AddPlace(sidebar, "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "▷");
        foreach (var drive in DriveInfo.GetDrives())
        {
            try { if (drive.IsReady) AddPlace(sidebar, drive.Name.TrimEnd(Path.DirectorySeparatorChar), drive.RootDirectory.FullName, "◉"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var sidebarScroll = new ScrollViewer { Content = sidebar, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(sidebarScroll, 0);
        body.Children.Add(sidebarScroll);

        var browser = new Grid();
        browser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        browser.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        browser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var locationRow = new DockPanel { Margin = new Thickness(10, 1, 8, 8) };
        locationLabel.Foreground = new SolidColorBrush(Color.FromRgb(161, 164, 171));
        locationLabel.FontSize = 10;
        locationLabel.VerticalAlignment = VerticalAlignment.Center;
        locationRow.Children.Add(locationLabel);
        Grid.SetRow(locationRow, 0);
        browser.Children.Add(locationRow);

        fileList.ItemsSource = visibleEntries;
        AutomationProperties.SetAutomationId(fileList, "NashiFilePickerItems");
        AutomationProperties.SetName(fileList, L10n.Text("Files in this folder"));
        fileList.SelectionMode = allowMultiple ? SelectionMode.Extended : SelectionMode.Single;
        fileList.Background = Brushes.Transparent;
        fileList.BorderThickness = new Thickness(0);
        fileList.Foreground = Brushes.White;
        fileList.ItemContainerStyle = EntryStyle();
        fileList.ItemTemplate = EntryTemplate();
        fileList.SelectionChanged += (_, _) => UpdateSelection();
        fileList.MouseDoubleClick += (_, _) => ActivateSelection();
        fileList.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ActivateSelection(); e.Handled = true; } };
        var listFrame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 15, 17)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(40, 42, 47)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = fileList
        };
        Grid.SetRow(listFrame, 1);
        browser.Children.Add(listFrame);
        statusLabel.Margin = new Thickness(10, 7, 8, 0);
        statusLabel.Foreground = new SolidColorBrush(Color.FromRgb(150, 153, 161));
        statusLabel.FontSize = 10;
        Grid.SetRow(statusLabel, 2);
        browser.Children.Add(statusLabel);
        Grid.SetColumn(browser, 1);
        body.Children.Add(browser);
        Grid.SetRow(body, 2);
        layout.Children.Add(body);

        var footer = new DockPanel { Margin = new Thickness(0, 4, 0, 0), LastChildFill = false };
        selectionLabel.Text = L10n.Text("No files selected");
        selectionLabel.Foreground = new SolidColorBrush(Color.FromRgb(161, 164, 171));
        selectionLabel.FontSize = 11;
        selectionLabel.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(selectionLabel);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = ActionButton("Cancel", false, () => DialogResult = false);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(cancel);
        addButton = ActionButton("Add files", true, CommitSelection);
        addButton.IsEnabled = false;
        buttons.Children.Add(addButton);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);

        Loaded += async (_, _) => { fileList.Focus(); await NavigateAsync(currentPath, remember: false); };
        Closed += (_, _) => loadGeneration++;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }

    private static TextBlock Label(string value, double size, FontWeight weight, bool muted = false)
    {
        var label = new TextBlock { Text = value, FontSize = size, FontWeight = weight, Foreground = muted ? new SolidColorBrush(Color.FromRgb(154, 157, 165)) : Brushes.White };
        return L10n.Label(label);
    }

    private Button IconButton(string glyph, string tip, Action action)
    {
        var button = new Button { Content = glyph, Width = 35, Height = 35, Margin = new Thickness(0), Padding = new Thickness(0), FontSize = 15, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(27, 28, 32)), BorderBrush = new SolidColorBrush(Color.FromRgb(55, 57, 63)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = L10n.Text(tip), Template = ButtonTemplate() };
        button.Click += (_, _) => action();
        return button;
    }

    private Button ActionButton(string title, bool primary, Action action)
    {
        var button = new Button { Content = title, MinWidth = primary ? 120 : 88, Height = 38, Padding = new Thickness(14, 7, 14, 7), FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Background = primary ? new SolidColorBrush(Color.FromRgb(10, 132, 255)) : new SolidColorBrush(Color.FromRgb(28, 29, 33)), BorderBrush = primary ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(55, 57, 63)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        button.Template = ButtonTemplate();
        L10n.Content(button);
        button.Click += (_, _) => action();
        return button;
    }

    private void AddPlace(Panel sidebar, string title, string path, string glyph)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, Width = 25, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(176, 179, 187)), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(Label(title, 11, FontWeights.Medium));
        var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 34, Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(9, 4, 8, 4), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Template = ButtonTemplate() };
        button.Click += async (_, _) => await NavigateAsync(path);
        sidebar.Children.Add(button);
    }

    private static Style EntryStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(3, 2, 3, 2)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.TemplateProperty, EntryContainerTemplate()));
        return style;
    }

    private static ControlTemplate EntryContainerTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Chrome";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(ListViewItem)) { VisualTree = border };
        var selected = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(33, 46, 63)), "Chrome"));
        template.Triggers.Add(selected);
        var hovered = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hovered.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(32, 34, 39)), "Chrome"));
        template.Triggers.Add(hovered);
        return template;
    }

    private static DataTemplate EntryTemplate()
    {
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
        var icon = new FrameworkElementFactory(typeof(Border));
        icon.SetValue(Border.WidthProperty, 32d);
        icon.SetValue(Border.HeightProperty, 32d);
        icon.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        icon.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(31, 33, 38)));
        var iconText = new FrameworkElementFactory(typeof(TextBlock));
        iconText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Entry.IsDirectory)) { Converter = new EntryGlyphConverter() });
        iconText.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        iconText.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        iconText.SetValue(TextBlock.FontSizeProperty, 15d);
        iconText.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(191, 196, 205)));
        icon.AppendChild(iconText);
        row.AppendChild(icon);
        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Entry.Name)));
        name.SetValue(TextBlock.FontSizeProperty, 11d);
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        name.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        stack.AppendChild(name);
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Entry.Detail)));
        detail.SetValue(TextBlock.FontSizeProperty, 9d);
        detail.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(147, 151, 160)));
        stack.AppendChild(detail);
        row.AppendChild(stack);
        var template = new DataTemplate(typeof(Entry)) { VisualTree = row };
        return template;
    }

    private static ControlTemplate ButtonTemplate()
    {
        var chrome = new FrameworkElementFactory(typeof(Border));
        chrome.Name = "Chrome";
        chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        chrome.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        chrome.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        chrome.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(38, 40, 45)), "Chrome"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(52, 61, 75)), "Chrome"));
        template.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .42));
        template.Triggers.Add(disabled);
        return template;
    }

    private async Task NavigateAsync(string path, bool remember = true)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (!Directory.Exists(path))
        {
            statusLabel.Text = L10n.Text("That folder is unavailable.");
            return;
        }
        var generation = ++loadGeneration;
        var oldPath = currentPath;
        currentPath = Path.GetFullPath(path);
        var destinationPath = currentPath;
        address.Text = currentPath;
        locationLabel.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(currentPath));
        if (string.IsNullOrEmpty(locationLabel.Text)) locationLabel.Text = currentPath;
        backButton.IsEnabled = remember && oldPath != currentPath;
        forwardButton.IsEnabled = false;
        upButton.IsEnabled = Directory.GetParent(currentPath) is not null;
        statusLabel.Text = L10n.Text("Loading folder…");
        visibleEntries.Clear();
        fileList.SelectedIndex = -1;
        try
        {
            var result = await Task.Run(() =>
            {
                var directories = Directory.EnumerateDirectories(destinationPath)
                    .Select(folder => new Entry(folder, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), L10n.Text("Folder"), true));
                var files = Directory.EnumerateFiles(destinationPath)
                    .Where(file => allowedExtensions is null || allowedExtensions.Contains(FeatureCatalog.Extension(file)))
                    .Select(file => new FileInfo(file))
                    .Select(file => new Entry(file.FullName, file.Name, FormatSize(file.Length), false));
                return SortEntries(directories.Concat(files), IsDownloadsDirectory(destinationPath));
            });
            if (generation != loadGeneration) return;
            if (remember && oldPath != destinationPath)
            {
                backHistory.Push(oldPath);
                forwardHistory.Clear();
            }
            backButton.IsEnabled = backHistory.Count > 0;
            forwardButton.IsEnabled = forwardHistory.Count > 0;
            allEntries.Clear();
            allEntries.AddRange(result);
            ApplyFilter();
            statusLabel.Text = result.Count == 0 && allowedExtensions?.Contains("pdf") == true
                ? (L10n.Language == "tr" ? "Bu klasörde PDF yok. Belgeler veya İndirilenler\u0027i dene." : "No PDFs in this folder. Try Documents or Downloads.")
                : CountText(result.Count);
        }
        catch (UnauthorizedAccessException)
        {
            if (generation == loadGeneration)
            {
                currentPath = oldPath;
                address.Text = oldPath;
                ApplyFilter();
                statusLabel.Text = L10n.Text("You do not have permission to view this folder.");
            }
        }
        catch (IOException error)
        {
            if (generation == loadGeneration)
            {
                currentPath = oldPath;
                address.Text = oldPath;
                ApplyFilter();
                statusLabel.Text = error.Message;
            }
        }
    }

    private static bool IsDownloadsDirectory(string path)
    {
        var downloadsPath = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(downloadsPath), StringComparison.OrdinalIgnoreCase);
    }

    private static List<Entry> SortEntries(IEnumerable<Entry> entries, bool prioritizeMedia)
    {
        return entries
            .OrderBy(entry => entry.IsDirectory ? (prioritizeMedia ? 2 : 1) : prioritizeMedia ? (FeatureCatalog.IsImage(entry.Path) || FeatureCatalog.IsVideo(entry.Path) ? 0 : 1) : 0)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }

    private static string CountText(int count) => string.Format(System.Globalization.CultureInfo.CurrentCulture, L10n.Text("{0} items"), count);

    private void ApplyFilter()
    {
        var query = search.Text.Trim();
        visibleEntries.Clear();
        foreach (var item in allEntries.Where(item => string.IsNullOrEmpty(query) || item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))) visibleEntries.Add(item);
        statusLabel.Text = visibleEntries.Count == 0 && allowedExtensions?.Contains("pdf") == true
            ? (L10n.Language == "tr" ? "Bu klasörde PDF yok. Belgeler veya İndirilenler'i dene." : "No PDFs in this folder. Try Documents or Downloads.")
            : string.IsNullOrEmpty(query) ? CountText(visibleEntries.Count) : string.Format(System.Globalization.CultureInfo.CurrentCulture, L10n.Text("{0} matches"), visibleEntries.Count);
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = fileList.SelectedItems.Cast<Entry>().Where(entry => !entry.IsDirectory).ToArray();
        var type=allowedExtensions?.Count==1?allowedExtensions.Single().ToUpperInvariant():"file";
        selectionLabel.Text = selected.Length == 0 ? L10n.Text("No files selected") : pdfPurposeLabel(selected.Length,type);
        addButton.IsEnabled = selected.Length > 0;

        static string pdfPurposeLabel(int count,string type)=>type=="PDF"?L10n.Language=="tr"?$"{count} PDF seçildi":$"{count} PDF selected":string.Format(System.Globalization.CultureInfo.CurrentCulture,L10n.Text("{0} selected"),count);
    }

    private async void GoBack()
    {
        if (backHistory.Count == 0) return;
        var path = backHistory.Pop();
        forwardHistory.Push(currentPath);
        await NavigateAsync(path, remember: false);
        backButton.IsEnabled = backHistory.Count > 0;
        forwardButton.IsEnabled = forwardHistory.Count > 0;
    }

    private async void GoForward()
    {
        if (forwardHistory.Count == 0) return;
        var path = forwardHistory.Pop();
        backHistory.Push(currentPath);
        await NavigateAsync(path, remember: false);
        backButton.IsEnabled = backHistory.Count > 0;
        forwardButton.IsEnabled = forwardHistory.Count > 0;
    }

    private async void GoUp()
    {
        if (Directory.GetParent(currentPath) is { } parent) await NavigateAsync(parent.FullName);
    }

    private async void ActivateSelection()
    {
        if (fileList.SelectedItem is not Entry selected) return;
        if (selected.IsDirectory) await NavigateAsync(selected.Path);
        else CommitSelection();
    }

    private void CommitSelection()
    {
        var selected = fileList.SelectedItems.Cast<Entry>().Where(entry => !entry.IsDirectory).Select(entry => entry.Path).ToArray();
        if (selected.Length == 0) return;
        SelectedPaths = selected;
        DialogResult = true;
    }

    private sealed class EntryGlyphConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is true ? "▰" : "▧";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
