using System.IO.Compression;
using Nashi.Core;

internal static class ArchiveSecurityChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows()) return;

        const string zoneStream = ":Zone.Identifier";
        const string internet = "[ZoneTransfer]\r\nZoneId=3\r\n";
        const string restricted = "[ZoneTransfer]\r\nZoneId=4\r\n";
        var zipPath = Path.Combine(root, "Internet archive.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("nested/Example.txt").Open())
            entry.Write("harmless example"u8);

        var ordinaryFolder = ArchiveEngine.Extract(zipPath, root)[0];
        check(!File.Exists(Path.Combine(ordinaryFolder, "nested", "Example.txt") + zoneStream),
            "Extracting an unmarked archive added an internet zone");

        File.WriteAllText(zipPath + zoneStream, internet + "HostUrl=https://example.com/sample.zip\r\n");
        var internetFolder = ArchiveEngine.Extract(zipPath, root)[0];
        check(File.ReadAllText(Path.Combine(internetFolder, "nested", "Example.txt") + zoneStream) == internet,
            "Extraction did not preserve the Internet zone on a nested file");

        var converted = ArchiveEngine.Convert(zipPath, "tar", root)[0];
        check(File.ReadAllText(converted + zoneStream) == internet,
            "Archive conversion lost the Internet zone");

        var gzipPath = Path.Combine(root, "Restricted example.txt.gz");
        using (var raw = File.Create(gzipPath))
        using (var gzip = new GZipStream(raw, CompressionLevel.Optimal))
            gzip.Write("harmless example"u8);

        File.WriteAllText(gzipPath + zoneStream, restricted);
        var restrictedFolder = ArchiveEngine.Extract(gzipPath, root)[0];
        check(File.ReadAllText(Path.Combine(restrictedFolder, "Restricted example.txt") + zoneStream) == restricted,
            "GZIP extraction lost the Restricted Sites zone");
    }
}
