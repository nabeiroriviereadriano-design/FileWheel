#!/bin/sh
# Verification hors Windows (Linux) :
#  1. compile le moteur (Core + Conversion) et execute la suite de tests reelle ;
#  2. compile les projets Windows (FileWheel.Windows, FileWheel.App) en mode VERIFICATION DE TYPES
#     contre build/wpfref/WpfRefAll.dll (assemblee de reference WPF, non distribuee) si elle est presente.
# Outils utilises par les tests s'ils sont installes : ffmpeg, ImageMagick (convert/identify), 7z,
# LibreOffice (soffice), Calibre (ebook-convert), poppler-utils (pdftotext), python3 (+ pyyaml, fonttools,
# pypdfium2, Pillow). Les tests qui dependent d'un outil absent sont marques « non executes ».
set -eu
cd "$(dirname "$0")/.."
dotnet build src/FileWheel.Core -c Release -nologo -v q
dotnet build src/FileWheel.Conversion -c Release -nologo -v q
if [ -f build/wpfref/WpfRefAll.dll ]; then
  dotnet build src/FileWheel.App -c Release -nologo -v q
  echo "Projets Windows : verification de types OK (binaire non executable hors Windows)."
else
  echo "build/wpfref/WpfRefAll.dll absent : verification des projets Windows ignoree."
fi
dotnet run --project tests/FileWheel.Tests -c Release
