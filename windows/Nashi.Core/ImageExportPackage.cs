using System.Text;
namespace Nashi.Core;

public sealed record ImageExportVariant(string Format, int MaxDimension, string Subfolder)
{
    public (int Width, int Height) Dimensions(int width, int height)
    {
        if (MaxDimension == 0 || Math.Max(width, height) <= MaxDimension) return (width, height);
        var scale = (double)MaxDimension / Math.Max(width, height);
        return (Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero)),
                Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero)));
    }
}

public sealed record ImageExportOutput(int InputIndex, int VariantIndex, string RelativePath);

public sealed record ImageExportPackage(string Name, ImageExportVariant[] Variants)
{
    public static readonly string[] Formats = ["webp", "jpg", "png"];
    public static ImageExportPackage Web => new("Web package", [
        new("webp", 1600, "web"), new("jpg", 400, "thumbnail"), new("jpg", 0, "archive")
    ]);

    public void Validate()
    {
        if (!ValidFolderName(Name)) throw new ArgumentException("Use a short package name without paths or reserved names.");
        if (Variants is null || Variants.Length is < 1 or > 12) throw new ArgumentException("Add between 1 and 12 outputs to the package.");
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in Variants)
        {
            if (variant is null || !Formats.Contains(variant.Format)) throw new ArgumentException("Package outputs must be PNG, JPG or WebP.");
            if (variant.MaxDimension is < 0 or > 16384) throw new ArgumentException("Use a longest edge from 1 to 16384 pixels, or 0 for original size.");
            if (!ValidFolderName(variant.Subfolder)) throw new ArgumentException("Use short subfolder names without paths or reserved names.");
            if (!folders.Add(NameKey(variant.Subfolder))) throw new ArgumentException("Give each output a different subfolder name.");
        }
    }

    public IReadOnlyList<ImageExportOutput> Plan(IReadOnlyList<string> inputs)
    {
        Validate();
        if (inputs.Count is < 1 or > 1000 || inputs.Any(p => !FeatureCatalog.IsImage(p)))
            throw new ArgumentException("Choose between 1 and 1000 still images for the package.");
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ImageExportOutput>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var stem = OutputStem(Path.GetFileNameWithoutExtension(inputs[index]));
            for (var variantIndex = 0; variantIndex < Variants.Length; variantIndex++)
            {
                var variant = Variants[variantIndex];
                var number = 1;
                string relative;
                do
                {
                    relative = variant.Subfolder + "/" + stem + (number == 1 ? "" : $" {number}") + "." + variant.Format;
                    number++;
                } while (!used.Add(NameKey(relative)));
                result.Add(new(index, variantIndex, relative));
            }
        }
        return result;
    }

    internal static string NameKey(string value) => value.Normalize(NormalizationForm.FormC).ToLowerInvariant();
    private static readonly HashSet<string> Reserved = new(["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).SelectMany(n => new[] { "COM" + n, "LPT" + n })], StringComparer.OrdinalIgnoreCase);
    private static bool ValidFolderName(string? value) => !string.IsNullOrWhiteSpace(value) &&
        Encoding.UTF8.GetByteCount(value.Normalize(NormalizationForm.FormD)) <= 120 && value == value.Trim() &&
        !value.StartsWith('.') && !value.EndsWith('.') && !value.Any(c => c < 32 || c == 127 || "/\\:*?\"<>|".Contains(c)) &&
        !Reserved.Contains(value.Split('.')[0]);

    private static string OutputStem(string value)
    {
        var stem = new string(value.Normalize(NormalizationForm.FormC).Select(c => c < 32 || c == 127 || "/\\:*?\"<>|".Contains(c) ? '_' : c).ToArray());
        // Remove complete Unicode scalars so a long emoji filename cannot leave an unpaired surrogate.
        while (Encoding.UTF8.GetByteCount(stem.Normalize(NormalizationForm.FormD)) > 120)
            stem = stem[..^(char.IsLowSurrogate(stem[^1]) ? 2 : 1)];
        stem = stem.Trim('.', ' ');
        return ValidFolderName(stem) ? stem : "Image";
    }
}
