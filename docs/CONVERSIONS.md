# Conversions supported by FileWheel

List **generated automatically** from the capabilities declared by each provider (`dotnet run --project tests/FileWheel.Tests -- --catalogue docs/CONVERSIONS.md`; the generator outputs French, the app's language — this file is its English translation).
A conversion only appears in the wheel if its engine is present; otherwise it is greyed out with the name of the engine to install.
Conversions through the Windows codecs (WIC) are not listed here: they depend on the codecs installed on the PC (PNG, JPEG, BMP, TIFF, GIF, ICO, and depending on Store extensions: HEIC, WebP, AVIF, JPEG XR, RAW).

The ImageMagick image formats listed here are those of the test environment; on Windows, the actual list is read at startup from the installed ImageMagick (`magick -list format`) and may be longer.

Loss legend: **none** = content kept; **quality** = lossy compression (image, sound, video); **structure** = formatting, metadata or elements the target format cannot represent. Details are shown before each conversion.

## Documents (built-in)

Engine: built-in (no external program). 9 conversion(s).

| From | To | Type | Loss | Notes |
|---|---|---|---|---|
| md | html | conversion | none | Any raw HTML is escaped (never executed). |
| txt | html | conversion | none |  |
| epub | html | conversion | structure / formatting | A single HTML file (chapters in sequence, images included). Original layout approximated. DRM-protected books refused. |
| txt | pdf | conversion | none | Standard PDF fonts (Helvetica/Courier): characters outside the Western Latin alphabet are replaced by "?". |
| jpg, png | pdf | conversion | none | JPEG embedded as is (no recompression), PNG embedded losslessly. |
| jpg, png | pdf | merge | none | **Combine the images into one PDF** — One page per image, in list order. Several files → one. |
| md | txt | conversion | none |  |
| html | txt | conversion | structure / formatting | Only the text is kept (formatting, images and links lost). |
| epub | txt | conversion | structure / formatting | Text only. DRM-protected books refused. |

## Data (built-in)

Engine: built-in (no external program). 7 conversion(s).

| From | To | Type | Loss | Notes |
|---|---|---|---|---|
| csv, json, tsv, xlsx, xml, yaml | csv | conversion | none | Nested structures are flattened according to the chosen strategy. |
| xlsx | folder of csv | split | none | **All sheets as CSV (one file per sheet)** |
| csv, tsv, xlsx, xml, yaml | json | conversion | none |  |
| csv, json, tsv, xlsx, xml, yaml | tsv | conversion | none | Nested structures are flattened according to the chosen strategy. |
| csv, json, tsv, xml, yaml | xlsx | conversion | none | Nested structures are flattened according to the chosen strategy. |
| csv, json, tsv, xlsx, yaml | xml | conversion | none | Convention: attributes "@name", text "#text", repeated elements as arrays. |
| csv, json, tsv, xlsx, xml | yaml | conversion | none |  |

## 3D (built-in)

Engine: built-in (no external program). 5 conversion(s).

| From | To | Type | Loss | Notes |
|---|---|---|---|---|
| glb, gltf, obj, ply, stl | 3mf | conversion | structure / formatting | Geometry only: materials, colors, textures, normals and animations are not kept. 3MF unit: millimeter (STL/OBJ files have no unit, values are copied as is). |
| 3mf, gltf, obj, ply, stl | glb | conversion | structure / formatting | Geometry only: materials, colors, textures, normals and animations are not kept. |
| 3mf, glb, gltf, ply, stl | obj | conversion | structure / formatting | Geometry only: materials, colors, textures, normals and animations are not kept. |
| 3mf, glb, gltf, obj, stl | ply | conversion | structure / formatting | Geometry only: materials, colors, textures, normals and animations are not kept. |
| 3mf, glb, gltf, obj, ply | stl | conversion | structure / formatting | Geometry only: materials, colors, textures, normals and animations are not kept. |

## Fonts (built-in)

Engine: built-in (no external program). 3 conversion(s).

| From | To | Type | Loss | Notes |
|---|---|---|---|---|
| woff | otf | conversion | none | Lossless decompression. If the font contains TrueType outlines, the result is a TTF font. |
| woff | ttf | conversion | none | Lossless decompression. If the font contains CFF outlines, the result is an OpenType font (OTF). |
| otf, ttf | woff | conversion | none | Tables copied identically, compressed (lossless). |

## E-mails, contacts, calendars (built-in)

Engine: built-in (no external program). 5 conversion(s).

| From | To | Type | Loss | Notes |
|---|---|---|---|---|
| vcf | csv | conversion | none | Common fields (name, e-mails, phones, address, organization…); photos not kept. |
| ics | csv | conversion | none | One event per row; reminders and attendees not kept, recurrence rules copied as is. |
| mbox | folder of eml | split | none | **Split into EML messages (one file per message)** |
| eml | html | conversion | structure / formatting | Scripts and remote images removed; attachments only listed. |
| eml | txt | conversion | structure / formatting | Message text and headers; attachments only listed. |

## Archives (built-in + 7-Zip)

Engine: 7-Zip. 5 conversion(s).

| From | To | Type | Loss | Notes |
|---|---|---|---|---|
| bz2, gz, rar, tar, tgz, xz, zip | 7z | recompression | none | **Convert the archive to 7Z** — The content is extracted then recompressed; the original archive is kept. |
| gz, tar, tgz, zip | folder of extract | extraction | none | **Extract to a folder** — Dangerous paths and links skipped; size limits applied. |
| 7z, bz2, rar, xz | folder of extract | extraction | none | **Extract to a folder** — RAR: only if 7-Zip is installed on the PC (read-only, creating RAR is not possible). |
| 7z, bz2, gz, rar, tar, xz, zip | tgz | recompression | none | **Convert the archive to TAR.GZ** — The content is extracted then recompressed; the original archive is kept. |
| 7z, bz2, gz, rar, tar, tgz, xz | zip | recompression | none | **Convert the archive to ZIP** — The content is extracted then recompressed; the original archive is kept. |

## PDFium (PDF)

Engine: PDFium, ImageMagick. 3 conversion(s).

| From | To | Type | Loss | Notes |
|---|---|---|---|---|
| pdf | folder of jpg | pages to images | quality (compression) | **Pages as JPEG images** |
| pdf | folder of png | pages to images | none | **Pages as PNG images** — Lossless rendering at the chosen resolution (text becomes an image). |
| pdf | txt | text extraction | structure / formatting | **Extract the text (TXT)** — Text extraction: layout, images and tables not kept. No optical character recognition (OCR): a scanned PDF contains no extractable text. |

## FFmpeg (audio and video)

Engine: FFmpeg. 42 conversion(s).

| From | To | Type | Loss | Notes |
|---|---|---|---|---|
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | aac | conversion | quality (compression) |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | aac | audio extraction | quality (compression) | **Extract the audio (AAC (ADTS))** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | aac | merge | quality (compression) | **Merge into a single AAC (ADTS)** — Several files → one. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | aif | conversion | none |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | aif | audio extraction | none | **Extract the audio (AIFF)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | aif | merge | none | **Merge into a single AIFF** — Several files → one. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | avi | conversion | quality (compression) | Lossless copy (remux) when the codecs are compatible; otherwise lossy re-encode. The mode used is shown before starting. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | flac | conversion | none |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | flac | audio extraction | none | **Extract the audio (FLAC)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | flac | merge | none | **Merge into a single FLAC** — Several files → one. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | flv | conversion | quality (compression) | Lossless copy (remux) when the codecs are compatible; otherwise lossy re-encode. The mode used is shown before starting. |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | gif | conversion | quality (compression) | **Convert to animated GIF** — GIF: 256-color palette, no sound. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | jpg | thumbnail | quality (compression) | **Thumbnail (JPG)** |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | folder of jpg | image sequence | quality (compression) | **Image sequence (JPG)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | m4a | conversion | quality (compression) |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | m4a | audio extraction | quality (compression) | **Extract the audio (M4A (AAC/ALAC))** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | m4a | merge | quality (compression) | **Merge into a single M4A (AAC/ALAC)** — Several files → one. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | m4v | conversion | quality (compression) | Lossless copy (remux) when the codecs are compatible; otherwise lossy re-encode. The mode used is shown before starting. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | mkv | conversion | quality (compression) | Lossless copy (remux) when the codecs are compatible; otherwise lossy re-encode. The mode used is shown before starting. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | mov | conversion | quality (compression) | Lossless copy (remux) when the codecs are compatible; otherwise lossy re-encode. The mode used is shown before starting. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | mp3 | conversion | quality (compression) |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | mp3 | audio extraction | quality (compression) | **Extract the audio (MP3)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | mp3 | merge | quality (compression) | **Merge into a single MP3** — Several files → one. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | mp4 | conversion | quality (compression) | Lossless copy (remux) when the codecs are compatible; otherwise lossy re-encode. The mode used is shown before starting. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | mpg | conversion | quality (compression) | Lossless copy (remux) when the codecs are compatible; otherwise lossy re-encode. The mode used is shown before starting. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | ogg | conversion | quality (compression) |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | ogg | audio extraction | quality (compression) | **Extract the audio (Ogg Vorbis)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | ogg | merge | quality (compression) | **Merge into a single Ogg Vorbis** — Several files → one. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | opus | conversion | quality (compression) |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | opus | audio extraction | quality (compression) | **Extract the audio (Opus)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | opus | merge | quality (compression) | **Merge into a single Opus** — Several files → one. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | png | thumbnail | none | **Thumbnail (PNG)** |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | folder of png | image sequence | none | **Image sequence (PNG)** |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | ts | conversion | quality (compression) | Lossless copy (remux) when the codecs are compatible; otherwise lossy re-encode. The mode used is shown before starting. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | wav | conversion | none |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | wav | audio extraction | none | **Extract the audio (WAV)** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | wav | merge | none | **Merge into a single WAV** — Several files → one. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | webm | conversion | quality (compression) | Lossless copy (remux) when the codecs are compatible; otherwise lossy re-encode. The mode used is shown before starting. |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | wma | conversion | quality (compression) |  |
| 3gp, avi, flv, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | wma | audio extraction | quality (compression) | **Extract the audio (Windows Media Audio (WMA))** |
| aac, aif, amr, flac, m4a, mp3, ogg, opus, wav, wma | wma | merge | quality (compression) | **Merge into a single Windows Media Audio (WMA)** — Several files → one. |
| 3gp, avi, flv, gif, m4v, mkv, mov, mp4, mpg, ts, webm, wmv | wmv | conversion | quality (compression) | Lossless copy (remux) when the codecs are compatible; otherwise lossy re-encode. The mode used is shown before starting. |

## ImageMagick (images)

Engine: ImageMagick. 15 conversion(s).

| From | To | Type | Loss | Notes |
|---|---|---|---|---|
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | avif | conversion | quality (compression) | AVIF is lossy (adjustable quality). |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | bmp | conversion | none |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | dds | conversion | none |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | exr | conversion | none |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | gif | conversion | none |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | heic | conversion | quality (compression) | HEIC/HEIF is lossy (adjustable quality). |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | ico | conversion | none | Multi-size icon, 16 to 256 px. |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | jpg | conversion | quality (compression) | JPEG is lossy (adjustable quality). |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | pdf | conversion | none | **Convert to PDF** |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | pdf | merge | none | **Combine the images into one PDF** — Several files → one. |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | png | conversion | none |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | psd | conversion | none |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | tga | conversion | none |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | tif | conversion | none |  |
| avif, bmp, dds, exr, gif, hdr, heic, ico, jpg, png, psd, cr2, svg, tga, tif, webp | webp | conversion | none |  |

## LibreOffice (documents)

Engine: LibreOffice, PDFium. 18 conversion(s).

| From | To | Type | Loss | Notes |
|---|---|---|---|---|
| ods, xls, xlsx | csv | conversion | none | CSV: only the first sheet is exported, without formulas or formatting. Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| docx, html, md, odt, rtf, txt | doc | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| doc, html, md, odt, rtf, txt | docx | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| pdf | docx | conversion | structure / formatting | **Rebuild as editable DOCX** — Approximate reconstruction: text is placed in positioned boxes. A complex PDF (columns, tables) will not be faithful. Scanned PDF: no text recognized (no OCR). |
| doc, docx, html, md, odt, rtf, txt | epub | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| doc, docx, md, odt, rtf, txt | html | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| ppt, pptx | odp | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| csv, xls, xlsx | ods | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| doc, docx, html, md, rtf, txt | odt | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| pdf | odt | conversion | structure / formatting | **Rebuild as editable ODT** — Approximate reconstruction (positioned text boxes). |
| csv, doc, docx, html, md, odp, ods, odt, ppt, pptx, rtf, txt, xls, xlsx | pdf | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| odp, ppt, pptx | folder of png | pages to images | none | **Slides as PNG images** — Each slide becomes an image (animations lost). |
| odp, pptx | ppt | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| odp, ppt | pptx | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| doc, docx, html, md, odt, txt | rtf | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| doc, docx, html, md, odt, rtf | txt | conversion | structure / formatting | Text only: formatting is lost. Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| csv, ods, xlsx | xls | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |
| csv, ods, xls | xlsx | conversion | none | Layout preserved as well as possible; font or rendering differences may occur depending on installed fonts. |

## Calibre (e-books)

Engine: Calibre (ebook-convert). 7 conversion(s).

| From | To | Type | Loss | Notes |
|---|---|---|---|---|
| docx, epub, fb2, html, mobi, odt, pdf, rtf, txt | azw3 | conversion | structure / formatting | Layout adapted by Calibre: fonts and interactive elements may change. DRM-protected books refused. PDF input: approximate text extraction. |
| azw3, epub, fb2, html, mobi, odt, pdf, rtf, txt | docx | conversion | structure / formatting | Layout adapted by Calibre: fonts and interactive elements may change. DRM-protected books refused. PDF input: approximate text extraction. |
| azw3, docx, fb2, html, mobi, odt, pdf, rtf, txt | epub | conversion | structure / formatting | Layout adapted by Calibre: fonts and interactive elements may change. DRM-protected books refused. PDF input: approximate text extraction. |
| azw3, docx, epub, html, mobi, odt, pdf, rtf, txt | fb2 | conversion | structure / formatting | Layout adapted by Calibre: fonts and interactive elements may change. DRM-protected books refused. PDF input: approximate text extraction. |
| azw3, docx, epub, fb2, html, odt, pdf, rtf, txt | mobi | conversion | structure / formatting | Layout adapted by Calibre: fonts and interactive elements may change. DRM-protected books refused. PDF input: approximate text extraction. |
| azw3, docx, epub, fb2, html, mobi, odt, rtf, txt | pdf | conversion | structure / formatting | Layout recomputed: pagination differs from the original. DRM-protected books refused. PDF input: approximate text extraction. |
| azw3, docx, epub, fb2, html, mobi, odt, pdf, rtf | txt | conversion | structure / formatting | Text only: images and formatting lost. DRM-protected books refused. PDF input: approximate text extraction. |

