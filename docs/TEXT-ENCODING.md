# Text input encodings (macOS core and CLI)

Automatic detection remains the default. For a legacy subtitle received from a
Turkish Windows system, choose its known encoding explicitly:

```sh
nashi-cli convert vtt Captions.srt --options '{"textEncoding":"windows-1254"}'
```

`textEncoding` accepts `auto`, `utf-8`, `windows-1252`, `windows-1254`, or
`mac-roman`. It applies to SRT/VTT and TXT/Markdown imports. Outputs are fresh
files; subtitle exports use UTF-8. Unsupported names or undecodable input fail
without publishing an output.

Legacy code pages cannot reliably be distinguished from their bytes alone.
Choose the encoding used to save the source; selecting Turkish for every legacy
file could change Western European characters. This option does not override
CSV/TSV or Office package import settings. Windows keeps its existing system
code-page handling. The native macOS interface currently uses automatic detection.
