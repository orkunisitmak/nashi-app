# PDF → Word

Converting a PDF to DOCX rebuilds an editable document and lays the page's own
picture behind it, so the result both edits like a Word file and looks like the
PDF.

## What becomes what

| In the PDF | In Word |
| --- | --- |
| Text | Editable paragraphs with the source font, size, weight, slant, colour, letter spacing and alignment. Justified and centred paragraphs keep their source line breaks and their own column, so line counts never change. |
| Headings, bullet and numbered lines | Separate paragraphs (never merged into the text around them). |
| Lines repeated at the top/bottom of most pages | The Word header and footer. Page numbers become `PAGE` / `NUMPAGES` fields; header/footer graphics (logo, rules) sit behind them in the header/footer. |
| Aligned rows of cells | Word tables. Ruled tables get real Word borders, column widths and row heights read from the rules, and cell shading. |
| Side-by-side blocks (letterhead vs. invoice box, two signature blocks, newspaper columns) | One borderless layout row per band, one cell per column (an XY-cut of the page). Stacked bands with the same columns (receipts, forms) become one table, one row per band; text that wraps inside a cell keeps its source line breaks. |
| Framed sections and shaded bands (receipts, forms, statements) | One Word table per box: the frame becomes the cell borders, every band its own row with the band colour as cell shading, separator lines become row borders, a white margin between frame and band stays an unshaded side column, and the content shares columns across rows. Rows grow with their text, so text can never drift off its band in Word, Google Docs or Pages. |
| Placed photos and screenshots | Separate pictures at their exact position. |
| Everything else — page colour, rules, charts, logos, signatures, stamps, seals, watermarks | The page artwork: the page rendered with the editable text lifted out, anchored behind the text. |
| Text set at an angle (seal rings, stamps, diagonal watermarks) | Stays in the artwork instead of turning into stray letters. |
| Scanned pages and pictures of text | On-device OCR with automatic language detection (Turkish, English, Russian, Ukrainian, German, French, Spanish, Italian, Portuguese). A page that is mostly Cyrillic is read a second time with Russian/Ukrainian first, so letters are not turned into Latin look-alikes. The recognised lines become editable text in their own ink colour, and the scan without that text becomes the artwork. |
| Phone photos of a document (paper on a desk, tilted or in perspective) | The sheet is found, cut out and flattened before OCR, like a scanner app: the Word page has the paper's proportions (842pt on its long side), lines are horizontal and the desk is gone. Born-digital pages, flatbed scans and "keep pages as pictures" are left exactly as they are. |

Glyph positions come from PDFKit's per-character bounds, not from selection
boxes: PDFKit can join lines from neighbouring cells into one "line" (a
wrapped IBAN beside a vertically centred label) and report that line's box for
every glyph, which would interleave the two lines' characters.

Lifting text out of the artwork removes only pixels that match the glyph's ink
blended with its local background, then refills them from the clean pixels on
either side. A red seal or blue signature drawn over a line of text therefore
survives intact, and a watermark under the text stays continuous.

Boxes are read from the rendered page: one-colour runs stacked across pixel
rows give rules (thin) and fills (tall); a frame is a top and bottom rule
joined by vertical rules. Rounded shapes, boxes over pictures, boxes with
anything else drawn inside (a logo, a stamp), scans and pages under 300pt stay
in the artwork.

Font sizes are rounded down to Word's half-point steps (8.75pt → 8.5pt, not
9pt), so a line that filled its source width never wraps onto two. Vertical
gaps before tables are paragraph space-before, which Google Docs keeps (it
drops exact line heights on empty lines).

LibreOffice runs headless with fontconfig, which on its own sees only the
fonts bundled with LibreOffice. UForge points it at the macOS font folders
too, so every conversion through LibreOffice (PDF → Word, Word → PDF, …) uses
the document's real fonts — Georgia, Verdana, Calibri, Palatino — instead of
look-alikes with other widths. The fidelity runner does the same.

LibreOffice writes DOCX with the strict OOXML side names (`w:start`/`w:end`);
UForge rewrites them to `left`/`right`, which Google Docs and older Word
versions need to keep cell margins, indents and right-aligned amounts.

Pages export uses the same reconstruction without the artwork layer (Pages has
no page-anchored background); light text is then darkened to stay readable.

## Checking fidelity

`scripts/pdf-fidelity/run.sh` generates a corpus — report, contract, invoice,
bank receipt, scanned and mixed documents and an eight-family typography
sample (serif, sans, monospace; bold, italic, colour, five sizes, line
spacing, justified, centred and letter-spaced text) covering every case above, with
fictitious data — converts each to DOCX
with UForge's engine, re-exports the DOCX to PDF and writes, per page:

- a side-by-side image (original | Word) with a visual similarity score, and
- a drift report: how many points each line of text moved, and whether every
  run of digit-bearing tokens (IBANs, amounts, dates, reference numbers) of
  each source line appears, in order, in the output.

Output lands in `build/pdf-fidelity/compare/`. Unit tests for the individual
behaviours are in `Tests/NashiCoreTests/PDFLayoutReconstructionTests.swift`.
