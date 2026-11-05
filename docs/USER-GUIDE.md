# FileWheel user guide

The interface is available in English and French (*Settings › General › Interface language*; "Automatic" follows the Windows language). This guide gives the English labels, with the French ones in *italics* where it helps.

## 1. Opening the wheel

1. In File Explorer or on the Desktop, **start dragging** a file (or several, or a folder).
2. While dragging, **hold Alt** (the key combination can be changed in *Paramètres › Déclenchement*, Settings › Trigger).
3. The wheel appears under the cursor. Bring the files onto an action and **release**: the action runs.

What never opens the wheel: a simple click, a drag without the key, pressing Alt on its own, Ctrl+Alt or AltGr when the combination is "Alt", a drag outside File Explorer and the Desktop, a rectangle selection in an empty area.

To cancel: **Esc**, drop on the **center** of the wheel, or drop **outside the wheel** (the normal Windows drop then happens where you release).

### Submenus

A sector marked with a small dot opens a submenu when you **hover over it for a moment** (an arc shows the opening). The center then acts as a "Back" button. If you drop the files on a submenu, the wheel stays open and is driven **by clicking**; a click outside or Esc closes it.

### "Express" layout (default)

The wheel adapts to the dragged file:

| File | Sectors |
|---|---|
| Document, PDF, audio, video, data… | The **target formats in large type** (e.g. PDF: PNG, TXT, DOCX, JPG): drop on one and the file is converted next to the original. Then *Autres* (Other: all formats and options) and *Fichier* (File). |
| Image | Crop, Background, Compress, Annotate, Info (metadata), **Cutout** (remove background), **Text** (recognize text), Convert (formats in large type), File. |
| PDF | Target formats in large type, Other, **PDF** (merge, compress, pages, rotate, recognize text), File. |
| Folder, archive, program | Classic layout (below). |

In the center: the **file size** and its name at rest, otherwise the description of the hovered sector. *Fichier* contains Copy, Move to, Open with, Archive, Rename, Recycle Bin and More.

### "Classic" layout (*Paramètres › Roue › Disposition*, Settings › Wheel › Layout)

| Sector | Contents |
|---|---|
| Convert | Up to 5 direct conversions, "Resize…" for images, "Quick conversion…", "Advanced converter…" |
| Copy | Copies to the clipboard: then paste with Ctrl+V wherever you want |
| Move to… | Recent folders / "Send to" folders, or "Choose a folder…" |
| Open with… | Windows "Open with" dialog (single file) |
| Archive | Files: compress to ZIP, 7Z, TAR.GZ, or "Compress…" (level, password). Archives: extract here, extract to…, test, convert to 7Z / ZIP |
| Rename | One item: new name (extension preserved). Several: batch rename with preview |
| Recycle Bin | Move to the Recycle Bin (confirmation can be enabled in *Paramètres › Roue*) |
| More | Open, parent folder, terminal here, properties, copy path / name, shortcut, share, send to, favorites, my actions, **delete permanently** (always confirmed) |

Up to two custom actions can be placed directly on the main wheel.

Actions that are not possible are **greyed out** and the center of the wheel explains why.

## 2. Image tools

Each tool opens a window with a preview; the result is saved **next to the original** with a suffix (never overwritten). Suffixes are in French.

| Tool | What it does | Result |
|---|---|---|
| Crop | Movable frame with handles, aspect ratios Free / 1:1 / 4:3 / 3:4 / 16:9 / 9:16, rotation ↺ ↻, mirror | `photo recadrée.jpg` |
| Background | Gradients and solid colors (or transparent), padding, rounded corners, shadow, frame Image / 1:1 / 4:3 / 16:9 | `photo fond.png` |
| Compress | JPEG or PNG, quality, maximum size (4K, 2560, 1920, 1280), live size estimate; several images at once | `photo compressée.jpg` |
| Annotate | Arrow, box, pen, text, **Redact** (black rectangle) and **Blur** (pixelation), colors, thickness, Ctrl+Z | `photo annotée.jpg` |
| Info | Dimensions, camera, date, shooting settings, **GPS location** (flagged), software, author; copy without metadata | `photo sans métadonnées.jpg` |

Phone photos are straightened automatically (EXIF orientation). Formats Windows can read (PNG, JPEG, BMP, GIF, TIFF, ICO, and WebP/HEIC/AVIF if the Windows extensions are installed) are accepted; the result keeps the original format when Windows can write it, otherwise it is saved as PNG.

### Remove background (Cutout)

The **Cutout** tool removes the background of a photo with an AI model (IS-Net) that runs **on your PC**: the image is never sent anywhere. The first time, FileWheel offers to download the model and ONNX Runtime (about 190 MB, verified with SHA-256 checksums written in FileWheel); it also needs the Microsoft Visual C++ runtime, which most PCs already have (FileWheel offers to install it through winget if it is missing). In the window, choose sharp or soft edges and a transparent, white or black background; the result is saved as `photo cutout.png` (`photo détourée.png` in French).

### Recognize text (OCR)

**Text** (images) and **PDF › Recognize text** (scanned PDFs) use the text recognition built into Windows: free, local, nothing to download. The text appears in a window where you can correct it, **copy** it in one click or **save it as .txt** next to the original. Recognition languages are the ones installed in Windows (*Windows Settings › Time & language › Language*, "Optical character recognition" option); a language list lets you pick another one.

## 3. PDF tools

Drag one or more PDFs, hold Alt, then open the **PDF** sector:

| Tool | What it does | Result |
|---|---|---|
| Merge | Several PDFs, in an order you can change (Move up / Move down / Sort by name) | `first-file merged.pdf` |
| Compress | Photos and images are downscaled and recompressed as JPEG; text and drawings stay vector. Usage: Screen (96 dpi), Standard (150 dpi), Print (220 dpi). Black-and-white scans, transparent and small images are left untouched. | `file compressed.pdf` |
| Pages… | Thumbnails of the pages: click, Shift+click, Ctrl+click or type "1-3, 5"; then **Only these pages** (extract), **All except these pages** (delete) or **Rotate these pages** | `file pages 1-3.pdf`, `file without pages 2.pdf`, `file rotated.pdf` |
| Rotate right / left | All pages, 90° | `file rotated.pdf` |
| Recognize text | OCR of every page of a scanned PDF | text window |

PDF tools use the PDFium engine (offered for download the first time). The original PDF is never modified.

## 4. Converting

- **Quick conversion**: from the wheel, *Convertir › JPEG* (for example). The converted file is created next to the original with a free name (`photo (2).jpg` if `photo.jpg` already exists). A notification signals completion; click it to open the folder.
- **Quick conversion…**: conversion window with every possible target, their options (quality, resolution, codec, pages…) and a clear warning about any losses before starting.
- **Advanced converter** (tray icon menu › *Convertisseur*): file list filled by drag and drop, common target, output folder, savable profiles, batch conversion.

Rules always applied:
- the original is never modified or overwritten; an existing file is never replaced without an explicit rule from you (*Paramètres › Conversion › En cas de nom déjà pris*, If the name is taken);
- the result is written to a temporary file, verified (size, header, readability), then renamed: an interrupted conversion never leaves a half-written file;
- if a conversion is not possible (missing engine, damaged file, DRM- or password-protected), a clear message explains why.

### Engines

Some conversions use an external program. In *Paramètres › Moteurs* (Settings › Engines):
- **FFmpeg, ImageMagick, 7-Zip, PDFium**: "Download and install" button. FileWheel shows the source (official GitHub repository), version and size, then verifies the published SHA-256 hash before installing into `%LOCALAPPDATA%\FileWheel\engines`. Nothing is downloaded without your click.
- **LibreOffice, Calibre**: installed as regular applications through winget (with your consent); FileWheel then detects them.
- You can also point to an engine already present on the PC.

## 5. Tasks and history

Click the FileWheel icon in the notification area: **Tasks** window (*Tâches*), with progress, estimated time remaining, cancel, retry, open result. Pause is only offered if the engine supports it (none today: cancelling is always possible).

The **history** (*Paramètres › Historique*) keeps file names, the conversion, the date and the result — never the content. Search, disable and clear are available.

## 6. Settings

| Section | Main settings |
|---|---|
| *Général* (General) | Wheel enabled, start with Windows, **interface language**, log level |
| *Déclenchement* (Trigger) | Key combination, appearance delay, minimum distance, Desktop / File Explorer, one or several files, close when the key is released |
| *Roue* (Wheel) | Express / Classic layout, labels under icons, diameter, offset, opacity, blur (experimental), animations, submenu delay, Recycle Bin confirmation |
| *Conversion* | Output folder, suffix, name conflicts, simultaneous tasks, priority, minimum free space, extraction limits |
| *Moteurs* (Engines) | Install, update, manual path, uninstall engines |
| *Envoyer vers et favoris* (Send to and favorites) | Folders offered in the wheel, favorites reachable from the icon |
| *Actions personnalisées* (Custom actions) | Run a program, copy/move to a folder, convert — with safety rules (see SECURITY.md) |
| *Historique* (History) | Browse, search, clear |
| *À propos* (About) | Version, licenses, **updates** (automatic check, check now), **report a problem**, source code |

Settings are saved immediately to `%APPDATA%\FileWheel\settings.json`.

## 7. Icon next to the clock

Monochrome (white on a dark taskbar, black on a light taskbar). Greyed out when the wheel is disabled; its sectors fill up during a conversion or a copy; a **green** (done) or **red** (failed) dot stays until the next click. Right-click › Troubleshooting: test the wheel display, diagnostic mode, logs folder, **report a problem**, **check for updates**. When an update is available, the first menu item installs it.

## 8. Updating, uninstalling

**Automatic**: once a day, FileWheel checks the [Releases](https://github.com/nabeiroriviereadriano-design/FileWheel/releases) page (no file or personal information is sent; can be turned off in *Settings › About* or at first launch). When a new version is out, a notification appears: click it to read what's new, then **Update** — the installer is downloaded from GitHub, its SHA-256 checksum is verified, FileWheel closes, updates and restarts, with your settings kept. **Skip this version** stops the reminders for that version.

**Manual**: simply run a newer `FileWheel-Setup`. To uninstall: *Windows Settings › Apps › FileWheel*, or run the same `FileWheel-Setup` again › Uninstall.

**Portable version**: extract `FileWheel-<version>-portable-win-x64.zip` anywhere (USB stick, Documents…) and run `FileWheel.exe`. Settings, history and engines stay in the `FileWheel-data` subfolder. To update, replace the files and keep `FileWheel-data`. To uninstall, run `UNINSTALL-PORTABLE.bat`: it stops FileWheel, removes auto-start if it points to this folder, and deletes the folder (your settings too, if you say so).

## 9. Reporting a problem

*Settings › About › Report a problem* (or the icon's Troubleshooting menu) opens a form with your description, the FileWheel and Windows versions and, if you want, the end of the log with **file names, paths and e-mail addresses masked**. Everything is shown and editable before sending; **Open on GitHub** pre-fills an issue in the public repository (a GitHub account is needed), **Copy the report** lets you send it another way.

## 10. Troubleshooting

- **The wheel does not appear**: is FileWheel running (icon next to the clock)? Is the wheel enabled? Is File Explorer running as administrator while FileWheel is not (or the other way round)? Try another combination in *Paramètres › Déclenchement*.
- **Log**: icon menu › *Ouvrir le dossier des journaux* (Open the logs folder, `%LOCALAPPDATA%\FileWheel\logs`). The log contains neither the content of your files nor your user name.
