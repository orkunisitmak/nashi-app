# Create QR Code

Choose Create QR Code on Home on either desktop. Enter text or a URL, choose a
256/512/1024-pixel image and L/M/Q/H error correction, then save a PNG. macOS
updates the preview while editing; Windows uses Generate QR Code and invalidates
the preview after an input change. The tool works locally with no upload or
external runtime download.

The source text is preserved as UTF-8. Input is limited to 2,000 UTF-8 bytes; the
selected correction level may have a lower capacity. A code that cannot fit the
chosen pixel size is refused. Modules use whole pixels and a white quiet zone.
Exports are staged and receive unique names when a destination already exists.
Saved PNGs appear in Library. Text is not saved as a separate history field.

This is a standalone creation tool because its input is text rather than a file;
it does not add a wheel item or change the file-operation catalog. Wi-Fi/vCard
forms, SVG output and browser-playground generation are outside this first scope.
