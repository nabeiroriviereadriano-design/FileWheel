# Contributing to FileWheel

Thanks for your interest! FileWheel is free software (MIT). Discussions can be in English or French. The application's user interface is in French.

## Reporting a problem

Open an *Issue* with:
- the FileWheel version (*Paramètres › À propos*, i.e. Settings › About) and the Windows version;
- what you were doing (file type, File Explorer or Desktop, key used) and what happened;
- the log: FileWheel tray icon menu › *Dépannage › Ouvrir le dossier des journaux* (Troubleshooting › Open the logs folder). Diagnostic mode (*Dépannage › Mode diagnostic*) adds details about the gesture.

Read the log before attaching it: it contains file and folder names.

Security vulnerability: do not disclose it in a public Issue; use the repository's *Security › Report a vulnerability*.

## Proposing a change

1. Create a branch from `main`.
2. Follow the architecture ([docs/DEVELOPER.md](docs/DEVELOPER.md)):
   - pure logic (gesture, wheel, file operations, settings) → `src/FileWheel.Core`, **with a test**;
   - conversions → a provider in `src/FileWheel.Conversion/Providers` that **declares** its real capabilities (including losses and limits), plus a test that checks the **content** of the output;
   - Windows interop → `src/FileWheel.Windows`; user interface → `src/FileWheel.App` (WPF written in C#, styles from `Ui/Theme.cs`, see [docs/DESIGN.md](docs/DESIGN.md)).
3. Check:
   - on Windows: `BUILD-SETUP.bat` compiles everything and produces the installer;
   - on Linux: `build/ci-linux.sh` compiles the engine and runs the tests;
   - GitHub Actions compiles the Windows version and the installer for every Pull request.
4. If you add a conversion: `dotnet run --project tests/FileWheel.Tests -- --catalogue docs/CONVERSIONS.md`.

## Project rules

- Never break native Windows drag and drop.
- Never modify or overwrite an original; write to a temporary file, then rename it to a free name.
- Nothing is sent over the Internet; an external engine is only downloaded with the user's explicit consent, from its official source, with SHA-256 verification.
- No conversion is advertised unless it is really implemented and tested.
- The installer always ships with its uninstaller (`Uninstall.exe`, "Installed apps" entry).
- No third-party program is added to the repository.

By submitting a contribution, you agree that it is published under the MIT license.
