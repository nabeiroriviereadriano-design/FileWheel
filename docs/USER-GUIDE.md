# FileWheel user guide

The application's interface is in French: French labels are shown in *italics*, followed by their English meaning.

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
| Image | *Recadrer* (Crop), *Fond* (Background), *Compresser* (Compress), *Annoter* (Annotate), *Infos* (metadata), *Convertir* (Convert: formats in large type), *Fichier* (File). |
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

## 3. Converting

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

## 4. Tasks and history

Click the FileWheel icon in the notification area: **Tasks** window (*Tâches*), with progress, estimated time remaining, cancel, retry, open result. Pause is only offered if the engine supports it (none today: cancelling is always possible).

The **history** (*Paramètres › Historique*) keeps file names, the conversion, the date and the result — never the content. Search, disable and clear are available.

## 5. Settings

| Section | Main settings |
|---|---|
| *Général* (General) | Wheel enabled, start with Windows, log level |
| *Déclenchement* (Trigger) | Key combination, appearance delay, minimum distance, Desktop / File Explorer, one or several files, close when the key is released |
| *Roue* (Wheel) | Express / Classic layout, labels under icons, diameter, offset, opacity, blur (experimental), animations, submenu delay, Recycle Bin confirmation |
| *Conversion* | Output folder, suffix, name conflicts, simultaneous tasks, priority, minimum free space, extraction limits |
| *Moteurs* (Engines) | Install, update, manual path, uninstall engines |
| *Envoyer vers et favoris* (Send to and favorites) | Folders offered in the wheel, favorites reachable from the icon |
| *Actions personnalisées* (Custom actions) | Run a program, copy/move to a folder, convert — with safety rules (see SECURITY.md) |
| *Historique* (History) | Browse, search, clear |

Settings are saved immediately to `%APPDATA%\FileWheel\settings.json`.

## 6. Icon next to the clock

Monochrome (white on a dark taskbar, black on a light taskbar). Greyed out when the wheel is disabled; its sectors fill up during a conversion or a copy; a **green** (done) or **red** (failed) dot stays until the next click. Right-click › *Dépannage* (Troubleshooting): test the wheel display, diagnostic mode, logs folder.

## 7. Updating, uninstalling

Simply run a newer `FileWheel-Setup`: FileWheel is closed, updated and restarted, with your settings kept. To uninstall: *Windows Settings › Apps › FileWheel*, or run the same `FileWheel-Setup` again › Uninstall.

## 8. Troubleshooting

- **The wheel does not appear**: is FileWheel running (icon next to the clock)? Is the wheel enabled? Is File Explorer running as administrator while FileWheel is not (or the other way round)? Try another combination in *Paramètres › Déclenchement*.
- **Log**: icon menu › *Ouvrir le dossier des journaux* (Open the logs folder, `%LOCALAPPDATA%\FileWheel\logs`). The log contains neither the content of your files nor your user name.
