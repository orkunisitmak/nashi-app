using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Nashi.Core;
namespace Nashi.Windows;

public sealed class ImageExportPackageWindow : Window
{
    private readonly IReadOnlyList<string> inputs;
    private readonly ImageExportPackageStore store;
    private readonly TextBox name = new();
    private readonly ComboBox saved = new();
    private readonly StackPanel rows = new();
    private readonly List<OutputRow> outputs = [];
    private readonly TextBlock preview = new() { FontFamily = new("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock count = Label("", 15), message = Label("", 12);
    private readonly Button export, save, remove, add;
    private readonly Func<string?>? chooseDestination;
    private bool loading;
    public string? Destination { get; private set; }

    private sealed record OutputRow(ComboBox Format, TextBox Edge, TextBox Folder);
    public ImageExportPackage Package => new(name.Text, outputs.Select(row => new ImageExportVariant(
        row.Format.SelectedItem as string ?? "webp",
        int.TryParse(row.Edge.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var edge) ? edge : -1,
        row.Folder.Text)).ToArray());

    public ImageExportPackageWindow(Window owner, IReadOnlyList<string> inputs, ImageExportPackageStore store, Func<string?>? chooseDestination = null)
    {
        Owner = owner; this.inputs = inputs; this.store = store; this.chooseDestination = chooseDestination;
        Title = L10n.Text("Export image package"); Width = 770; Height = 740; MinWidth = 720; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Theme.Attach(this);
        var root = new DockPanel { Margin = new(22) }; Content = root;
        var heading = new StackPanel { Margin = new(0, 0, 0, 16) };
        heading.Children.Add(Label("Export image package", 23)); heading.Children.Add(Label("Several formats and sizes, in one export.", 12));
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new DockPanel { Margin = new(0, 16, 0, 0) };
        export = MainWindow.Primary("Export package…", Export);
        AutomationProperties.SetAutomationId(export, "PackageExport");
        DockPanel.SetDock(export, Dock.Right); footer.Children.Add(export);
        var cancel = MainWindow.Button("Cancel", () => Close()); cancel.IsCancel = true;
        DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel); footer.Children.Add(Label("Original stays safe", 11));
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var content = new StackPanel(); root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var savedRow = new DockPanel();
        remove = MainWindow.Button("Delete saved package", RemoveSaved); DockPanel.SetDock(remove, Dock.Right); savedRow.Children.Add(remove);
        savedRow.Children.Add(saved); content.Children.Add(Label("Saved packages", 12)); content.Children.Add(savedRow);
        saved.SelectionChanged += (_, _) => { if (!loading) LoadSaved(); };
        AutomationProperties.SetAutomationId(saved, "PackageSaved");
        content.Children.Add(Label("Package name", 12)); content.Children.Add(name);
        AutomationProperties.SetAutomationId(name, "PackageName"); name.TextChanged += (_, _) => RefreshPreview();
        content.Children.Add(Label("Each output gets its own subfolder. Size limits keep the aspect ratio and never enlarge images.", 12));
        var header = new Grid { Margin = new(0, 12, 0, 4) };
        Columns(header);
        foreach (var (title, column) in new[] { ("Format", 0), ("Longest edge (px)", 1), ("Subfolder", 2) })
        { var label = Label(title, 11); Grid.SetColumn(label, column); header.Children.Add(label); }
        content.Children.Add(header); content.Children.Add(rows);
        var controls = new DockPanel { Margin = new(0, 8, 0, 12) };
        save = MainWindow.Button("Save package", SavePackage); AutomationProperties.SetAutomationId(save, "PackageSave");
        DockPanel.SetDock(save, Dock.Right); controls.Children.Add(save);
        add = MainWindow.Button("Add output", () => { AddRow(new("png", 0, "output" + (outputs.Count + 1))); RefreshPreview(); });
        DockPanel.SetDock(add, Dock.Left); controls.Children.Add(add); controls.Children.Add(Label("0 keeps the original size.", 11)); content.Children.Add(controls);
        content.Children.Add(message); content.Children.Add(new Separator { Margin = new(0, 10, 0, 10) }); content.Children.Add(count);
        content.Children.Add(Label("A new package folder keeps existing files safe. Its name gets a number if needed.", 12));
        var inset = new Border { Child = preview, Padding = new(14), CornerRadius = new(12), Margin = new(0, 10, 0, 10) };
        inset.SetResourceReference(BackgroundProperty, "Inset"); content.Children.Add(inset);
        AutomationProperties.SetAutomationId(preview, "PackagePreview");
        content.Children.Add(Label("Source metadata is omitted. JPG uses a white background for transparent areas.", 11));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && export.IsEnabled) { export.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; } };
        RefreshSaved(); LoadPackage(ImageExportPackage.Web); RefreshPreview();
    }

    private static TextBlock Label(string text, double size)
    {
        var block = L10n.Label(new TextBlock { Text = text, FontSize = size, Margin = new(4, 5, 4, 5), TextWrapping = TextWrapping.Wrap });
        block.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); return block;
    }
    private static void Columns(Grid grid)
    {
        grid.ColumnDefinitions.Add(new() { Width = new(105) }); grid.ColumnDefinitions.Add(new() { Width = new(150) });
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new(45) });
    }
    private void AddRow(ImageExportVariant variant)
    {
        var format = new ComboBox { ItemsSource = ImageExportPackage.Formats, SelectedItem = variant.Format };
        var edge = new TextBox { Text = variant.MaxDimension.ToString(CultureInfo.InvariantCulture) };
        var folder = new TextBox { Text = variant.Subfolder };
        AutomationProperties.SetName(format, L10n.Text("Format")); AutomationProperties.SetName(edge, L10n.Text("Longest edge (px)")); AutomationProperties.SetName(folder, L10n.Text("Subfolder"));
        var row = new OutputRow(format, edge, folder); outputs.Add(row);
        var grid = new Grid { Margin = new(0, 3, 0, 3) }; Columns(grid);
        var delete = MainWindow.Button("−", () => { if (outputs.Count == 1) return; outputs.Remove(row); rows.Children.Remove(grid); RefreshPreview(); });
        delete.ToolTip = L10n.Text("Remove output"); AutomationProperties.SetName(delete, L10n.Text("Remove output"));
        foreach (var (control, column) in new (FrameworkElement, int)[] { (format, 0), (edge, 1), (folder, 2), (delete, 3) })
        { control.Margin = new(3); Grid.SetColumn(control, column); grid.Children.Add(control); }
        rows.Children.Add(grid);
        format.SelectionChanged += (_, _) => RefreshPreview(); edge.TextChanged += (_, _) => RefreshPreview(); folder.TextChanged += (_, _) => RefreshPreview();
    }
    private void LoadPackage(ImageExportPackage package)
    {
        loading = true;
        try { name.Text = package.Name; outputs.Clear(); rows.Children.Clear(); foreach (var variant in package.Variants) AddRow(variant); }
        finally { loading = false; }
        RefreshPreview();
    }
    private void RefreshSaved(string selected = "")
    {
        loading = true;
        try
        {
            saved.Items.Clear(); saved.Items.Add(L10n.Content(new ComboBoxItem { Content = "Example package", Tag = "" }));
            foreach (var package in store.Packages) saved.Items.Add(new ComboBoxItem { Content = package.Name, Tag = package.Name });
            saved.SelectedItem = saved.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected) ?? saved.Items[0];
            remove.IsEnabled = selected.Length > 0;
        }
        finally { loading = false; }
    }
    private void LoadSaved()
    {
        var selected = (saved.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        remove.IsEnabled = selected.Length > 0;
        LoadPackage(store.Packages.FirstOrDefault(package => package.Name == selected) ?? ImageExportPackage.Web);
    }
    private void SavePackage()
    {
        try { store.Save(Package); RefreshSaved(Package.Name); L10n.SetText(message, "Package saved."); }
        catch (Exception error) { L10n.SetText(message, error.Message); }
    }
    private void RemoveSaved()
    {
        try { store.Remove((saved.SelectedItem as ComboBoxItem)?.Tag as string ?? ""); RefreshSaved(); L10n.SetText(message, "Saved package removed."); }
        catch (Exception error) { L10n.SetText(message, error.Message); }
    }
    private void Export()
    {
        if (!RefreshPreview()) return;
        string? destination;
        if (chooseDestination is not null) destination = chooseDestination();
        else
        {
            var dialog = new OpenFolderDialog { Title = L10n.Text("Save new files to…"), InitialDirectory = Path.GetDirectoryName(inputs[0]) };
            destination = dialog.ShowDialog(this) == true ? dialog.FolderName : null;
        }
        // Keep an unsaved draft open when the destination picker is cancelled.
        if (string.IsNullOrEmpty(destination)) return;
        Destination = destination; DialogResult = true;
    }
    private bool RefreshPreview()
    {
        if (loading) return false;
        add.IsEnabled = outputs.Count < 12;
        try
        {
            var plan = Package.Plan(inputs);
            L10n.SetText(count, plan.Count == 1 ? "1 output file" : $"{plan.Count} output files");
            // User filenames and package names remain verbatim.
            preview.Text = Package.Name + "/\n" + string.Join("\n", plan.Take(24).Select(entry => "  " + entry.RelativePath)) +
                (plan.Count > 24 ? "\n" + L10n.Text($"{plan.Count - 24} more outputs") : "");
            L10n.SetText(message, ""); export.IsEnabled = save.IsEnabled = true; return true;
        }
        catch (ArgumentException error)
        {
            preview.Text = ""; L10n.SetText(count, "0 output files"); L10n.SetText(message, error.Message);
            export.IsEnabled = save.IsEnabled = false; return false;
        }
    }
}
