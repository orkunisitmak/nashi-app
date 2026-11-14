# Local OCR options for Nashi

Research date: 2026-11-02. Tesseract text recognition is now implemented locally. OCRmyPDF, PaddleOCR and Granite remain proposals; this document is not an accuracy benchmark.

## Current implementation

**Tools → Read → OCR** opens local image/PDF recognition, English/Turkish language selection, automatic/block/sparse layouts, PDF page ranges and password entry. Existing PDF text is retained by default. Results can be reviewed, edited, copied or saved as fresh UTF-8 TXT files beside their sources. Scanned pages render at 300 DPI. Cancellation, per-page timeouts and input/page/text limits are enforced.

Tesseract is a system dependency; Tesseract 5 is recommended, with compatible Tesseract 4 LSTM installations also supported. Ubuntu/Debian: `sudo apt install tesseract-ocr tesseract-ocr-eng tesseract-ocr-tur`. The editor lists installed recognition languages, including English/Turkish packs and shows installation instructions when missing. It does not install packages automatically.

The initial output is reviewed TXT. Searchable PDF generation, handwriting-specific models and reconstruction of table structure are not implemented. Redact Photo now offers local OCR word regions with confidence filtering and manual review. PDF → TXT/Markdown and editable document conversions automatically recognize pages without text; existing digital text requires no Tesseract executable. Multi-frame image files currently use their first image; PDFs support selected pages.

## Recommendation

Start with **Tesseract 5** for image-to-text and text-region detection. Add **OCRmyPDF** when preserving a scanned PDF while adding a searchable text layer. Neither requires a cloud account, paid API or generative language model. Modern Tesseract itself uses trained neural OCR models, so describing this as strictly “no AI” would be inaccurate.

| Option | Role | Integration tradeoff |
| --- | --- | --- |
| Tesseract 5 | Printed text recognition, word boxes/confidence and text/searchable-PDF output | Suitable starting point for an offline desktop utility. Language data and page segmentation must be configured. |
| OCRmyPDF | Adds a searchable text layer to scanned or mixed PDFs using Tesseract | PDF workflow rather than a competing recognition engine; additional Python/runtime dependencies. |
| PaddleOCR | Multilingual text detection and recognition; its Latin recognizer includes Turkish | Candidate for a later comparison on difficult photographed text; adds model/runtime packaging work. No accuracy advantage is claimed without testing. |
| Granite-Docling-258M | A vision-language model for structured document conversion, including tables and formulas | An optional advanced document-understanding path, rather than the simplest OCR dependency. Its model card lists English; Turkish requires evaluation. |

## Verification

The local targeted suite passed 23 OCR checks, including real image and scanned-PDF recognition, Turkish letters, cancellation, page selection, preservation of existing PDF text, reviewed text export, multi-file edit retention and copying through the isolated headless clipboard. Real local recognition used Ubuntu 22.04’s Tesseract 4.1.1 compatibility path, extracted into temporary storage without system installation. The Ubuntu 24.04 workflow installs distribution Tesseract and English/Turkish packs for hosted verification; that workflow change has not yet run on GitHub.

```sh
NASHI_VERIFY_TESSERACT=/usr/bin/tesseract dotnet run --project linux/Nashi.Linux.Checks -c Release -- --ocr
```

`NASHI_VERIFY_TESSDATA` can override the language-data directory for tests. The production editor uses Tesseract’s normal system language-data location.

## A useful first OCR workflow

- **Recognize Text** on images or selected PDF pages, with English/Turkish language selection.
- Fast and accurate modes using `tessdata_fast` / `tessdata_best` respectively. These are relative model choices, not promises about a user's files.
- Preview editable recognized text; copy it or save UTF-8 TXT. Markdown export should preserve text honestly rather than invent table structure.
- Preserve existing PDF text; OCR scanned pages as needed. Use OCRmyPDF for a searchable PDF instead of rasterizing every original page into a replacement document.
- Provide optional rotation/deskew and image preprocessing, cancellation, bounded page/image sizes and fresh outputs beside the sources.
- Word boxes/confidence are exposed through the “Find text regions” redaction tool. Do not automatically redact content without reviewing the detected regions.

Do not promise automatic language identification, reliable handwriting recognition or reconstructed spreadsheets in the initial feature.

## Primary sources

- [Tesseract usage](https://tesseract-ocr.github.io/tessdoc/Command-Line-Usage.html): languages, neural/legacy engines, text/PDF/hOCR/TSV output.
- [Tesseract language data](https://tesseract-ocr.github.io/tessdoc/Data-Files.html): fast/best tradeoffs.
- [Tesseract quality guidance](https://tesseract-ocr.github.io/tessdoc/ImproveQuality.html): preprocessing and skew.
- [OCRmyPDF introduction](https://ocrmypdf.readthedocs.io/en/latest/introduction.html): text-layer preservation and mixed scanned/digital PDFs.
- [PaddleOCR multilingual models](https://www.paddleocr.ai/latest/en/version3.x/algorithm/PP-OCRv5/PP-OCRv5_multi_languages.html): Turkish support in the Latin recognizer.
- [IBM Granite Docling model card](https://huggingface.co/ibm-granite/granite-docling-258M): architecture, declared language and document-conversion capabilities.
