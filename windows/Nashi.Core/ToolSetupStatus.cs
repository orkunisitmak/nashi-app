using System.Runtime.InteropServices;

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
    /// Linux can restore or copy a tool folder without the executable bit.
    public static bool CanExecute(string path) => OperatingSystem.IsWindows() || access(path, 1) == 0;
    [DllImport("libc", SetLastError = true)] private static extern int access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
    /// Linux does not keep the executable bit when a download is moved or unpacked.
    public static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
    public static void Publish(string stage, string target, Func<string, bool> complete)
    {
        if (!complete(stage)) throw new InvalidDataException("The downloaded tools are incomplete. Please try setup again.");
        if (complete(target)) return;
        var backup = Path.Combine(Path.GetDirectoryName(target)!, ".previous-" + Guid.NewGuid().ToString("N"));
        var existing = Directory.Exists(target);
        if (existing) Directory.Move(target, backup);
        try { Directory.Move(stage, target); }
        catch { if (existing) Directory.Move(backup, target); throw; }
        if (existing) { try { Directory.Delete(backup, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
