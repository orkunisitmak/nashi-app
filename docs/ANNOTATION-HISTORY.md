# Annotation undo and redo (macOS)

Annotate Image keeps up to 100 edits in the current editor session. Undo and Redo
cover added marks, pointer moves/resizes, Delete and Clear. One continuous drag
is one edit. A new edit after Undo discards the redo branch. Reset starts a fresh
history; closing the editor ends the session.

Use the Undo/Redo buttons or Command-Z / Command-Shift-Z. Focused text fields
retain their own text-editing undo. Export uses the currently displayed marks
and keeps the original file unchanged. This history does not apply to the other
image tools or the Windows editor.
