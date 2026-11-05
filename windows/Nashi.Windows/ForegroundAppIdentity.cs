using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Nashi.Core;

namespace Nashi.Windows;

// Both global drag gestures and window arrangement use this foreground identity.
internal sealed class ForegroundAppIdentity
{
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder className, int capacity);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint window, ChildVisitor callback, nint param);
    private delegate bool ChildVisitor(nint child, nint param);

    private nint cachedWindow;
    private string? cachedName;
    private DateTimeOffset refreshAfter = DateTimeOffset.MinValue;

    internal bool IsExcluded(string? configuredNames)
    {
        if (string.IsNullOrWhiteSpace(configuredNames))
            return false;

        var foreground = GetForegroundWindow();
        var now = DateTimeOffset.UtcNow;
        if (foreground != cachedWindow || now >= refreshAfter || cachedName is null)
        {
            cachedWindow = foreground;
            cachedName = ProcessName(foreground);
            refreshAfter = now.AddSeconds(1);
        }
        return ShortcutAppFilter.IsExcluded(configuredNames, cachedName);
    }

    private static string? ProcessName(nint window)
    {
        if (window == 0 || GetWindowThreadProcessId(window, out var pid) == 0 || pid == 0)
            return null;

        var topClass = WindowClass(window);
        if (topClass == "ApplicationFrameWindow")
        {
            var children = new List<(string ClassName, int ProcessId)>();
            EnumChildWindows(window, (child, _) =>
            {
                var className = WindowClass(child);
                if (className == "Windows.UI.Core.CoreWindow" &&
                    GetWindowThreadProcessId(child, out var childPid) != 0 && childPid > 0)
                    children.Add((className, checked((int)childPid)));
                return true;
            }, 0);
            pid = (uint)HostedAppProcess.SelectProcessId(topClass, checked((int)pid), children);
        }

        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            return process.ProcessName;
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
        return null;
    }

    private static string WindowClass(nint window)
    {
        var name = new StringBuilder(128);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }
}
