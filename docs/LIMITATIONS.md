# Known limitations of FileWheel 0.3.0

This document honestly lists what is not done, what could not be verified, and why.

## What has not been run on Windows

FileWheel was developed and tested in a Linux environment: the conversion engine, the wheel logic (trigger, multiple screens, menus, interaction) and the file operations were **really** tested there (see TEST-RESULTS.md). The new PDF tools and the AI background removal are tested there too, with the real PDFium and ONNX Runtime libraries.

The Windows-specific parts **compile and start on a real Windows machine** (GitHub Actions, `windows-latest`): at each change the app is started and stopped, every window and the wheel are opened in French and in English and saved as screenshots, the OCR is run on a sample image, the background removal on a sample photo, and the installer is installed and uninstalled silently. What still needs a person in front of a PC:
- the real gesture: keyboard/mouse hooks, Alt menu suppression, OLE drag and drop over File Explorer;
- Shell APIs (Recycle Bin, Properties, Open with, Share, shortcuts);
- notification icon, auto-start, single instance;
- interacting with the windows (the screenshots show them open, not used);
- the in-app update (download, silent installer, restart) on a real installation.

The wheel itself (trigger, display, drop) was validated on the user's PC in version 0.1.5.

If an error appears at installation or first launch, the message and the log (`%LOCALAPPDATA%\FileWheel\logs`) make it possible to fix it. Checklist: WINDOWS-CHECKLIST.md.

## Trigger

- An application running **as administrator** does not receive files dragged from a non-elevated File Explorer (a Windows rule, UIPI). FileWheel must run at the same level as File Explorer.
- In File Explorer windows, the item under the cursor is checked through UI Automation (UIA) with a time limit; if Windows does not answer in time, the "tolerant" mode (default) shows the wheel anyway.
- Drags from other applications (browser, Outlook, archives opened in File Explorer, MTP phones) do not always provide real file paths: the wheel says so and offers nothing.
- On some keyboards, Alt may conflict with third-party tools; change the combination in the settings.

## Interface

- Wheel **background blur**: experimental (undocumented Windows API), off by default.
- **Share**: uses the Windows share sheet when available; otherwise FileWheel opens File Explorer on the files.
- The toggle switch does not animate (the state changes instantly).
- Window buttons stay the Windows ones (no macOS "traffic lights"), on purpose.
- The interface is available in English and French. Texts coming from external engines (FFmpeg, ImageMagick, LibreOffice messages in the log) stay as the engine writes them. A few rare messages may still appear in French in the English interface: the automatic check lists them at each build so they can be added.

## Conversion

Not supported (and therefore never offered):
- **OCR inside conversions**: "PDF → TXT" extracts the text layer only; for a scanned PDF, use *PDF › Recognize text*;
- **CAD**: STEP, IGES, DXF/DWG (recognized, not converted); **FBX** (proprietary format);
- **WOFF2** (recognized, not converted);
- **creating RAR archives** (proprietary format); RAR extraction requires 7-Zip;
- Outlook **MSG/PST** e-mails;
- **DRM**-protected books (refused with a message).

Losses to be aware of (always announced before converting):
- PDF → DOCX/ODT: approximate reconstruction;
- 3D: geometry only (materials, textures, animations lost);
- text → built-in PDF: standard PDF fonts, characters outside the Western Latin alphabet replaced by "?" (use LibreOffice for other alphabets);
- lossy formats (JPEG, MP3, AAC, H.264…): quality loss at each re-encode; FileWheel copies without re-encoding whenever possible (video remuxing).

## Image tools

- They use the Windows codecs: WebP, HEIC and AVIF can only be read if the corresponding extensions are installed (Microsoft Store); PSD, RAW, SVG, EXR must first go through "Convert › PNG".
- Windows cannot write WebP, HEIC or AVIF: a tool's result is then saved as PNG.
- Compressing to PNG is lossless: the gain mostly comes from the maximum size. JPEG is re-encoded (slight effect at each pass).
- "Copy without metadata" rewrites the pixels: for a JPEG, that is a re-encode at quality 95.
- Annotate: text is typed in a small window and then placed with a click (no in-place editing).

## Text recognition (OCR)

- It uses the Windows engine: quality is good for printed text, poor for handwriting.
- Only the languages installed in Windows with the "Optical character recognition" option are available.
- Layout is simplified to lines and paragraphs (no tables, no columns).
- Large pages are rendered at up to 200 dpi, within the Windows engine's size limit.

## Background removal (Cutout)

- Optional engine of about 190 MB (IS-Net model + ONNX Runtime), downloaded once with your consent; needs the Microsoft Visual C++ 2015-2022 runtime.
- Runs on the processor (no graphics card needed): a few seconds per photo; the model stays in memory while FileWheel runs.
- Works best on photos with a clear subject; fine details (hair, glass, smoke) may be imperfect. "Soft edges" helps for hair and fur.

## PDF tools

- Compression only recompresses images (photos, color scans) as JPEG; a PDF made of text or vector drawings gains little. Black-and-white scans (1 bit), transparent images and images already small are left as they are.
- Rotation sets the page rotation (like a PDF viewer does); it does not redraw the content.
- Password-protected PDFs: open them once in a PDF viewer to remove the password, then use the tools.

## Tasks

- **Pause**: no current engine can cleanly suspend a running conversion; only cancelling is offered (the partial file is deleted). The queue itself can be paused.
- Remaining time is estimated from the progress reported by the engine; some engines (LibreOffice, Calibre) do not report any: the bar is then indeterminate.

## Engines

- Engine hashes are not hard-coded: FileWheel requires the SHA-256 hash published by GitHub for each downloaded file and refuses to install if it is missing or does not match.
- LibreOffice and Calibre are installed through winget (recent Windows 10 / Windows 11); without winget, install them manually and FileWheel will detect them.
