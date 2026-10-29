# Changelog and development milestones

Each milestone follows the same method: what was done, how it is verified, what remains uncertain.

## Milestone 1 — Feasibility of the "drag + key" trigger

**Question**: can a wheel be shown during a native File Explorer drag without breaking it?

**Answer: yes, with a precise architecture.**
- During an OLE drag, File Explorer keeps the mouse capture: the drag cannot be "stolen". FileWheel **observes** it (low-level `WH_MOUSE_LL` / `WH_KEYBOARD_LL` hooks, blocking nothing) and shows a window on top.
- That window is an **OLE drop target**: Windows hands it the files (`CF_HDROP`) when the cursor hovers it — the only reliable source of the file list.
- It answers **Copy** to the drop: File Explorer therefore never deletes the original, even if the user was moving it.
- Releasing Alt would activate File Explorer's menu bar: it is preceded by an injected neutral key (standard technique).
- Constraint found: an elevated application does not receive drops from a non-elevated File Explorer (UIPI) → `asInvoker` manifest, warning in the installer.

**Verification**: the decision logic (22 cases: exact combinations, AltGr, Alt+Tab, delays, empty area…) is a state machine tested without Windows. The hooks themselves had to be validated on a PC.

## Milestones 2–3 — Architecture, wheel and file actions

- Split into 4 projects: `Core` (pure logic, tested), `Conversion` (engine, tested), `Windows` (interop), `App` (interface).
- Wheel: geometry and interaction (hover, timed submenus, click mode after dropping on a branch) in `Core/Wheel`, tested; multi-screen and DPI placement tested (100 %, 125 %, 200 %, negative coordinates, edges).
- Safe file operations: copy/move through a verified temporary file, never an implicit overwrite, renaming with Windows name validation (reserved names, forbidden characters, lengths).

## Milestones 4–5 — Conversion engine and modules

- A single `IConversionProvider` contract: each provider **declares** its real conversions (sources, target, options, losses, limits, engine). The menu, the converter and `CONVERSIONS.md` are generated from these declarations: nothing is advertised that is not implemented.
- Built-in providers written for FileWheel: documents (Markdown, HTML, text, PDF, EPUB), data (CSV/TSV/JSON/XML/YAML/XLSX), 3D (STL/OBJ/PLY/3MF/glTF/GLB), fonts (WOFF↔TTF/OTF), e-mails/contacts/calendars, ZIP/TAR/GZ archives.
- External engines: FFmpeg (lossless remux when possible, re-encode otherwise, progress, automatic retry), ImageMagick, 7-Zip, PDFium, LibreOffice, Calibre; Windows codecs (WIC).
- Job queue: adjustable concurrency, low priority, cancellation (process tree killed, partial file deleted), retry, remaining time.

## Milestone 6 — Security and real tests

- Archives: zip slip, links, reserved names, decompression bombs, passwords.
- Processes: separate arguments, no shell; engines verified by SHA-256.
- Tests: **195 passed, 0 failed** at that point, 2 not run for lack of a sample (a malicious 7z archive impossible to craft here, no RAR tool), plus 8 Windows items listed as not run. Details: TEST-RESULTS.md.

## Milestone 7 — Interface, delivery

- Complete WPF interface: wheel, quick conversion, advanced converter, tasks, settings (9 sections), dialogs (conflicts, batch rename, compression, engine consent, welcome), custom actions.
- **Hybrid Windows 11 + macOS design system** (DESIGN.md), live light/dark mode.
- Installer and **uninstaller** ("Installed apps" entry), without administrator rights.

**Remaining uncertainty at that point**: real execution on Windows (hooks, OLE, Shell, WPF) could not be done in the development environment; it is framed by WINDOWS-CHECKLIST.md.

## Version 0.2.0 — Express wheel, image tools, .exe installer

- **Feedback from the PC** (0.1.1 → 0.1.5): 38 WPF-specific compile errors (implicit `System.IO` removed), stronger File Explorer detection, synthetic keys ignored, installer that waits for FileWheel to close, and above all the wheel that never appeared: the "drag image visible" signal triggered the display but its result was discarded. Fixed; wheel validated on the PC in 0.1.5.
- **Logo and notification icon** (0.1.6): logo A chosen, monochrome icon with states (greyed out, progress, dot).
- **Express wheel**: inspired by a reference video — target formats directly in the wheel, tools for images, file actions grouped together. 9 new tests (204 passed in total).
- **Image tools**: crop, background, compress, annotate / redact / blur, metadata. Compiled, to be validated on Windows.
- **Installer**: `FileWheel-Setup-<version>.exe` (NSIS) — install, update keeping the options, repair, uninstall. Tested under Wine: install, update (version, options, cleanup), uninstall (settings kept), Welcome / Update / Maintenance screens.

## Public open-source repository

- Published on GitHub under the MIT license: https://github.com/nabeiroriviereadriano-design/FileWheel
- **GitHub Actions** compiles the Windows version on a real Windows machine (`windows-latest`) and builds the installer on every change; a `v<version>` tag publishes `FileWheel-Setup-<version>.exe` in the Releases. First real Windows build of 0.2.0: success, 0 errors.
- Documentation translated into English; test sample data anonymized; no third-party program or reference binary in the repository.
