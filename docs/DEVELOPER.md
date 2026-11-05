# Developer documentation

## Tech stack

- **C# / .NET 10**, **WPF user interface written in C#** (no XAML: styles are built in code in `Ui/Theme.cs`).
- WPF Fluent theme (`ThemeMode = System`) for fields and lists, overridden by the hybrid design system.
- No NuGet dependency at runtime. External engines are called as separate programs, or loaded dynamically through their C API (PDFium, ONNX Runtime). On Windows the target is `net10.0-windows10.0.19041.0` for the WinRT projections (Windows OCR); minimum Windows 10 1809.
- Manifest: `asInvoker`, `PerMonitorV2` DPI, long paths.
- Interface texts are written in French in the code and translated at display time (see *Localization*); log messages and code comments are in French; the documentation is in English.

## Source tree

```
FileWheel/
├─ .github/workflows/build.yml          GitHub Actions: Windows build + installer; Release on v* tags
├─ INSTALL.bat                          builds FileWheel-Setup-<version>.exe and runs it
├─ BUILD-SETUP.bat                      only builds the .exe
├─ UNINSTALL.bat                        runs Uninstall.exe from the installation
├─ installer/
│  ├─ Build-Setup.ps1                   .NET 10 SDK + self-contained win-x64 publish + NSIS → FileWheel-Setup-<version>.exe
│  └─ FileWheel.nsi                     NSIS installer: install / update / repair / uninstall
├─ assets/                              filewheel.ico (16 to 256 px), filewheel.png, filewheel.svg, installer-side.bmp,
│                                       logo_gen.py / logo_icon.py (logo sources)
├─ Directory.Build.props                version, common options; FwIsWindows depending on the OS
├─ build/
│  ├─ WindowsDesktop.props              net10.0-windows + UseWPF on Windows; type checking elsewhere
│  └─ ci-linux.sh                       build + tests outside Windows
├─ src/
│  ├─ FileWheel.Core/                   no Windows dependency, tested
│  │  ├─ Input/                         gesture detector (state machine), key combination, screens/DPI, drop effect
│  │  ├─ Wheel/                         items, geometry, interaction (hover, submenus), menu building
│  │  ├─ Formats/                       format catalog, signature detection
│  │  ├─ FileOperations/                safe copy/move/rename, name validation
│  │  ├─ Actions/                       command identifiers, custom actions
│  │  ├─ Localization/                  Loc (translation at display time) + en.json (French → English)
│  │  ├─ Settings/ Logging/ Util/        atomic JSON settings, log (+ LogSanitizer for reports), paths (+ portable mode)
│  ├─ FileWheel.Conversion/             conversion engine, no Windows dependency, tested
│  │  ├─ Core/                          contracts, registry, manager, job queue, processes, archive safety,
│  │  │                                 output planning and validation, history, profiles
│  │  ├─ Engines/                       engine catalog, verified download (SHA-256), pinned engines, winget
│  │  ├─ Ai/                            ONNX Runtime C API binding, BackgroundRemover (IS-Net)
│  │  ├─ Updates/                       Updater (GitHub Releases: check, verified download, silent install)
│  │  └─ Providers/                     Media (FFmpeg), Images (ImageMagick), Archives, Data, Text, Pdf (PDFium),
│  │                                    Office (LibreOffice), Ebooks (Calibre), Model3D, Fonts, Mail
│  │                                    Pdf/PdfTools + PdfiumEdit: merge, extract/delete/rotate pages, compress images
│  ├─ FileWheel.Windows/                Windows interop: hooks, UIA, Shell, notification area, DWM, WIC, Ocr (Windows.Media.Ocr)
│  └─ FileWheel.App/                    exe: resident application, wheel, windows, action execution
│     ├─ Program.cs                     entry point, single instance, --arriere-plan / --parametres / --quitter
│     ├─ FileWheelApp.cs                wiring: hooks → wheel → actions, icon, notifications
│     ├─ ActionExecutor.cs              runs each wheel command
│     ├─ CaptureMode.cs                 --capture: opens every window, saves PNGs, lists untranslated texts (CI)
│     ├─ Services.cs                    shared engine for the windows
│     ├─ Tools/                         image tools: ImageIO (safe read/write), ToolWindow (base),
│     │                                 CropWindow, BackgroundWindow, CompressWindow, AnnotateWindow, MetadataWindow,
│     │                                 RemoveBackgroundWindow, OcrWindow, PdfWindows (pages, merge, compress)
│     └─ Ui/                            Theme (design system), Ui (builders), WheelWindow, TrayGlyph (notification icon),
│                                       Dialogs, SettingsWindow, QuickConvertWindow, ConverterWindow, TasksWindow, CustomActionDialog,
│                                       EngineInstaller (consent + install), MaintenanceDialogs (update, report a problem)
├─ installer/portable/                  files added to the portable zip (marker, readme, UNINSTALL-PORTABLE.bat)
├─ installer/New-WingetManifest.ps1     winget manifests of a version
├─ tests/FileWheel.Tests/               custom harness: real engines, generated files, verified outputs
└─ docs/
```

## Main flow

1. `InputHookService` (dedicated thread) receives mouse and keyboard input, classifies the window under the cursor (File Explorer/Desktop) and feeds `DragGestureDetector`.
2. The detector emits `ShowWheel(x, y)` when: button pressed in an allowed area + minimum distance + exact key combination, stable for the delay, and a non-negative UIA check of the item.
3. `WheelWindow` appears (without taking focus) and becomes an **OLE drop target**: when the drag enters, it reads `CF_HDROP`, builds the menu (`WheelMenuBuilder`), tracks hovering, and on drop returns `Copy` to File Explorer.
4. `ActionExecutor` runs the command; conversions go through `ConversionEngine.Queue`, file operations through `OperationsCenter` (background, progress, cancellation).

## Adding a conversion format

1. Declare the format in `FormatCatalog` (and its signature in `FormatDetector` if needed).
2. In a provider (`ProviderBase`), declare the capability with `Cap(...)`: sources, target, type, options, loss, limits, engine.
3. Implement `ConvertAsync`: write to `request.TempOutput`, report progress, throw `ConversionException` with a clear error type.
4. Add a test in `tests/FileWheel.Tests/ConversionTests.cs` that checks the **content** of the output.
5. Regenerate the list: `dotnet run --project tests/FileWheel.Tests -- --catalogue docs/CONVERSIONS.md`.

Note: capability descriptions and test names are declared in French in the code (they are shown in the French UI). The generators (`--catalogue` and the test report `TEST-RESULTS.md`) therefore produce French text; the committed `docs/CONVERSIONS.md` and `docs/TEST-RESULTS.md` are English translations, to be updated by hand when they are regenerated.

## Localization

The code keeps French texts; `Loc.T(text)` translates them when they are shown, if the interface is in English (`AppSettings.Language`: Automatic follows the Windows language). Translation points: the `Ui` builders (titles, texts, buttons, fields, switches, segmented controls, combo boxes, message boxes), the wheel (labels, captions, center texts), notifications, the tray menu, window titles, and a pass over each window when it opens (`Ui.Translate`) for texts set directly on controls. `en.json` maps French to English; entries with `{0}`, `{1}` are patterns for texts with variable parts. Lines, sentences, "(3)" / "…" suffixes and UPPERCASE captions are handled automatically. Texts without a translation stay in French.

To find what is missing: the English capture run (`--capture <folder> --langue en`) writes `en-textes-non-traduits.txt`, and `LocTests` checks that every wheel text is translated.

## Windows checks in CI (capture mode)

`FileWheel.exe --capture <folder> --langue fr|en` runs without the tray and without blocking dialogs (`Ui.Headless`): it creates sample files, renders the wheel (Express image / PDF / PDF tools / File / Classic), opens every settings page, every tool (with PDFium and the background removal engine when `FILEWHEEL_CAPTURE_ENGINES` points to them), the converters and the dialogs, saves each one as a PNG, runs the OCR on a sample image and writes a report. The exit code is the number of screens that failed. The workflow pushes the captures to the `ci-screenshots` branch.

## Building and creating the installer

On Windows: `INSTALL.bat` (or `BUILD-SETUP.bat`) runs `installer/Build-Setup.ps1`:

```
dotnet publish src\FileWheel.App\FileWheel.App.csproj -c Release -r win-x64 --self-contained true -o publish
makensis /DVERSION=<version> /DPUBLISH_DIR=<...>\publish /DOUTFILE=<...>\FileWheel-Setup-<version>.exe installer\FileWheel.nsi
```

The version comes from `Directory.Build.props`. The installer:
- installs into `%LOCALAPPDATA%\Programs\FileWheel` (HKCU, no administrator rights);
- detects the installed version (`HKCU\...\Uninstall\FileWheel\DisplayVersion`): install, update, downgrade or maintenance (repair / uninstall);
- closes FileWheel (`--quitter`, then forced stop after 10 s) before replacing files;
- keeps the choices of the first installation (start with Windows, Desktop shortcut) during an update;
- writes `Uninstall.exe` and the "Installed apps" entry; uninstalling keeps the settings unless the box is ticked; `/S` = silent;
- speaks English or French (Windows language);
- `/S /RELANCER`: silent update started by FileWheel itself, which restarts FileWheel at the end.

### Continuous integration and releases (GitHub Actions)

`.github/workflows/build.yml` runs on `windows-latest` for every push to `main`, every Pull request and every release:
1. real WPF build and self-contained win-x64 publish;
2. startup smoke test (start, stay alive 10 s, `--quitter`, no error in the log);
3. captures in French and English (see above), pushed to the `ci-screenshots` branch;
4. optional SignPath signing (see [SIGNING-AND-WINGET.md](SIGNING-AND-WINGET.md));
5. installer (`makensis`), then silent install / uninstall test;
6. portable zip, `SHA256SUMS.txt`, winget manifests;
7. on a release: files attached to it, optional winget submission.

Publishing a version:
1. change `<Version>` in `Directory.Build.props` (and `AssemblyVersion` / `FileVersion`);
2. update `docs/CHANGELOG.md`;
3. `git tag v<version> && git push origin v<version>` (or create the release on GitHub: *Releases › Draft a new release*, new tag `v<version>`): the workflow builds the files and attaches them to the Release. Installed copies of FileWheel then offer the update.

Testing outside Windows: `makensis -DTARGET_AMD64 ...` produces a 64-bit variant that runs under Wine (`wine64 Setup.exe /S`).

Outside Windows: `build/ci-linux.sh` compiles the engine, runs the tests, and compiles the Windows projects in **type-checking** mode against a WPF reference assembly (`build/wpfref/WpfRefAll.dll`, not distributed). The `FW_TYPECHECK` symbol is defined and, as on Windows with `UseWPF`, `System.IO` is not imported implicitly.

## Tests

```
dotnet run --project tests/FileWheel.Tests -c Release            # everything
dotnet run --project tests/FileWheel.Tests -c Release -- Archives # filter on the name
```

The harness generates its sample files with real tools (ffmpeg, ImageMagick, LibreOffice, Python), runs the real conversions and verifies the outputs (dimensions, duration, tracks, re-read text, hashes, archive contents…). A test whose tool is missing is marked "not run" with the reason. The report is written to `TEST-RESULTS.md` next to the binary (or to `FILEWHEEL_TEST_OUTPUT`).

The tests are designed for the Linux reference environment (they also use `pdftotext`, `pgrep`, `python3` and its modules for checks); the Windows part is verified with `docs/WINDOWS-CHECKLIST.md`.

## Data and locations

| What | Where |
|---|---|
| Program | `%LOCALAPPDATA%\Programs\FileWheel` |
| Settings, history, actions, profiles, engine paths | `%APPDATA%\FileWheel\*.json` (atomic write, `.corrompu-*` backup if unreadable) |
| Engines, logs, downloads, temporary files | `%LOCALAPPDATA%\FileWheel\` |
| Auto-start | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\FileWheel` = `"…\FileWheel.exe" --arriere-plan` |
| "Installed apps" entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\FileWheel` |
| Portable version | everything in `FileWheel-data\` next to `FileWheel.exe` (enabled by the `FileWheel.portable` file) |

`FILEWHEEL_HOME` (environment variable) redirects settings and data to another folder (tests, portable version).
