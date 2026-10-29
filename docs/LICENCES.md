# Licences

## FileWheel

Code source de FileWheel : licence **MIT** (voir `LICENSE.txt`).

Aucune bibliothèque tierce n'est incluse ni liée statiquement : le programme n'utilise que .NET 10 (MIT) et WPF (MIT). Les icônes de l'interface viennent de la police **Segoe Fluent Icons / Segoe MDL2 Assets** fournie par Windows (non redistribuée). L'icône de l'application est une création originale.

## Moteurs externes (non inclus, installés à votre demande)

| Moteur | Rôle | Licence | Source | Mode d'utilisation |
|---|---|---|---|---|
| FFmpeg (build « essentials » de gyan.dev) | Audio, vidéo | GPL v3 | github.com/GyanD/codexffmpeg | Programme séparé |
| ImageMagick (portable Q16) | Images | Licence ImageMagick (type Apache 2.0) | github.com/ImageMagick/ImageMagick | Programme séparé |
| 7-Zip (« extra ») | Archives 7Z, XZ, BZ2, RAR (lecture) | GNU LGPL 2.1 ; restriction unRAR pour le code RAR | github.com/ip7z/7zip | Programme séparé |
| PDFium (bblanchon/pdfium-binaries) | Rendu et texte PDF | BSD 3 clauses / Apache 2.0 | github.com/bblanchon/pdfium-binaries | Bibliothèque chargée dynamiquement |
| LibreOffice | Documents bureautiques | MPL 2.0 | libreoffice.org (winget) | Programme séparé |
| Calibre | Livres numériques | GPL v3 | calibre-ebook.com (winget) | Programme séparé |

FileWheel ne redistribue aucun de ces moteurs : ils sont téléchargés depuis leur source officielle, après votre accord, et restent soumis à leur propre licence. Les programmes GPL sont exécutés comme des processus indépendants, sans liaison avec le code de FileWheel.
