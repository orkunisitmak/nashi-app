namespace Nashi.Core;

// An ApplicationFrameWindow belongs to the Windows host, not the hosted UWP application.
public static class HostedAppProcess
{
    public static int SelectProcessId(string? foregroundClass, int foregroundProcessId,
        IEnumerable<(string ClassName, int ProcessId)> children)
    {
        if (!string.Equals(foregroundClass, "ApplicationFrameWindow", StringComparison.Ordinal))
            return foregroundProcessId;
        foreach (var (className, processId) in children)
            if (className == "Windows.UI.Core.CoreWindow" && processId > 0)
                return processId;
        return foregroundProcessId;
    }
}
