using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;

internal static class ImageExportPackageUiChecks
{
    internal static void Run(string root, MainWindow owner, string source, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "image-packages"); Directory.CreateDirectory(directory);
        var store = new ImageExportPackageStore(Path.Combine(directory, "Preferences"));
        var previousLanguage = L10n.Language; var previousDark = Theme.Dark;
        try
        {
            foreach (var dark in new[] { false, true }) foreach (var language in new[] { "en", "tr" })
            {
                Theme.Apply(dark ? "dark" : "light"); L10n.Language = language;
                var dialog = new ImageExportPackageWindow(owner, [source], store);
                dialog.Show(); dialog.UpdateLayout(); DesktopInput.Pump();
                var export = Find<Button>(dialog, "PackageExport");
                var format = Descendants(dialog).OfType<ComboBox>().First(field => ReferenceEquals(field.ItemsSource, ImageExportPackage.Formats));
                var firstEdge = Descendants(dialog).OfType<TextBox>().First(field => AutomationProperties.GetName(field) == L10n.Text("Longest edge (px)"));
                var formatBounds = format.TransformToAncestor(dialog).TransformBounds(new Rect(format.RenderSize));
                var edgeBounds = firstEdge.TransformToAncestor(dialog).TransformBounds(new Rect(firstEdge.RenderSize));
                check(formatBounds.Right <= edgeBounds.Left, "Package format picker stays inside its column without overlapping the size field");
                check(export.IsEnabled && dialog.Package.Plan([source]).Count == 3, "Native package editor previews the three example variants");
                var preview = Find<TextBlock>(dialog, "PackagePreview");
                check(preview.Text.Contains("web/") && preview.Text.Contains("thumbnail/"), "Native preview includes variant subfolders");
                var render = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                render.Render(dialog); ImageEngine.Save(render, Path.Combine(directory, $"windows-image-package-{language}-{(dark ? "dark" : "light")}.png"), "png");
                var packageName = Find<TextBox>(dialog, "PackageName"); packageName.Text = "Ürün fotoğrafları";
                var edge = Descendants(dialog).OfType<TextBox>().First(field => AutomationProperties.GetName(field) == L10n.Text("Longest edge (px)"));
                edge.Text = "invalid"; DesktopInput.Pump();
                check(!export.IsEnabled && !Find<Button>(dialog, "PackageSave").IsEnabled, "Invalid numeric text disables native export and save");
                edge.Text = "40"; DesktopInput.Pump();
                Find<Button>(dialog, "PackageSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(store.Packages.Single().Variants[0].MaxDimension == 40, "Native Save button persists the edited size");
                var spec = dialog.Package;
                // Definitions are independent of previous native renders in this same suite.
                store.Remove(spec.Name); dialog.Close();
            }
            L10n.Language = "en";
            var accepted = new ImageExportPackageWindow(owner, [source], store, () => Path.Combine(directory, "Exports"));
            accepted.Dispatcher.BeginInvoke(new Action(() =>
            {
                accepted.UpdateLayout();
                Find<TextBox>(accepted, "PackageName").Text = "Modal package";
                Find<Button>(accepted, "PackageExport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            check(accepted.ShowDialog() == true && accepted.Package.Name == "Modal package" && accepted.Destination == Path.Combine(directory, "Exports"), "Production Export button accepts the actual package dialog and destination");
            var destinationCancelled = new ImageExportPackageWindow(owner, [source], store, () => null);
            destinationCancelled.Show(); destinationCancelled.UpdateLayout(); DesktopInput.Pump();
            Find<TextBox>(destinationCancelled, "PackageName").Text = "Keep this draft";
            Find<Button>(destinationCancelled, "PackageExport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(destinationCancelled.IsVisible && destinationCancelled.Package.Name == "Keep this draft" && destinationCancelled.Destination is null, "Cancelling destination selection keeps the package draft open");
            destinationCancelled.Close();
            var cancelled = new ImageExportPackageWindow(owner, [source], store);
            cancelled.Dispatcher.BeginInvoke(new Action(() => DesktopInput.Key(cancelled, System.Windows.Input.Key.Escape)));
            check(cancelled.ShowDialog() != true, "Escape cancels package export before selecting a destination");
            check(!Directory.Exists(Path.Combine(directory, "Exports")), "Editing, saving and cancelling packages do not create exports");
        }
        finally { L10n.Language = previousLanguage; Theme.Apply(previousDark ? "dark" : "light"); }
    }
    private static T Find<T>(DependencyObject root, string identifier) where T : DependencyObject =>
        Descendants(root).OfType<T>().First(item => AutomationProperties.GetAutomationId(item) == identifier);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
