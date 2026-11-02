using ImageMagick;
using Nashi.Core;
using System.Text;
using System.Text.Json;

internal static class ImageExportPackageChecks
{
    public static void Run(Action<bool, string> check, string root)
    {
        var directory = Path.Combine(root, "Image packages"); Directory.CreateDirectory(directory);
        _ = PhotoCodec.Format("png");
        var input = Path.Combine(directory, "İstanbul 🍐.png");
        using (var image = new MagickImage(new MagickColor("#FF000080"), 80, 40))
        { image.SetAttribute("comment", "Private fixture author"); image.Write(input, MagickFormat.Png); }
        var original = File.ReadAllBytes(input);
        var package = new ImageExportPackage("Product set", [new("png", 30, "web"), new("jpg", 10, "thumbnail"), new("webp", 0, "archive")]);
        var destination = Path.Combine(directory, "Exports");
        var outputs = ImageExportPackageEngine.Run([input], package, destination);
        check(outputs.Count == 3, "Package exports every variant");
        var expected = new[] { (30u, 15u, MagickFormat.Png), (10u, 5u, MagickFormat.Jpeg), (80u, 40u, MagickFormat.WebP) };
        for (var index = 0; index < outputs.Count; index++)
        {
            using var image = new MagickImage(outputs[index]);
            check((image.Width, image.Height, image.Format) == expected[index], "Package has the actual requested format and dimensions");
            check(image.GetAttribute("comment") is null && !image.ProfileNames.Contains("exif"), "Source metadata is omitted from package outputs");
            using var pixels = image.GetPixels(); var color = pixels.GetPixel(0, 0).ToColor()!;
            check(index == 1 ? color.A == 255 && color.G > 100 && color.B > 100 : color.A < 200, "Package preserves PNG/WebP alpha and flattens JPG on white");
        }
        check(File.ReadAllBytes(input).SequenceEqual(original), "Package preserves original bytes");
        var again = ImageExportPackageEngine.Run([input], package, destination);
        check(again.All(p => p.Contains("Product set 2")) && outputs.All(File.Exists), "Repeated export keeps the existing package together");
        var second = Path.Combine(directory, "two", "i̇stanbul 🍐.png"); Directory.CreateDirectory(Path.GetDirectoryName(second)!); File.Copy(input, second);
        var plan = package.Plan([input, second, input]);
        check(plan.Count == 9 && plan.Select(p => p.RelativePath.Normalize().ToLowerInvariant()).Distinct().Count() == 9, "Preview gives duplicate basenames distinct output names");
        var batch = ImageExportPackageEngine.Run([input, second, input], package, destination);
        check(batch.Zip(plan).All(pair => pair.First.Replace('\\', '/').EndsWith(pair.Second.RelativePath, StringComparison.Ordinal)), "Preview paths match published files");
        var portrait = Path.Combine(directory, "Portrait.png"); using (var image = new MagickImage(MagickColors.Gold, 20, 60)) image.Write(portrait);
        var portraitOutputs = ImageExportPackageEngine.Run([portrait], package, destination);
        var portraitSizes = new[] { (10u, 30u), (3u, 10u), (20u, 60u) };
        for (var index = 0; index < portraitOutputs.Count; index++)
        { using var tall = new MagickImage(portraitOutputs[index]); check((tall.Width, tall.Height) == portraitSizes[index], "Portrait package preserves the requested longest edge"); }
        check(new ImageExportVariant("png", 1000, "a").Dimensions(20, 60) == (20, 60), "Package never upscales");
        check(new ImageExportVariant("png", 30, "a").Dimensions(1, 10000).Width == 1, "Thin image retains a positive dimension");

        foreach (var atPublication in new[] { false, true })
        {
            var cancelled = Path.Combine(directory, atPublication ? "Cancel publication" : "Cancel encoding");
            using var cancellation = new CancellationTokenSource();
            var report = new ImmediateProgress(value => { if (atPublication ? value.Label == "Saving your files…" : value.Fraction > 0) cancellation.Cancel(); });
            try { ImageExportPackageEngine.Run([input], package, cancelled, report, cancellation.Token); throw new Exception("Cancelled package succeeded"); }
            catch (OperationCanceledException) { check(true, "Cancellation reaches package transaction"); }
            check(!Directory.EnumerateFileSystemEntries(cancelled).Any(), "Cancellation leaves no partial package or staging directory");
        }
        var corrupt = Path.Combine(directory, "Broken.png"); File.WriteAllText(corrupt, "not an image");
        var failed = Path.Combine(directory, "Failed");
        try { ImageExportPackageEngine.Run([input, corrupt], package, failed); throw new Exception("Corrupt package succeeded"); }
        catch (MagickException) { check(true, "A corrupt later input fails the whole package"); }
        check(!Directory.EnumerateFileSystemEntries(failed).Any(), "Earlier staged variants are removed after a later failure");

        foreach (var name in new[] { "../escape", "a/b", "a\\b", ".hidden", "CON", "COM1.txt", "NUL", "file:stream", "trailing.", " name", "a\n", new string('é', 41) })
        {
            Reject(() => (package with { Name = name }).Validate(), check);
            Reject(() => (package with { Variants = [new("png", 0, name)] }).Validate(), check);
        }
        Reject(() => (package with { Variants = [new("png", 0, "web"), new("jpg", 0, "WEB")] }).Validate(), check);
        foreach (var edge in new[] { -1, 16385 }) Reject(() => (package with { Variants = [new("png", edge, "a")] }).Validate(), check);
        Reject(() => (package with { Variants = [new("pdf", 1, "a")] }).Validate(), check);
        Reject(() => (package with { Variants = [] }).Validate(), check);
        Reject(() => package.Plan([]), check); Reject(() => package.Plan(["a.gif"]), check);
        Reject(() => package.Plan(Enumerable.Repeat(input, 1001).ToArray()), check);
        var longPlan = package.Plan([string.Concat(Enumerable.Repeat("猫🍐", 50)) + ".png", "CON.jpg", "a?b.png"]);
        check(longPlan.All(entry => Encoding.UTF8.GetByteCount(Path.GetFileName(entry.RelativePath).Normalize(NormalizationForm.FormD)) < 255), "Preview bounds long Unicode filenames");
        check(longPlan[3].RelativePath.EndsWith("Image.png", StringComparison.Ordinal) && longPlan[6].RelativePath.EndsWith("a_b.png", StringComparison.Ordinal), "Preview sanitizes reserved and nonportable stems");

        var preferences = Path.Combine(directory, "Preferences"); var store = new ImageExportPackageStore(preferences);
        store.Save(package); check(new ImageExportPackageStore(preferences).Packages.Single().Name == package.Name, "Saved packages survive reopening");
        store.Save(package with { Name = "PRODUCT SET" }); check(store.Packages.Count == 1, "Saving an existing package name replaces that package");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        File.WriteAllText(Path.Combine(preferences, "image-export-packages.json"), "[" + JsonSerializer.Serialize(package, json) + ",null,12,{\"name\":\"Broken\",\"variants\":[]}]");
        check(store.Packages.Count == 1, "One corrupt saved package does not discard valid packages");
        store.Remove("product set"); check(store.Packages.Count == 0, "Deleting a saved package removes only its definition");
        for (var i = 0; i < 50; i++) store.Save(package with { Name = "Set " + i });
        Reject(() => store.Save(package), check); store.Save(package with { Name = "Set 0" });
        check(store.Packages.Count == 50, "Package limit still permits replacing a saved name");
    }
    private static void Reject(Action action, Action<bool, string> check)
    {
        try { action(); throw new Exception("Invalid package accepted"); }
        catch (ArgumentException) { check(true, "Invalid package is rejected before export"); }
    }
    private sealed class ImmediateProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    { public void Report(OperationProgress value) => report(value); }
}
