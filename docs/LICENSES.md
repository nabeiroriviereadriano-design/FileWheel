# Licenses

## FileWheel

FileWheel source code: **MIT** license (see `LICENSE.txt`).

No third-party library is included or statically linked: the program only uses .NET 10 (MIT) and WPF (MIT). Interface icons come from the **Segoe Fluent Icons / Segoe MDL2 Assets** font shipped with Windows (not redistributed). The application icon is an original creation.

## External engines (not included, installed on request)

| Engine | Role | License | Source | Usage |
|---|---|---|---|---|
| FFmpeg ("essentials" build by gyan.dev) | Audio, video | GPL v3 | github.com/GyanD/codexffmpeg | Separate program |
| ImageMagick (portable Q16) | Images | ImageMagick License (Apache 2.0-style) | github.com/ImageMagick/ImageMagick | Separate program |
| 7-Zip ("extra") | 7Z, XZ, BZ2, RAR (read) archives | GNU LGPL 2.1; unRAR restriction for the RAR code | github.com/ip7z/7zip | Separate program |
| PDFium (bblanchon/pdfium-binaries) | PDF rendering and text | BSD 3-Clause / Apache 2.0 | github.com/bblanchon/pdfium-binaries | Dynamically loaded library |
| LibreOffice | Office documents | MPL 2.0 | libreoffice.org (winget) | Separate program |
| Calibre | E-books | GPL v3 | calibre-ebook.com (winget) | Separate program |

FileWheel redistributes none of these engines: they are downloaded from their official source, after your consent, and remain subject to their own license. GPL programs run as independent processes, with no linking to FileWheel's code.
