# Developer documentation

## Tech stack

- **C# / .NET 10**, **WPF user interface written in C#** (no XAML: styles are built in code in `Ui/Theme.cs`).
- WPF Fluent theme (`ThemeMode = System`) for fields and lists, overridden by the hybrid design system.
- No NuGet dependency at runtime. External engines are called as separate programs (or PDFium, loaded dynamically).
- Manifest: `asInvoker`, `PerMonitorV2` DPI, long paths.
- The user interface, log messages and code comments are in French; the documentation is in English.

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
│  │  ├─ Settings/ Logging/ Util/        atomic JSON settings, log, paths
│  ├─ FileWheel.Conversion/             conversion engine, no Windows dependency, tested
│  │  ├─ Core/                          contracts, registry, manager, job queue, processes, archive safety,
│  │  │                                 output planning and validation, history, profiles
│  │  ├─ Engines/                       engine catalog, verified download (SHA-256), winget
│  │  └─ Providers/                     Media (FFmpeg), Images (ImageMagick), Archives, Data, Text, Pdf (PDFium),
│  │                                    Office (LibreOffice), Ebooks (Calibre), Model3D, Fonts, Mail
│  ├─ FileWheel.Windows/                Windows interop: hooks, UIA, Shell, notification area, DWM, WIC
│  └─ FileWheel.App/                    exe: resident application, wheel, windows, action execution
│     ├─ Program.cs                     entry point, single instance, --arriere-plan / --parametres / --quitter
│     ├─ FileWheelApp.cs                wiring: hooks → wheel → actions, icon, notifications
│     ├─ ActionExecutor.cs              runs each wheel command
│     ├─ Tools/                         image tools: ImageIO (safe read/write), ToolWindow (base),
│     │                                 CropWindow, BackgroundWindow, CompressWindow, AnnotateWindow, MetadataWindow
│     └─ Ui/                            Theme (design system), Ui (builders), WheelWindow, TrayGlyph (notification icon),
│                                       Dialogs, SettingsWindow, QuickConvertWindow, ConverterWindow, TasksWindow, CustomActionDialog
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
- writes `Uninstall.exe` and the "Installed apps" entry; uninstalling keeps the settings unless the box is ticked; `/S` = silent.

### Continuous integration and releases (GitHub Actions)

`.github/workflows/build.yml` runs on `windows-latest` for every push to `main` and every Pull request: real WPF build, self-contained win-x64 publish, then `makensis`. The installer is attached to the run (*Actions › run › Artifacts*).

Publishing a version:
1. change `<Version>` in `Directory.Build.props` (and `AssemblyVersion` / `FileVersion`);
2. update `docs/CHANGELOG.md`;
3. `git tag v<version> && git push origin v<version>` (or create the release on GitHub: *Releases › Draft a new release*, new tag `v<version>`): the workflow builds `FileWheel-Setup-<version>.exe` and attaches it to the Release.

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

`FILEWHEEL_HOME` (environment variable) redirects settings and data to another folder (tests, portable version).
