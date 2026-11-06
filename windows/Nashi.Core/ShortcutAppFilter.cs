namespace Nashi.Core;

// Only exact executable process names are matched: no window titles, paths or wildcards.
public static class ShortcutAppFilter
{
    public static bool IsExcluded(string? configuredNames, string? processName)
    {
        if (string.IsNullOrWhiteSpace(configuredNames) || string.IsNullOrWhiteSpace(processName))
            return false;
        foreach (var name in configuredNames.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var executableName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
            if (string.Equals(executableName, processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
