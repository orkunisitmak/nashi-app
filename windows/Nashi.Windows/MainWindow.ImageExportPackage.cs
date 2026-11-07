using Nashi.Core;
namespace Nashi.Windows;

public sealed partial class MainWindow
{
    private async Task ShowImageExportPackageAsync()
    {
        if (busy || files.Count == 0 || !files.All(FeatureCatalog.IsImage)) return;
        var selected = files.ToArray();
        var dialog = new ImageExportPackageWindow(this, selected, new ImageExportPackageStore(library.DirectoryPath));
        if (dialog.ShowDialog() != true) return;
        var package = dialog.Package;
        await RunAsync((destination, progress, token) => Task.Run(() =>
            ImageExportPackageEngine.Run(selected, package, destination, progress, token), token), "Export image package", destinationOverride: dialog.Destination);
    }
}
