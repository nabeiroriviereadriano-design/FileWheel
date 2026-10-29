# Known limitations of FileWheel 0.2.0

This document honestly lists what is not done, what could not be verified, and why.

## What has not been run on Windows

FileWheel was developed and tested in a Linux environment: the conversion engine, the wheel logic (trigger, multiple screens, menus, interaction) and the file operations were **really** tested there (204 tests passed, see TEST-RESULTS.md).

The Windows-specific parts **compile on a real Windows machine** (GitHub Actions, `windows-latest`), but most of them **have not been run** yet:
- keyboard/mouse hooks, Alt menu suppression, OLE drag and drop over File Explorer;
- Shell APIs (Recycle Bin, Properties, Open with, Share, shortcuts);
- notification icon, auto-start, single instance;
- all WPF windows and the design system (no real screenshot could be taken);
- WIC conversions;
- image tools (crop, background, compress, annotate, metadata) and the new Express wheel: compiled, never run;
- the `FileWheel-Setup.exe` installer was tested under Wine (silent install, update and uninstall, Welcome / Update / Maintenance screens) and is built by GitHub Actions on Windows.

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
- The interface is only available in French.

## Conversion

Not supported (and therefore never offered):
- **OCR**: a scanned PDF or an image does not produce text;
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

## Tasks

- **Pause**: no current engine can cleanly suspend a running conversion; only cancelling is offered (the partial file is deleted). The queue itself can be paused.
- Remaining time is estimated from the progress reported by the engine; some engines (LibreOffice, Calibre) do not report any: the bar is then indeterminate.

## Engines

- Engine hashes are not hard-coded: FileWheel requires the SHA-256 hash published by GitHub for each downloaded file and refuses to install if it is missing or does not match.
- LibreOffice and Calibre are installed through winget (recent Windows 10 / Windows 11); without winget, install them manually and FileWheel will detect them.
