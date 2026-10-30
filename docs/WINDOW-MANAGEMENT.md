# Window management

Nashi can arrange a window while its native title bar or resize border is being dragged. Tap **Shift or Command on macOS**, or **Shift or Alt on Windows**, move over a wheel slice and release the mouse. Releasing the modifier keeps the wheel open. Releasing over the center/outside the wheel leaves placement unchanged. **Escape** dismisses the wheel until the next gesture.

The layouts are:

| Size | Position | macOS shortcut | Windows shortcut |
| --- | --- | --- | --- |
| 1/3 width | Left, middle, right | ⌃⌥⌘←, ⌃⌥⌘↓, ⌃⌥⌘→ | Ctrl+Alt+Win+←, Ctrl+Alt+Win+↓, Ctrl+Alt+Win+→ |
| 1/2 width | Left, right | ⌥⌘←, ⌥⌘→ | Ctrl+Alt+←, Ctrl+Alt+→ |
| 1/2 height | Top, bottom | ⌥⌘↑, ⌥⌘↓ | Ctrl+Alt+↑, Ctrl+Alt+↓ |
| 2/3 width | Left, right | ⇧⌥⌘←, ⇧⌥⌘→ | Ctrl+Alt+Shift+←, Ctrl+Alt+Shift+→ |
| 100% | Maximize | ⌥⌘F | Ctrl+Alt+F |

Each layout also has a global keyboard shortcut that arranges the front window without the wheel; the defaults are listed above. Windows keeps Win+arrow for its own Snap, so Nashi uses Ctrl+Alt there. Because they are global, they take precedence over the same keys inside other apps (for example ⌥⌘←/→ browser tab switching on macOS). In **Settings → General → Window management**, press **Record** beside a layout and type a new combination to change it: it must include ⌘, ⌥ or ⌃ (Ctrl, Alt or Win on Windows) and cannot repeat another window shortcut or the copied-link download shortcut (⌃⇧D / Ctrl+Shift+D). **Delete** (Backspace on Windows) clears a layout's shortcut and **Escape** cancels recording. **Restore defaults** brings back the table above, and **Arrange the front window with keyboard shortcuts** turns them all off.

With more than one display, repeating a left or right layout continues onto the neighbouring display: with the window already on the right half, ⌥⌘→ (Ctrl+Alt+→) moves it to the left half of the display to the right, and pressing it again moves it to that display's right half. Thirds and two-thirds work the same way; the outermost display keeps the window in place. Centered, top/bottom and maximized layouts stay on the current display.

**Maximize** fills the usable desktop without entering a separate full-screen space. You can drag the window again to choose another arrangement.

The preview and placement use the destination display's usable work area. The menu bar, Dock and taskbar are excluded. Adjacent regions share rounded pixel boundaries, including odd-width and Retina displays; screens left of or above the primary display keep their negative coordinates.

## macOS

Enable the feature in **Settings → General → Window management**. Use **Open permission settings…** and grant Nashi the window-control permission once. On macOS 27 the Privacy & Security pane is named **Device Control and Data Access** (**Aygıt Denetimi ve Veri Erişimi** in Turkish); earlier versions call it **Accessibility**. The button opens that pane directly. The app checks permission without prompting repeatedly; it never changes macOS privacy settings automatically. Accessibility APIs move and resize the window that was captured at the beginning of the drag.

Upgraded installations retain the `local.tangerine.replica` bundle identity to preserve update and user-data compatibility. macOS may cache the previous Tangerine display name in its Accessibility list; the reference app (`com.th.Tangerine`) is a separate permission. Nashi checks its own `AXIsProcessTrusted()` result. If an old row is enabled but Nashi still reports no access, use **Show Nashi in Finder** to select the current installed app when adding it through macOS settings. Nashi does not reset or edit the privacy database.

The window must actually move or resize before the wheel can activate. Fresh file/web drag payloads, a visible file wheel, editable text controls, scrollbars, minimized windows, full-screen windows and non-resizable/dialog windows are excluded. The overlay does not activate Nashi or capture mouse events from the app being dragged. No window titles, typed content or accessibility trees are recorded.

Some apps enforce minimum dimensions or disallow programmatic resizing. A larger minimum keeps the chosen edge, and a window whose size cannot change is moved to the layout's edge at its own size. If the requested rectangle cannot be applied, Nashi reports it rather than claiming successful placement.

Keyboard shortcuts use the front app's focused window, falling back to its main window. Windows that apps report as dialogs or without a standard subrole (common in Java, Qt and Electron apps) are accepted; system dialogs and floating system panels are not. While a window is being placed, Nashi briefly turns off the app's enhanced accessibility mode (`AXEnhancedUserInterface`, used by Chromium/Electron apps and assistive tools), which otherwise animates every move, and restores it afterwards.

## Windows

The **Settings → General → Window management** checkbox enables/disables window management and persists outside the portable installation with the user's Nashi preferences. Native `MOVESIZESTART`/`MOVESIZEEND` events identify window gestures; no global keyboard hook or text monitoring is installed. The overlay is non-activating and passes pointer events through to the original app. DWM's invisible window borders are compensated so the visible frame aligns with the target area. Elevated/protected applications may reject resizing. A refused placement shows the same window-arrangement explanation as on macOS. Layout shortcuts are `RegisterHotKey` registrations on a message-only window; they report only their own combinations, are released while Settings records a new one, and act on the foreground window (a maximized window is restored first). The desktop, taskbar and Nashi's own windows are skipped.

## Verification

- Swift regression tests cover all ten layouts, display hopping across side-by-side and stacked displays, fixed-size anchoring, odd/Retina/negative screen coordinates, ordinary content drags, fresh-file precedence and Escape cancellation.
- The Windows core verifier covers the same partitions, top/bottom halves, display hopping, default/recorded/cleared/conflicting shortcuts and preference persistence.
- The explicit macOS `--window-management-smoke <manager-pid> <output-directory>` mode runs from the installed Nashi identity after access is granted. It admits only a manager running that same executable and a newly launched disposable Nashi fixture. It tests all ten physical Shift/Command drags, releasing the modifier before mouse-up, and Escape cancellation. Input is released on failure or interruption; the fixture closes and the pointer is restored. Missing access fails before posting any input.
- The Windows native smoke test launches a separate disposable Nashi fixture process, applies all ten layouts using real window APIs, applies layout shortcuts to the foreground fixture (a repeat on the single runner display stays in place), exercises real Shift/Alt title-bar drags, a resize-border drag and Escape, and captures the wheel. It does not move the user's other windows.
- macOS 0.7.6 (37) live verification passed ten checks in each of two fixture runs: ordinary windows and windows with a 1,000-point minimum width. Each run covered all eight layouts, including Maximize, a resize-border drag and Escape. Maximize filled the 1,920 × 963-point usable area at screen y = 30.
- The 0.7.5 macOS 27.0.1 baseline passed nine checks: all seven physical Shift/Command title-bar drags, a resize-border drag, modifier release before mouse-up, and Escape in the separate disposable fixture process. Visible work area was 1920 × 963 points, starting at screen y = 30. Permission remained enabled after replacing the locally signed app with the next build.
- Other display configurations are covered by geometry regressions; they have not all been exercised on physical displays. App-specific minimum sizes can still prevent a requested layout.
- `--hold-for-capture` adds a bounded six-second pause to the explicit smoke mode for visual QA. An explicit manager invocation with `--capture-window-wheel <directory>` captures its own live AppKit view once during selection. This does not record other windows or require Screen Recording access. The captured labels and fraction glyphs were inspected for clipping and alignment.

![Nashi window arrangement wheel](assets/showcase/mac-window-management-view.png)

The image is a live AppKit view render captured during a native drag. Desktop compositing is outside this image; real placement and dismissal are verified by the native gesture checks.
