namespace Nashi.Windows;

internal static class IslandInteractionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(MainWindow.IslandCollapseDelayMilliseconds == 250,
            "The island hover collapse delay changed from its intended 250 ms.");
        check(MainWindow.ShouldCollapseIsland(false, false, false, false),
            "The island did not collapse after the pointer left all surfaces.");
        check(!MainWindow.ShouldCollapseIsland(true, false, false, false),
            "The island collapsed while the pointer was still over it.");
        check(!MainWindow.ShouldCollapseIsland(false, true, false, false),
            "The island collapsed while a file drag was active.");
        check(!MainWindow.ShouldCollapseIsland(false, false, true, false),
            "The island collapsed while its action popover was being used.");
        check(!MainWindow.ShouldCollapseIsland(false, false, false, true),
            "The island collapsed while its media popover was being used.");
        check(!MainWindow.ShouldCollapseIsland(false, false, false, false, editorFocused: true),
            "The island collapsed while a text editor inside it still had keyboard focus.");
        var collapsed=PillGeometry.Create(PillGeometry.CollapsedWidth,PillGeometry.CollapsedHeight);
        var expanded=PillGeometry.Create(PillGeometry.ExpandedWidth,PillGeometry.ExpandedHeight);
        check(Math.Abs(collapsed.Bounds.Width-PillGeometry.CollapsedWidth)<.01&&Math.Abs(collapsed.Bounds.Height-PillGeometry.CollapsedHeight)<.01,
            "The collapsed pill geometry does not match its layout bounds.");
        check(Math.Abs(expanded.Bounds.Width-PillGeometry.ExpandedWidth)<.01&&Math.Abs(expanded.Bounds.Height-PillGeometry.ExpandedHeight)<.01,
            "The expanded pill geometry does not match its layout bounds.");
        check(PillGeometry.TryParseHotkey("Ctrl+Alt+N",out var modifiers,out var key)&&modifiers==3&&key==0x4E,
            "The default pill hotkey could not be parsed.");
        check(!PillGeometry.TryParseHotkey("Ctrl+N",out _,out _),
            "An unsupported pill hotkey was accepted.");
    }
}
