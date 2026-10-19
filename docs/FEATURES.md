# Features and limits

The application is an independent Windows implementation, not a claim of complete feature or pixel parity with the Mac app.

| Area | Available | Requirements / limits |
| --- | --- | --- |
| Drag wheels | Shift conversions, Ctrl tools, Alt file actions; remapping; multiple files | Real OLE file paths are required. Virtual attachments, elevated drag sources and unusual multi-monitor DPI setups need wider testing. |
| Images | JPG/PNG/WebP/HEIC/AVIF/TIFF/BMP/PDF, SVG input; crop, compress, adjustments, shapes/text, backgrounds, collage | No OCR, freehand annotation, automatic face/text detection or target-file-size compression. Multi-page conversion may create a folder of outputs. |
| Audio/video | Formats offered in the catalog; trim, speed, snapshots, split/join, mute, normalize, channel gains, bleeps, visualization | Requires local FFmpeg/ffprobe. Silent video cannot produce an audio track. Preview playback also depends on Windows media components. |
| Redaction | Image/video fixed rectangle, solid or blur | Video rectangle applies to the whole clip; no tracking or per-range animated redactions. |
| PDF | 300-DPI page images; text/Markdown/EPUB, DOCX/PPTX/XLSX exports; page organization and merging | Scans without text cannot become selectable text without OCR. DOCX reflows extracted text or uses page images; PPTX contains slide images; XLSX extracts text rather than reconstructing tables. Compression is lossless and may not shrink optimized PDFs. |
| Office documents | Layout-aware Word/Excel/PowerPoint conversions | Corresponding Microsoft Office application required. CSV/TSV uses Excel's active-sheet behavior. In-flight Office cancellation waits for the call to return. Apple Pages output is unavailable. |
| EPUB / text | Text extraction, basic DOCX HTML/EPUB writing, SRT/VTT/TXT conversion | Markdown output is primarily text extraction, not complete structural reconstruction. |
| Metadata | Search, inspection and Remove All | No arbitrary field editing. Removing metadata may affect photo-specific effects. |
| Archives | ZIP/TAR/GZIP/RAR extraction and archive conversion | Links rejected; 20 GB / 100,000-entry extraction limits. Password-protected inputs unsupported. |
| Batch | Shared settings, per-file overrides, sequential processing, errors isolated per file | Dimensions can scale per file; times and page choices refer to each source. Completed outputs survive cancellation. |
| File actions | Copy, copy paths, Move, Windows Share, terminal, floating shelf | AirDrop is replaced by Windows Share. Move operates on originals. |
| Motion and sound | Short WPF transitions respect system animation/high-contrast settings; optional highlight audio | Public builds exclude the private reference sound. |

The catalog in `Tangerine/Models.cs` is the source of truth for menu offerings. Unsupported cases should report a clear error and preserve originals.
