using System.Text.Json;
namespace Nashi.Core;

public sealed class ImageExportPackageStore(string directory)
{
    private readonly string file = Path.Combine(directory, "image-export-packages.json");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public IReadOnlyList<ImageExportPackage> Packages
    {
        get
        {
            if (!File.Exists(file)) return [];
            try
            {
                if (new FileInfo(file).Length > 1_000_000) return [];
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var result = new List<ImageExportPackage>();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    try
                    {
                        var package = item.Deserialize<ImageExportPackage>(Json);
                        if (package is not null) { package.Validate(); result.Add(package); }
                    }
                    catch (Exception error) when (error is JsonException or ArgumentException) { }
                    if (result.Count == 50) break;
                }
                return result;
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException) { return []; }
        }
    }

    public void Save(ImageExportPackage package)
    {
        package.Validate();
        var current = Packages.Where(p => ImageExportPackage.NameKey(p.Name) != ImageExportPackage.NameKey(package.Name)).ToList();
        if (current.Count >= 50) throw new ArgumentException("You can save up to 50 export packages.");
        current.Add(package); Write(current);
    }
    public void Remove(string name) => Write(Packages.Where(p => ImageExportPackage.NameKey(p.Name) != ImageExportPackage.NameKey(name)));

    private void Write(IEnumerable<ImageExportPackage> packages)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(packages, Json)); File.Move(temporary, file, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
