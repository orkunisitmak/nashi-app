using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Nashi.Windows;

public sealed partial class MainWindow
{
    internal const int IslandCollapseDelayMilliseconds = 250;
    private DispatcherTimer? islandCollapseTimer;
    private bool islandDragActive;

    private void AttachIslandPointerInteractions(FrameworkElement surface)
    {
        surface.AllowDrop = true;
        surface.MouseEnter += (_, _) =>
        {
            islandCollapseTimer?.Stop();
            if (!islandExpanded && preferences.Value.PillVisibilityMode != "shortcut") SetIslandExpanded(true);
        };
        surface.MouseLeave += (_, _) => { if(preferences.Value.PillVisibilityMode!="shortcut")ScheduleIslandCollapse(); };
        surface.LostKeyboardFocus += (_, _) => { if(preferences.Value.PillVisibilityMode!="shortcut")ScheduleIslandCollapse(); };
        surface.DragEnter += (_, e) =>
        {
            if (busy || !SupportsIslandDrop(e.Data)) return;
            islandDragActive = true;
            islandCollapseTimer?.Stop();
            if (!islandExpanded) SetIslandExpanded(true);
            if (islandHint is not null) islandHint.Text = IslandText("Drop to add files", "Dosya eklemek için bırak");
            e.Handled = true;
        };
        surface.DragLeave += (_, _) =>
        {
            islandDragActive = false;
            ScheduleIslandCollapse();
        };
        surface.Drop += (_, _) =>
        {
            islandDragActive = false;
            ScheduleIslandCollapse();
        };
        Closed += (_, _) => islandCollapseTimer?.Stop();
    }

    private static bool SupportsIslandDrop(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.UnicodeText) ||
        data.GetDataPresent(DataFormats.Html) || data.GetDataPresent("UniformResourceLocatorW");

    private void ScheduleIslandCollapse()
    {
        if (!islandExpanded) return;
        islandCollapseTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(IslandCollapseDelayMilliseconds)
        };
        islandCollapseTimer.Stop();
        islandCollapseTimer.Tick -= CollapseIfPointerLeft;
        islandCollapseTimer.Tick += CollapseIfPointerLeft;
        islandCollapseTimer.Start();
    }

    private void CollapseIfPointerLeft(object? sender, EventArgs e)
    {
        islandCollapseTimer?.Stop();
        if (!ShouldCollapseIsland(islandPanel?.IsMouseOver == true || islandPill?.IsMouseOver == true,
                islandDragActive,
                islandActionPopup is { IsOpen: true } action && action.IsMouseOver,
                islandMediaPopup is { IsOpen: true } media && media.IsMouseOver,
                islandPanel?.IsKeyboardFocusWithin == true && Keyboard.FocusedElement is TextBoxBase)) return;
        SetIslandExpanded(false);
    }

    internal static bool ShouldCollapseIsland(bool pointerOverSurface, bool dragging,
        bool actionPopupHovered, bool mediaPopupHovered, bool editorFocused = false) =>
        !pointerOverSurface && !dragging && !actionPopupHovered && !mediaPopupHovered && !editorFocused;
}
