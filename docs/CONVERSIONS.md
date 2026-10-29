# Conversions prises en charge par FileWheel

Liste **générée automatiquement** à partir des capacités déclarées par chaque fournisseur (`dotnet run --project tests/FileWheel.Tests -- --catalogue docs/CONVERSIONS.md`).
Une conversion n'apparaît dans la roue que si son moteur est présent ; sinon elle est grisée avec le nom du moteur à installer.
Les conversions par les codecs Windows (WIC) ne figurent pas ici : elles dépendent des codecs installés sur le PC (PNG, JPEG, BMP, TIFF, GIF, ICO, et selon les extensions du Store : HEIC, WebP, AVIF, JPEG XR, RAW).

Les formats d'images ImageMagick listés ici sont ceux de l'environnement de test ; sous Windows, la liste réelle est relue au démarrage depuis l'ImageMagick installé (`magick -list format`) et peut être plus longue.

Légende des pertes : **aucune** = contenu conservé ; **qualité** = compression avec perte (image, son, vidéo) ; **structure** = mise en forme, métadonnées ou éléments que le format cible ne sait pas représenter. Le détail est affiché avant chaque conversion.

## Documents (intégré)

Moteur : intégré (aucun programme externe). 9 conversion(s).

| Depuis | Vers | Type | Perte | Remarques |
|---|---|---|---|---|
| md | html | conversion | aucune | Le HTML brut éventuel est échappé (jamais exécuté). |
| txt | html | conversion | aucune |  |
| epub | html | conversion | structure / mise en forme | Un seul fichier HTML (chapitres à la suite, images incluses). Mise en page d'origine approximative. Livres protégés par DRM refusés. |
| txt | pdf | conversion | aucune | Polices standard PDF (Helvetica/Courier) : les caractères hors alphabet latin occidental sont remplacés par « ? ». |
| jpg, png | pdf | conversion | aucune | JPEG intégré tel quel (aucune recompression), PNG intégré sans perte. |
| jpg, png | pdf | assemblage | aucune | **Assembler les images en un PDF** — Une page par image, dans l'ordre de la liste. Plusieurs fichiers → un seul. |
| md | txt | conversion | aucune |  |
| html | txt | conversion | structure / mise en forme | Seul le texte est conservé (mise en forme, images et liens perdus). |
| epub | txt | conversion | structure / mise en forme | Texte seul. Livres protégés par DRM refusés. |

## Données (intégré)

Moteur : intégré (aucun programme externe). 7 conversion(s).

| Depuis | Vers | Type | Perte | Remarques |
|---|---|---|---|---|
| csv, json, tsv, xlsx, xml, yaml | csv | conversion | aucune | Les structures imbriquées sont aplaties selon la stratégie choisie. |
| xlsx | dossier de csv | découpage | aucune | **Toutes les feuilles en CSV (un fichier par feuille)** |
| csv, tsv, xlsx, xml, yaml | json | conversion | aucune |  |
| csv, json, tsv, xlsx, xml, yaml | tsv | conversion | aucune | Les structures imbriquées sont aplaties selon la stratégie choisie. |
| csv, json, tsv, xml, yaml | xlsx | conversion | aucune | Les structures imbriquées sont aplaties selon la stratégie choisie. |
| csv, json, tsv, xlsx, yaml | xml | conversion | aucune | Convention : attributs « @nom », texte « #text », éléments répétés en tableaux. |
| csv, json, tsv, xlsx, xml | yaml | conversion | aucune |  |

## 3D (intégré)

Moteur : intégré (aucun programme externe). 5 conversion(s).

| Depuis | Vers | Type | Perte | Remarques |
|---|---|---|---|---|
| glb, gltf, obj, ply, stl | 3mf | conversion | structure / mise en forme | Géométrie seule : matériaux, couleurs, textures, normales et animations ne sont pas conservés. Unité 3MF : millimètre (les fichiers STL/OBJ n'ont pas d'unité, les valeurs sont recopiées telles quelles). |
| 3mf, gltf, obj, ply, stl | glb | conversion | structure / mise en forme | Géométrie seule : matériaux, couleurs, textures, normales et animations ne sont pas conservés. |
| 3mf, glb, gltf, ply, stl | obj | conversion | structure / mise en forme | Géométrie seule : matériaux, couleurs, textures, normales et animations ne sont pas conservés. |
| 3mf, glb, gltf, obj, stl | ply | conversion | structure / mise en forme | Géométrie seule : matériaux, couleurs, textures, normales et animations ne sont pas conservés. |
| 3mf, glb, gltf, obj, ply | stl | conversion | structure / mise en forme | Géométrie seule : matériaux, couleurs, textures, normales et animations ne sont pas conservés. |

## Polices (intégré)

Moteur : intégré (aucun programme externe). 3 conversion(s).

| Depuis | Vers | Type | Perte | Remarques |
|---|---|---|---|---|
| woff | otf | conversion | aucune | Décompression sans perte. Si la police contient des contours TrueType, le résultat est une police TTF. |
| woff | ttf | conversion | aucune | Décompression sans perte. Si la police contient des contours CFF, le résultat est une police OpenType (OTF). |
| otf, ttf | woff | conversion | aucune | Tables recopiées à l'identique, compressées (sans perte). |

## E-mails, contacts, agendas (intégré)

Moteur : intégré (aucun programme externe). 5 conversion(s).

| Depuis | Vers | Type | Perte | Remarques |
|---|---|---|---|---|
| vcf | csv | conversion | aucune | Champs courants (nom, e-mails, téléphones, adresse, organisation…) ; photos non conservées. |
| ics | csv | conversion | aucune | Un événement par ligne ; rappels et invités non conservés, règles de répétition recopiées telles quelles. |
| mbox | dossier de eml | découpage | aucune | **Séparer en messages EML (un fichier par message)** |
| eml | html | conversion | structure / mise en forme | Scripts et images distantes retirés ; pièces jointes seulement listées. |
| eml | txt | conversion | structure / mise en forme | Texte du message et en-têtes ; pièces jointes seulement listées. |

## Archives (intégré + 7-Zip)

Moteur : 7-Zip. 5 conversion(s).

| Depuis | Vers | Type | Perte | Remarques |
|---|---|---|---|---|
| bz2, gz, rar, tar, tgz, xz, zip | 7z | recompression | aucune | **Convertir l'archive en 7Z** — Le contenu est extrait puis recompressé ; l'archive d'origine est conservée. |
| gz, tar, tgz, zip | dossier de extract | extraction | aucune | **Extraire dans un dossier** — Chemins dangereux et liens ignorés ; plafonds de taille appliqués. |
| 7z, bz2, rar, xz | dossier de extract | extraction | aucune | **Extraire dans un dossier** — RAR : uniquement si 7-Zip est installé sur le PC (lecture seule, la création de RAR n'est pas possible). |
| 7z, bz2, gz, rar, tar, xz, zip | tgz | recompression | aucune | **Convertir l'archive en TAR.GZ** — Le contenu est extrait puis recompressé ; l'archive d'origine est conservée. |
| 7z, bz2, gz, rar, tar, tgz, xz | zip | recompression | aucune | **Convertir l'archive en ZIP** — Le contenu est extrait puis recompressé ; l'archive d'origine est conservée. |

## PDFium (PDF)

Moteur : PDFium, ImageMagick. 3 conversion(s).

| Depuis | Vers | Type | Perte | Remarques |
|---|---|---|---|---|
| pdf | dossier de jpg | pages en images | qualité (compression) | **Pages en images JPEG** |
| pdf | dossier de png | pages en images | aucune | **Pages en images PNG** — Rendu sans perte à la résolution choisie (le texte devient une image). |
| pdf | txt | extraction de texte | structure / mise en forme | **Extraire le texte (TXT)** — Extraction du texte : mise en page, images et tableaux non conservés. Sans reconnaissance optique (OCR) : un PDF scanné ne contient pas de texte extractible. |

## FFmpeg (audio et vidéo)

Moteur : FFmpeg. 42 conversion(s).

| Depuis | Vers | Type | Perte | Remarques |
|---|---|---|---|---|
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | aac | conversion | qualité (compression) |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | aac | extraction audio | qualité (compression) | **Extraire l'audio (AAC (ADTS))** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | aac | assemblage | qualité (compression) | **Fusionner en un seul AAC (ADTS)** — Plusieurs fichiers → un seul. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | aif | conversion | aucune |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | aif | extraction audio | aucune | **Extraire l'audio (AIFF)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | aif | assemblage | aucune | **Fusionner en un seul AIFF** — Plusieurs fichiers → un seul. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | avi | conversion | qualité (compression) | Copie sans perte (remuxage) quand les codecs sont compatibles ; sinon réencodage avec perte. Le mode utilisé est indiqué avant le lancement. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | flac | conversion | aucune |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | flac | extraction audio | aucune | **Extraire l'audio (FLAC)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | flac | assemblage | aucune | **Fusionner en un seul FLAC** — Plusieurs fichiers → un seul. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | flv | conversion | qualité (compression) | Copie sans perte (remuxage) quand les codecs sont compatibles ; sinon réencodage avec perte. Le mode utilisé est indiqué avant le lancement. |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | gif | conversion | qualité (compression) | **Convertir en GIF animé** — GIF : palette de 256 couleurs, pas de son. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | jpg | miniature | qualité (compression) | **Miniature (JPG)** |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | dossier de jpg | séquence d'images | qualité (compression) | **Séquence d'images (JPG)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | m4a | conversion | qualité (compression) |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | m4a | extraction audio | qualité (compression) | **Extraire l'audio (M4A (AAC/ALAC))** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | m4a | assemblage | qualité (compression) | **Fusionner en un seul M4A (AAC/ALAC)** — Plusieurs fichiers → un seul. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | m4v | conversion | qualité (compression) | Copie sans perte (remuxage) quand les codecs sont compatibles ; sinon réencodage avec perte. Le mode utilisé est indiqué avant le lancement. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | mkv | conversion | qualité (compression) | Copie sans perte (remuxage) quand les codecs sont compatibles ; sinon réencodage avec perte. Le mode utilisé est indiqué avant le lancement. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | mov | conversion | qualité (compression) | Copie sans perte (remuxage) quand les codecs sont compatibles ; sinon réencodage avec perte. Le mode utilisé est indiqué avant le lancement. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | mp3 | conversion | qualité (compression) |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | mp3 | extraction audio | qualité (compression) | **Extraire l'audio (MP3)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | mp3 | assemblage | qualité (compression) | **Fusionner en un seul MP3** — Plusieurs fichiers → un seul. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | mp4 | conversion | qualité (compression) | Copie sans perte (remuxage) quand les codecs sont compatibles ; sinon réencodage avec perte. Le mode utilisé est indiqué avant le lancement. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | mpg | conversion | qualité (compression) | Copie sans perte (remuxage) quand les codecs sont compatibles ; sinon réencodage avec perte. Le mode utilisé est indiqué avant le lancement. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | ogg | conversion | qualité (compression) |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | ogg | extraction audio | qualité (compression) | **Extraire l'audio (Ogg Vorbis)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | ogg | assemblage | qualité (compression) | **Fusionner en un seul Ogg Vorbis** — Plusieurs fichiers → un seul. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | opus | conversion | qualité (compression) |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | opus | extraction audio | qualité (compression) | **Extraire l'audio (Opus)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | opus | assemblage | qualité (compression) | **Fusionner en un seul Opus** — Plusieurs fichiers → un seul. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | png | miniature | aucune | **Miniature (PNG)** |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | dossier de png | séquence d'images | aucune | **Séquence d'images (PNG)** |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | ts | conversion | qualité (compression) | Copie sans perte (remuxage) quand les codecs sont compatibles ; sinon réencodage avec perte. Le mode utilisé est indiqué avant le lancement. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | wav | conversion | aucune |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | wav | extraction audio | aucune | **Extraire l'audio (WAV)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | wav | assemblage | aucune | **Fusionner en un seul WAV** — Plusieurs fichiers → un seul. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | webm | conversion | qualité (compression) | Copie sans perte (remuxage) quand les codecs sont compatibles ; sinon réencodage avec perte. Le mode utilisé est indiqué avant le lancement. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | wma | conversion | qualité (compression) |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | wma | extraction audio | qualité (compression) | **Extraire l'audio (Windows Media Audio (WMA))** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | wma | assemblage | qualité (compression) | **Fusionner en un seul Windows Media Audio (WMA)** — Plusieurs fichiers → un seul. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | wmv | conversion | qualité (compression) | Copie sans perte (remuxage) quand les codecs sont compatibles ; sinon réencodage avec perte. Le mode utilisé est indiqué avant le lancement. |

## ImageMagick (images)

Moteur : ImageMagick. 15 conversion(s).

| Depuis | Vers | Type | Perte | Remarques |
|---|---|---|---|---|
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | avif | conversion | qualité (compression) | AVIF compresse avec perte (qualité réglable). |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | bmp | conversion | aucune |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | dds | conversion | aucune |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | exr | conversion | aucune |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | gif | conversion | aucune |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | heic | conversion | qualité (compression) | HEIC/HEIF compresse avec perte (qualité réglable). |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | ico | conversion | aucune | Icône multi-tailles 16 à 256 px. |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | jpg | conversion | qualité (compression) | JPEG compresse avec perte (qualité réglable). |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | pdf | conversion | aucune | **Convertir en PDF** |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | pdf | assemblage | aucune | **Assembler les images en un PDF** — Plusieurs fichiers → un seul. |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | png | conversion | aucune |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | psd | conversion | aucune |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | tga | conversion | aucune |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | tif | conversion | aucune |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | webp | conversion | aucune |  |

## LibreOffice (documents)

Moteur : LibreOffice, PDFium. 18 conversion(s).

| Depuis | Vers | Type | Perte | Remarques |
|---|---|---|---|---|
| ods, xls, xlsx | csv | conversion | aucune | CSV : seule la première feuille est exportée, sans formules ni mise en forme. Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| docx, html, md, odt, rtf, txt | doc | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| doc, html, md, odt, rtf, txt | docx | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| pdf | docx | conversion | structure / mise en forme | **Reconstruire en DOCX modifiable** — Reconstruction approximative : le texte est placé dans des zones positionnées. Un PDF complexe (colonnes, tableaux) ne sera pas fidèle. PDF scanné : pas de texte reconnu (pas d'OCR). |
| doc, docx, html, md, odt, rtf, txt | epub | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| doc, docx, md, odt, rtf, txt | html | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| ppt, pptx | odp | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| csv, xls, xlsx | ods | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| doc, docx, html, md, rtf, txt | odt | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| pdf | odt | conversion | structure / mise en forme | **Reconstruire en ODT modifiable** — Reconstruction approximative (zones de texte positionnées). |
| csv, doc, docx, html, md, odp, ods, odt, ppt, pptx, rtf, txt, xls, xlsx | pdf | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| odp, ppt, pptx | dossier de png | pages en images | aucune | **Diapositives en images PNG** — Chaque diapositive devient une image (animations perdues). |
| odp, pptx | ppt | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| odp, ppt | pptx | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| doc, docx, html, md, odt, txt | rtf | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| doc, docx, html, md, odt, rtf | txt | conversion | structure / mise en forme | Texte seul : la mise en forme est perdue. Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| csv, ods, xlsx | xls | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |
| csv, ods, xls | xlsx | conversion | aucune | Mise en page préservée au mieux ; des différences de polices ou de rendu sont possibles selon les polices installées. |

## Calibre (livres numériques)

Moteur : Calibre (ebook-convert). 7 conversion(s).

| Depuis | Vers | Type | Perte | Remarques |
|---|---|---|---|---|
| docx, epub, fb2, html, mobi, odt, pdf, rtf, txt | azw3 | conversion | structure / mise en forme | Mise en page adaptée par Calibre : polices et éléments interactifs peuvent changer. Livres protégés par DRM refusés. Entrée PDF : extraction approximative du texte. |
| azw3, epub, fb2, html, mobi, odt, pdf, rtf, txt | docx | conversion | structure / mise en forme | Mise en page adaptée par Calibre : polices et éléments interactifs peuvent changer. Livres protégés par DRM refusés. Entrée PDF : extraction approximative du texte. |
| azw3, docx, fb2, html, mobi, odt, pdf, rtf, txt | epub | conversion | structure / mise en forme | Mise en page adaptée par Calibre : polices et éléments interactifs peuvent changer. Livres protégés par DRM refusés. Entrée PDF : extraction approximative du texte. |
| azw3, docx, epub, html, mobi, odt, pdf, rtf, txt | fb2 | conversion | structure / mise en forme | Mise en page adaptée par Calibre : polices et éléments interactifs peuvent changer. Livres protégés par DRM refusés. Entrée PDF : extraction approximative du texte. |
| azw3, docx, epub, fb2, html, odt, pdf, rtf, txt | mobi | conversion | structure / mise en forme | Mise en page adaptée par Calibre : polices et éléments interactifs peuvent changer. Livres protégés par DRM refusés. Entrée PDF : extraction approximative du texte. |
| azw3, docx, epub, fb2, html, mobi, odt, rtf, txt | pdf | conversion | structure / mise en forme | Mise en page recalculée : pagination différente de l'original. Livres protégés par DRM refusés. Entrée PDF : extraction approximative du texte. |
| azw3, docx, epub, fb2, html, mobi, odt, pdf, rtf | txt | conversion | structure / mise en forme | Texte seul : images et mise en forme perdues. Livres protégés par DRM refusés. Entrée PDF : extraction approximative du texte. |

