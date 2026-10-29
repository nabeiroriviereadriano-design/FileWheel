# Security in FileWheel

## Principles

| Rule | Implementation |
|---|---|
| Never break native drag and drop | The wheel is an ordinary OLE drop target: it blocks no click, injects no mouse movement, and always answers "Copy" (never "Move") so that the source File Explorer never deletes the originals. Dropping elsewhere = normal Windows drop. |
| Originals untouched | Every conversion writes to a temporary `.~fw-*.tmp` file in the target folder, validates it, then renames it. An output can never target an input file. |
| No implicit overwrite | Name conflict = new name "(2)" by default; "Replace" only exists as an explicit choice. |
| Local by default | No conversion sends data over the network. The only network accesses are engine downloads you start yourself. |
| No unknown executable | Engines come from official repositories listed in the code, are verified by SHA-256 and installed into `%LOCALAPPDATA%\FileWheel\engines`. No automatic download. |
| No shell | All programs are started with an argument list (`ProcessStartInfo.ArgumentList`): no file name is ever interpreted by `cmd.exe` or PowerShell. |
| No elevation | `asInvoker`: FileWheel never asks for administrator rights. |

## Archives

- **Zip slip**: each entry is resolved and must stay inside the extraction folder; absolute paths, `..`, drive letters, NTFS streams (`:`) and reserved names (CON, NUL…) are skipped.
- Symbolic and hard **links**: skipped.
- **Decompression bombs**: limits on total size (20 GiB), number of entries (200,000) and compression ratio (×250), adjustable.
- **Never overwrite**: extraction into a new numbered folder if the name exists.
- Skipped entries are reported at the end and detailed in the log.

## Custom actions

- Limited types: run a program, copy/move to a folder, convert.
- "Run a program": absolute path required, **SHA-256 hash remembered** when saving; if the program changes, FileWheel asks for confirmation before running it.
- Warning when saving for interpreters (cmd, PowerShell, wscript, cscript, mshta, bash, WSL).
- Tokens `{path}`, `{name}`… become separate arguments; no shell interprets the line.

## Dangerous files

- Programs and scripts (`.exe`, `.bat`, `.ps1`, `.vbs`, `.js`, `.msi`…) are never converted; opening them from the wheel asks for confirmation.
- The real format is detected by signature (content), not only by extension.
- E-mails converted to HTML: scripts, event handlers and remote images removed.
- SVG: a file that declares XML entities or references an external resource (href, src, url()) is refused before any rendering by ImageMagick.

## Personal data

- History: file names, conversion, date and result — never the content. Can be disabled and cleared.
- Log: the user profile folder is masked (`%USERPROFILE%`), file content never appears; kept 14 days.

## Reporting a vulnerability

Use the repository's *Security › Report a vulnerability* rather than a public Issue.
