# PDF workspace (macOS)

Open **Organize PDF** for a PDF file. Original files remain unchanged; **Save organized PDF** writes a new copy.

- Click a page to select it. Command-click toggles individual pages; Shift-click selects a range.
- Command-A selects all pages. Left/Right changes the focused page; Shift-Left/Right extends the selection.
- Drag a selected page to another card to insert the selected pages before it. The end target moves them to the end. Selected pages retain their relative order.
- Delete and forward Delete remove selected pages as one undoable action. At least one page must remain.
- Rotate, Duplicate, Move Left and Move Right apply to the selection. Copies have independent identities, rotation and crop settings.
- Command-Z undoes an edit; Command-Shift-Z redoes it. The toolbar also exposes Undo/Redo. Text fields retain their normal editing shortcuts.
- Add PDF inserts the selected document's pages after the last selected page. Add blank page inserts a blank page of the selected page's unrotated size (A4 when nothing is selected).
- Extract selected pages exports a new PDF in workspace order without changing the workspace or clearing its unsaved status.
- Crop selected pages accepts left/right/top/bottom margins in PDF points (72 points = 1 inch), relative to each current crop box, before rotation. Every selected page must retain a visible area. Reset crop restores its imported crop box. Cropping hides content; it is not permanent redaction.
- Page width can keep original sizes, match the narrowest or widest displayed page, use A4 width (595.276 points), or use a custom width. Heights scale proportionally. Apply width commits a custom value. Windows' existing uniform-width option also uses the narrowest page.

Edits show an unsaved indicator. Closing an unsaved workspace asks whether to keep editing or discard changes. A successful full save marks the current workspace as saved; extraction does not. Undo/redo retains up to 100 edits for the current session. Reset starts a new session from the original input.

The existing preserving PDF writer remains responsible for forms, attachments, page references and unsupported structures. Rich PDFs are refused for width scaling, and unsupported page operations fail without publishing a partial copy. Digital signatures and document structures that cannot be safely preserved are not flattened silently. Existing page-editing file/page limits still apply.

## Verification

On a Mac with Xcode installed:

```sh
swift build --product Nashi
swift test --filter 'PDFPageEditorStateTests|PDFWorkspaceTests|PDFPreservationRegressionTests|PDFEngineTests|PDFSafetyAuditTests|PDFCompressCancellationCleanupTests|PDFSplitCancellationCleanupTests'
python3 scripts/test-native-ui.py
```

The native UI runner includes the own-window `--pdf-workspace-smoke` entry point for production shortcut decoding/routing, text-field protection, multi-page deletion, undo/redo and close interception. It requires no global keyboard injection or accessibility permission.

Windows' existing verification executable checks that mixed page widths normalize to the narrowest page:

```sh
dotnet run --project windows/Nashi.Verify -c Release
```
