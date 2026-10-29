# Checklist on a Windows PC

To run through after `INSTALL.bat` (or with `FileWheel-Setup-<version>.exe` downloaded from the GitHub Releases), because these items could not be executed in the development environment. Tick what works and note what does not (with the log: icon menu › *Ouvrir le dossier des journaux*, Open the logs folder).

## Installation
- [ ] `INSTALL.bat` compiles, creates `FileWheel-Setup-<version>.exe` and runs it (or: the .exe from the Releases runs after SmartScreen's *More info › Run anyway*).
- [ ] Installation: Welcome screen in FileWheel colors, options (start with Windows, Desktop shortcut), FileWheel launched at the end.
- [ ] Update from a previous version: "Update" screen, settings kept, FileWheel restarted.
- [ ] Same version run again: Repair / Uninstall screen.
- [ ] FileWheel appears in the Start menu and in *Settings › Installed apps*.
- [ ] The monochrome icon is visible next to the clock; its menu works.

## Wheel
- [ ] Drag a file in File Explorer + Alt: the wheel appears under the cursor.
- [ ] Same thing on the Desktop.
- [ ] Drop outside the wheel: the file is copied/moved normally by Windows.
- [ ] Drop on the center or Esc: nothing happens, the file stays in place.
- [ ] Release Alt after the wheel appears: File Explorer's menu bar does not activate.
- [ ] Drag without Alt, click, press Alt alone: no wheel.
- [ ] Submenu (Archive, More): opens on hover; dropping on a submenu → the wheel stays and is driven by clicking.
- [ ] Two screens / 125–200 % zoom: the wheel is sharp, the right size, never cut by the edge.
- [ ] The original file is never moved when dropping on a "Copy" action.

## Actions
- [ ] Copy, then Ctrl+V in a folder.
- [ ] Move to… (name conflict: the dialog offers "Keep both").
- [ ] Rename (one file, then several in a batch).
- [ ] Compress to ZIP / Extract here.
- [ ] Recycle Bin, Delete permanently (confirmation, Enter = Cancel).
- [ ] Open with, Properties, Parent folder, Terminal here, Create shortcut, Share.

## Express wheel and image tools
- [ ] Dragged PDF: PNG, TXT, DOCX, JPG in large type in the wheel; dropping on JPG creates the pages next to it.
- [ ] Dragged image: Crop, Background, Compress, Annotate, Info, Convert, File.
- [ ] Crop (aspect ratios, rotation), Background (gradient, padding, corners, shadow), Compress (estimated size), Annotate (arrow, text, redact, blur, Ctrl+Z), Info (GPS flagged, copy without metadata).
- [ ] In the center of the wheel: file size and name at rest.

## Conversion
- [ ] PNG image → JPEG through the wheel (Windows codecs, no engine).
- [ ] *Paramètres › Moteurs* › install FFmpeg: the consent dialog shows source, version, size; installation verifies the hash.
- [ ] MP4 video → MP3; cancel during conversion (partial file deleted).
- [ ] Completion notification; click → File Explorer selects the result.
- [ ] Tasks window: progress and remaining time.

## Design
- [ ] Light mode and dark mode (switch in Windows while FileWheel is open: changes live).
- [ ] Change the Windows accent color: primary buttons, switches and wheel follow.
- [ ] Settings: sidebar, grouped cards, switches, visible keyboard focus (Tab).
- [ ] Narrator reads buttons and settings.

## Uninstallation
- [ ] *Installed apps › FileWheel › Uninstall*: FileWheel stops, everything is removed; settings are only deleted if the box is ticked.
- [ ] After uninstalling: no more auto-start, no more shortcut, program folder deleted.
