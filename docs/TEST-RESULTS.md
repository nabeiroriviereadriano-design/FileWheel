# FileWheel — automated test results

Run on 2026-10-29 22:06. Environment: Ubuntu 24.04.5 LTS, .NET 10.0.13. Engines: FFmpeg present, FFprobe present, ImageMagick present, 7-Zip present, PDFium present, LibreOffice present, Calibre (ebook-convert) present.

Test names are declared in French in the code (`tests/FileWheel.Tests`); this report is their English translation. Sample file names are kept as is (they deliberately contain accents and spaces).

The Windows version is also compiled on a real Windows machine by GitHub Actions on every change.

**204 passed, 0 failed, 10 not run** out of 214.

## Wheel — trigger (mouse + keyboard)

| Test | Result | Details |
|---|---|---|
| Alt + drag in File Explorer shows the wheel | OK |  |
| Also works on the Desktop | OK |  |
| No trigger without the key | OK |  |
| No trigger for a simple click (no movement) | OK |  |
| No trigger for pressing Alt on its own | OK |  |
| No trigger outside File Explorer/Desktop | OK |  |
| The appearance delay is respected | OK |  |
| Exact combination: Ctrl+Alt does not trigger an "Alt" combination | OK |  |
| AltGr (left Ctrl + right Alt) is not taken for Alt | OK |  |
| Configurable combination: Ctrl+Shift | OK |  |
| Coexistence with Windows shortcuts: Alt+Tab before display cancels arming | OK |  |
| Synthetic codes (0xE8, 0xFF, 0x07, VK_PACKET) during the gesture: ignored, the wheel shows | OK |  |
| Coexistence with Windows shortcuts: Alt+Tab with wheel shown => the wheel disappears | OK |  |
| Esc closes the displayed wheel | OK |  |
| Key released too early: no wheel | OK |  |
| Key released after display: the wheel stays (default) and Alt is suppressed | OK |  |
| "Close when the key is released" option | OK |  |
| Mouse released outside the wheel: closes (the native drop happens) | OK |  |
| Item check: empty area (rectangle selection) => no wheel | OK |  |
| Item check: tolerant mode after a delay without answer | OK |  |
| Temporary disabling | OK |  |
| Desktop can be disabled separately | OK |  |
| Invalid combinations refused, with warnings | OK |  |

## Wheel — multiple screens and scale factors

| Test | Result | Details |
|---|---|---|
| 100 %: 320 px diameter, centered on the cursor | OK |  |
| 200 % (4K screen): physical diameter doubled | OK |  |
| 125 % on a screen to the left (negative coordinates) | OK |  |
| Screen edge: the wheel is shifted to stay visible (taskbar excluded) | OK |  |
| Configurable offset applied at the screen's scale | OK |  |
| Cursor between two screens: nearest screen | OK |  |

## Wheel — geometry and navigation

| Test | Result | Details |
|---|---|---|
| Hit test: center, sectors, outside | OK |  |
| Hovering a branch: submenu opens during the drag | OK |  |
| Drop on a leaf: runs | OK |  |
| Drop on a branch: the wheel stays open in click mode | OK |  |
| Drop on the center or Esc: cancel | OK |  |
| Unavailable action: never run, reason shown | OK |  |
| Destructive action flagged (confirmation required), outside the main menu | OK |  |

## Wheel — adapting to the file type

| Test | Result | Details |
|---|---|---|
| MP4: video conversions and audio extraction highlighted | OK |  |
| DOCX: PDF highlighted | OK |  |
| Image: image formats and resizing | OK |  |
| Archive: extraction and recompression | OK |  |
| Disguised program: conversion disabled | OK |  |
| Multiple selection: adapted labels and actions | OK |  |
| No more than 10 sectors per ring | OK |  |

## Express wheel (formats and tools directly in the wheel)

| Test | Result | Details |
|---|---|---|
| Default: Express layout | OK |  |
| Convertible file (video): target formats in large type, then Other formats and File | OK |  |
| "File" contains the classic actions (copy, move, rename, recycle bin, more) | OK |  |
| Image: tools (crop, background, compress, annotate, info) + Convert + File | OK |  |
| Several images: only batch tools stay enabled | OK |  |
| Folder, archive, program: classic layout | OK |  |
| Preview of the Express wheels (log) | OK |  |
| Short sector labels (12 characters at most) | OK |  |

## Drag and drop — safety

| Test | Result | Details |
|---|---|---|
| The wheel never answers "Move" to the OLE source | OK |  |

## File operations

| Test | Result | Details |
|---|---|---|
| Windows names: forbidden characters, reserved names, trailing dots | OK |  |
| Archive paths: "zip slip" blocked | OK |  |
| Unique names "(2)", "(3)" and double extensions | OK |  |
| Copy: conflict => rename, original and destination untouched | OK |  |
| Copy: "Skip" and "Ask" policies (apply to all) | OK |  |
| Move: the source disappears only after success | OK |  |
| Moving a folder into itself refused | OK |  |
| Recursive folder copy (accents, spaces) | OK |  |
| Cancel during a large copy: no partial file | OK |  |
| Large file (1 GiB) copied and verified | OK |  |
| Not enough disk space: refused before starting | OK |  |
| Rename: validation, conflict, case-only change | OK |  |
| Batch rename with {nom} and {n:000} pattern | OK |  |
| Permanent delete: missing items reported, no exception | OK |  |

## Custom actions — security

| Test | Result | Details |
|---|---|---|
| Arguments: one token = one argument, no shell interpretation | OK |  |
| Malformed pattern refused; relative program refused | OK |  |
| Program modified since it was saved: detected | OK |  |
| Command interpreter flagged | OK |  |
| Extension and folder filter | OK |  |

## Settings and storage

| Test | Result | Details |
|---|---|---|
| Corrupted settings file: defaults + backup copy | OK |  |
| Out-of-range values corrected | OK |  |
| Atomic save then re-read | OK |  |

## Format detection

| Test | Result | Details |
|---|---|---|
| photo exif.jpg -> jpg | OK |  |
| logo transparent.png -> png | OK |  |
| anime.gif -> gif | OK |  |
| dégradé.bmp -> bmp | OK |  |
| vert.tiff -> tiff | OK |  |
| cercle.webp -> webp | OK |  |
| clip vidéo.mp4 -> mp4 | OK |  |
| film.mkv -> mkv | OK |  |
| ancien.avi -> avi | OK |  |
| tonalité.wav -> wav | OK |  |
| chanson.mp3 -> mp3 | OK |  |
| piste.flac -> flac | OK |  |
| photos été.zip -> zip | OK |  |
| liens.tar.gz -> tgz | OK |  |
| livre.epub -> epub | OK |  |
| rapport annuel.docx -> docx | OK |  |
| présentation.pptx -> pptx | OK |  |
| rapport.pdf -> pdf | OK |  |
| cube.stl -> stl | OK |  |
| police.ttf -> ttf | OK |  |
| données.json -> json | OK |  |
| config.yaml -> yaml | OK |  |
| inventaire.csv -> csv | OK |  |
| catalogue.xml -> xml | OK |  |
| lisez-moi.md -> md | OK |  |
| page.html -> html | OK |  |
| message.eml -> eml | OK |  |
| contacts.vcf -> vcf | OK |  |
| agenda.ics -> ics | OK |  |
| boite.mbox -> mbox | OK |  |
| Misleading extension: PNG named .jpg => real content + warning | OK |  |
| File without extension identified by signature | OK |  |
| Program disguised as an image: flagged as executable | OK |  |
| Empty file and inconsistent content | OK |  |
| Truncated PDF flagged | OK |  |
| Reading limited to the header (2 GiB file detected instantly) | OK |  |

## Detected engines

| Test | Result | Details |
|---|---|---|
| External engine detection | OK |  |
| Missing engine: capability shown "to install", never available | OK |  |

## Images (ImageMagick)

| Test | Result | Details |
|---|---|---|
| JPEG -> PNG: dimensions kept, lossless | OK |  |
| EXIF orientation applied (120×80 rotated => 80×120) | OK |  |
| Metadata: kept by default, removed on request (GPS) | OK |  |
| Transparent PNG -> JPEG: warning + white background | OK |  |
| Transparent PNG -> WebP: transparency kept | OK |  |
| Resize "width 60" keeping the ratio | OK |  |
| Animated GIF -> WebP: animation kept; -> PNG: 1st frame + warning | OK |  |
| Image -> multi-size ICO | OK |  |
| BMP -> TIFF -> PNG (round trip) | OK |  |
| SVG: external references refused (security) | OK |  |
| Damaged image: clear error, no crash | OK |  |

## PDF

| Test | Result | Details |
|---|---|---|
| JPEG + transparent PNG images -> one PDF (built-in, no recompression) | OK |  |
| Interlaced (Adam7) and 16-bit PNG decoded by the built-in engine | OK |  |
| Accented TXT -> built-in PDF, text re-read identically (pdftotext) | OK |  |
| PDF -> PNG pages (PDFium): one image per page, dimensions at 100 dpi | OK |  |
| PDF -> TXT (PDFium): text extracted | OK |  |
| PDF: page range and non-existent pages | OK |  |
| Damaged PDF: clear error | OK |  |

## Documents (LibreOffice)

| Test | Result | Details |
|---|---|---|
| DOCX -> PDF: text present | OK |  |
| DOCX -> ODT -> DOCX | OK |  |
| PPTX -> PDF then slides as PNG (3 images) | OK |  |
| Markdown -> PDF (built-in rendering + LibreOffice) | OK |  |
| XLSX (built-in) -> PDF (LibreOffice) | OK |  |
| PDF -> DOCX: approximate reconstruction announced | OK |  |

## Video (FFmpeg)

| Test | Result | Details |
|---|---|---|
| MKV (H.264/AAC + subtitles) -> MP4: lossless remux, subtitles converted | OK |  |
| MP4 -> WebM: VP9/Opus re-encode announced as lossy | OK |  |
| AVI (MPEG-4) -> MP4: H.264 re-encode, 120p resolution | OK |  |
| Trim 1 s -> 3 s and audio removal | OK |  |
| Video -> animated GIF | OK |  |
| JPEG thumbnail and image sequence (1 per second) | OK |  |
| Damaged (truncated) video: clear error, no output | OK |  |
| Cancelling a long conversion: process stopped, temporary file deleted | OK |  |
| Impossible copy explicitly requested: explicit refusal | OK |  |

## Audio (FFmpeg)

| Test | Result | Details |
|---|---|---|
| Video -> MP3 (audio extraction) | OK |  |
| Video -> M4A: AAC track copied without re-encoding | OK |  |
| MP3 -> FLAC: "does not restore quality" warning, cover art and title kept | OK |  |
| WAV -> Opus voice profile: mono | OK |  |
| Normalization, 44.1 kHz and trimming | OK |  |
| Merging 3 audio files into one | OK |  |

## Data

| Test | Result | Details |
|---|---|---|
| Nested JSON -> flattened CSV (semicolon): "parent.child" columns | OK |  |
| Nested JSON -> CSV in "refuse" mode: explicit error | OK |  |
| CSV (semicolon, quotes, multi-line, accents) -> typed JSON | OK |  |
| CSV -> XLSX (built-in) -> CSV: identical round trip; XLSX validated by LibreOffice | OK |  |
| XML -> JSON (@ attributes, repeated elements as arrays) -> XML | OK |  |
| XML with DTD / external entity: refused (XXE) | OK |  |
| Invalid JSON: error with line number | OK |  |
| YAML -> JSON: compared with PyYAML (reference) | OK |  |
| JSON -> YAML re-read identically by PyYAML | OK |  |
| YAML with anchors: explicit refusal | OK |  |
| CSV -> XLSX: formula injection neutralized in CSV output | OK |  |

## Text, Markdown, HTML, books

| Test | Result | Details |
|---|---|---|
| Markdown -> HTML: headings, tasks, table, code; script and javascript: neutralized | OK |  |
| HTML -> TXT: scripts and styles ignored, lists and entities | OK |  |
| EPUB -> HTML (built-in): chapters, image included, script removed | OK |  |
| Protected EPUB (DRM): refused | OK |  |
| EPUB -> AZW3 and -> PDF (Calibre) | OK |  |

## 3D, fonts, e-mails

| Test | Result | Details |
|---|---|---|
| STL -> OBJ -> re-read: 12 triangles, 8 vertices, 10 × 10 × 10 | OK |  |
| STL -> PLY -> re-read: 12 triangles, 8 vertices, 10 × 10 × 10 | OK |  |
| STL -> 3MF -> re-read: 12 triangles, 8 vertices, 10 × 10 × 10 | OK |  |
| STL -> GLB -> re-read: 12 triangles, 8 vertices, 10 × 10 × 10 | OK |  |
| GLB validated by an independent JSON read (Python) | OK |  |
| TTF -> WOFF -> TTF: identical tables (checked with fontTools) | OK |  |
| EML -> TXT: decoded headers, text, attachment listed | OK |  |
| EML -> HTML: script, onclick and tracking image removed | OK |  |
| MBOX -> 3 EML files | OK |  |
| VCF (vCard 3.0 and 2.1 quoted-printable) -> CSV | OK |  |
| ICS -> CSV: events, all-day, recurrence, alarms ignored | OK |  |

## Conversion manager — guarantees

| Test | Result | Details |
|---|---|---|
| Accents and spaces in names; conflict => "(2)" | OK |  |
| "Skip" policy: task skipped, existing file untouched | OK |  |
| Explicit "Overwrite" policy: replaces the output, never the source | OK |  |
| Not enough disk space: refused before converting | OK |  |
| Disguised program: conversion refused | OK |  |
| Real content differs from the extension: chosen conversion refused with an explanation | OK |  |
| Invalid setting refused before starting | OK |  |
| Queue: batch of 6 including 1 damaged file, the others succeed | OK |  |
| Queue: pause (no new task), resume, cancel a waiting task | OK |  |
| History: record, search, delete, disable | OK |  |
| Format search: "WebP" | OK |  |
| Cleanup of abandoned temporary files | OK |  |

## Archives — creation, extraction, verification

| Test | Result | Details |
|---|---|---|
| ZIP (built-in): folder with accents created, CRC check, identical extraction | OK |  |
| Corrupted ZIP: check fails | OK |  |
| TAR.GZ (built-in): several items | OK |  |
| GZ of a single file, then GZ of several => TAR.GZ | OK |  |
| 7Z with password and encrypted headers (7-Zip) | OK |  |
| AES-256 encrypted ZIP (7-Zip) then extraction (7-Zip) | OK |  |
| XZ and BZ2 (7-Zip) | OK |  |
| Password refused for TAR.GZ (format without encryption) | OK |  |

## Archives — security

| Test | Result | Details |
|---|---|---|
| Zip slip: "../", absolute, drive and NTFS stream entries skipped; nothing outside the folder | OK |  |
| Zip slip via 7-Zip: extraction refused | NOT RUN | this environment's 7-Zip normalizes paths: the malicious archive cannot be crafted |
| Decompression bomb (80 MiB of zeros, ratio ×1000): stopped | OK |  |
| TAR with symbolic and hard links: links skipped, escape refused | OK |  |
| Extraction: never overwrites (numbered folder) | OK |  |
| ZIP -> 7Z archive conversion through the converter (identical content) | OK |  |
| Extraction through the converter (output folder validated) | OK |  |
| RAR: extraction only with full 7-Zip (never creation) | NOT RUN | no RAR test archive (non-free rar tool absent from the environment) |

## Windows part — cannot run in this environment

| Test | Result | Details |
|---|---|---|
| Real keyboard/mouse hooks (WH_MOUSE_LL / WH_KEYBOARD_LL) and Alt menu suppression | NOT RUN | requires Windows: to be validated on a PC with docs/WINDOWS-CHECKLIST.md (the code compiles on Windows via GitHub Actions) |
| Wheel as an OLE drop target over File Explorer and the Desktop (CF_HDROP, Copy effect) | NOT RUN | requires Windows: to be validated on a PC with docs/WINDOWS-CHECKLIST.md (the code compiles on Windows via GitHub Actions) |
| Native drag and drop intact when the file is dropped outside the wheel | NOT RUN | requires Windows: to be validated on a PC with docs/WINDOWS-CHECKLIST.md (the code compiles on Windows via GitHub Actions) |
| Recycle Bin, Properties, Open with, Share, .lnk shortcut (Shell API) | NOT RUN | requires Windows: to be validated on a PC with docs/WINDOWS-CHECKLIST.md (the code compiles on Windows via GitHub Actions) |
| Notification area icon, balloons, start with Windows (Run key) | NOT RUN | requires Windows: to be validated on a PC with docs/WINDOWS-CHECKLIST.md (the code compiles on Windows via GitHub Actions) |
| WPF windows: hybrid light/dark theme, per-monitor DPI, screen reader | NOT RUN | requires Windows: to be validated on a PC with docs/WINDOWS-CHECKLIST.md (the code compiles on Windows via GitHub Actions) |
| Conversions through the Windows codecs (WIC) | NOT RUN | requires Windows: to be validated on a PC with docs/WINDOWS-CHECKLIST.md (the code compiles on Windows via GitHub Actions) |
| FileWheel-Setup.exe installer on real Windows (install, update, uninstall) | NOT RUN | requires Windows: to be validated on a PC with docs/WINDOWS-CHECKLIST.md (the code compiles on Windows via GitHub Actions) |

