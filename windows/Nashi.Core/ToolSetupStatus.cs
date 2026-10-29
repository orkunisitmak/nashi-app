namespace Nashi.Core;

public enum ToolGroup { Media, Office }
public sealed record ToolSetupStatus(bool MediaReady, bool OfficeReady)
{
    public bool Ready => MediaReady && OfficeReady;
    public ToolGroup[] Missing => Enum.GetValues<ToolGroup>().Where(g => g == ToolGroup.Media ? !MediaReady : !OfficeReady).ToArray();
}

public static class ToolInstallation
{
    public static bool UsableFile(string path) => File.Exists(path) && new FileInfo(path).Length > 0;
    public static void Publish(string stage, string target, Func<string, bool> complete)
    {
        if (!complete(stage)) throw new InvalidDataException("The downloaded tools are incomplete. Please try setup again.");
        if (complete(target)) return;
        var backup = Path.Combine(Path.GetDirectoryName(target)!, ".previous-" + Guid.NewGuid().ToString("N"));
        var existing = Directory.Exists(target);
        if (existing) MoveDirectory(target, backup);
        try { MoveDirectory(stage, target); }
        catch { if (existing) MoveDirectory(backup, target); throw; }
        if (existing) TryDeleteDirectory(backup);
    }

    // Antivirus scanners, Search indexing and OneDrive briefly hold freshly written
    // executables open on Windows, which makes an immediate rename fail with a
    // sharing violation. Retry for a few seconds before reporting the failure.
    public static void MoveDirectory(string source, string destination, int attempts = 20)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { Directory.Move(source, destination); return; }
            catch (Exception error) when (attempt < attempts && error is IOException or UnauthorizedAccessException && Directory.Exists(source) && !Directory.Exists(destination))
            { Thread.Sleep(Math.Min(1000, 100 * attempt)); }
        }
    }

    public static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
