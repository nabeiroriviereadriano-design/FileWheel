# -*- coding: utf-8 -*-
"""Generation des fichiers d'essai qui demandent un controle fin du contenu
(archives malveillantes, EXIF, e-mails MIME, EPUB...). Appele par le harnais C#."""
import io, os, sys, tarfile, zipfile, struct, json

out = sys.argv[1]
def p(name): return os.path.join(out, name)

# --- JPEG avec EXIF (orientation 6, fabricant, GPS) ------------------------------
from PIL import Image
img = Image.new("RGB", (120, 80), (200, 60, 40))
for x in range(60):
    for y in range(80):
        img.putpixel((x, y), (40, 120, 220))
exif = Image.Exif()
exif[0x0112] = 6            # Orientation : rotation 90 degres
exif[0x010F] = "FileWheelCam"  # Make
exif[0x8825] = {1: "N", 2: (48.0, 51.0, 24.0), 3: "E", 4: (2.0, 21.0, 3.0)}  # GPS
img.save(p("photo exif.jpg"), "JPEG", quality=92, exif=exif)

# PNG transparent (RGBA) et palette 4 bits entrelacee
rgba = Image.new("RGBA", (64, 48), (0, 0, 0, 0))
for x in range(16, 48):
    for y in range(8, 40):
        rgba.putpixel((x, y), (255, 0, 0, 255))
rgba.save(p("logo transparent.png"))
pal = Image.new("P", (33, 17))
pal.putpalette([0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 255, 0] + [0] * (252 * 3))
for x in range(33):
    for y in range(17):
        pal.putpixel((x, y), (x + y) % 4)
pal.save(p("palette entrelacée.png"), interlace=1, bits=4)
Image.new("L", (40, 30), 128).save(p("gris 16 bits.png"))
Image.new("I;16", (40, 30), 30000).save(p("gris16.png"))

# GIF anime 3 images
frames = [Image.new("RGB", (40, 40), (i * 100, 50, 200 - i * 60)) for i in range(3)]
frames[0].save(p("anime.gif"), save_all=True, append_images=frames[1:], duration=100, loop=0)

# --- Archives ------------------------------------------------------------------
with zipfile.ZipFile(p("photos été.zip"), "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("dossier/é à ç.txt", "contenu accentué\n")
    z.writestr("dossier/sous/b.txt", "b\n")
    z.writestr("racine.txt", "r\n")
with zipfile.ZipFile(p("piège zip slip.zip"), "w") as z:
    z.writestr("normal.txt", "ok")
    z.writestr("../../evasion.txt", "ne doit pas sortir")
    z.writestr("/tmp/absolu.txt", "absolu")
    z.writestr("C:/Windows/lecteur.txt", "lecteur")
    z.writestr("flux.txt:cache", "flux alternatif")
with zipfile.ZipFile(p("bombe.zip"), "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("zeros.bin", b"\0" * (80 * 1024 * 1024))
with tarfile.open(p("liens.tar.gz"), "w:gz") as t:
    data = b"fichier normal\n"
    ti = tarfile.TarInfo("archive/normal.txt"); ti.size = len(data); t.addfile(ti, io.BytesIO(data))
    sl = tarfile.TarInfo("archive/lien-vers-etc"); sl.type = tarfile.SYMTYPE; sl.linkname = "/etc/passwd"; t.addfile(sl)
    hl = tarfile.TarInfo("archive/lien-dur"); hl.type = tarfile.LNKTYPE; hl.linkname = "archive/normal.txt"; t.addfile(hl)
    ev = tarfile.TarInfo("../evasion-tar.txt"); ev.size = 3; t.addfile(ev, io.BytesIO(b"bad"))

# --- E-mails, contacts, agendas -----------------------------------------------
eml = (
    "From: =?utf-8?B?w4lsb8Ovc2UgTWFydGlu?= <eloise@example.org>\r\n"
    "To: Destinataire <destinataire@example.org>\r\n"
    "Subject: =?iso-8859-1?Q?R=E9union_de_d=E9cembre?=\r\n"
    "Date: Mon, 01 Dec 2025 10:00:00 +0100\r\n"
    "MIME-Version: 1.0\r\n"
    "Content-Type: multipart/mixed; boundary=\"XX\"\r\n\r\n"
    "--XX\r\nContent-Type: multipart/alternative; boundary=\"YY\"\r\n\r\n"
    "--YY\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: quoted-printable\r\n\r\n"
    "Bonjour, la r=C3=A9union est d=C3=A9plac=C3=A9e.\r\n"
    "--YY\r\nContent-Type: text/html; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n"
)
import base64
html = '<html><body><p onclick="alert(1)">Bonjour, la réunion est <b>déplacée</b>.</p><img src="https://pixel.example.com/t.gif"><script>alert(2)</script></body></html>'
eml += base64.encodebytes(html.encode("utf-8")).decode().replace("\n", "\r\n") + "--YY--\r\n"
eml += "--XX\r\nContent-Type: application/pdf; name=\"ordre du jour.pdf\"\r\nContent-Disposition: attachment; filename=\"ordre du jour.pdf\"\r\nContent-Transfer-Encoding: base64\r\n\r\nJVBERi0xLjQK\r\n--XX--\r\n"
open(p("message.eml"), "wb").write(eml.encode("latin-1"))
mbox = ""
for i in range(3):
    mbox += f"From sender{i}@example.org Mon Dec  1 10:0{i}:00 2025\nFrom: s{i}@example.org\nSubject: Message {i + 1}\n\nCorps {i + 1}\n>From the archive\n\n"
open(p("boite.mbox"), "w").write(mbox)
vcf = ("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Élodie Durand\r\nN:Durand;Élodie;;;\r\nEMAIL;TYPE=work:elodie@example.org\r\n"
       "TEL;TYPE=cell:+33 6 00 00 00 00\r\nORG:Froid Industriel SA\r\nADR;TYPE=work:;;12 rue du Givre;Lyon;;69000;France\r\n"
       "NOTE:Ligne 1\\nLigne 2 avec une virgule\\, ok\r\nEND:VCARD\r\n"
       "BEGIN:VCARD\r\nVERSION:2.1\r\nN:Martin;Paul\r\nFN;ENCODING=QUOTED-PRINTABLE;CHARSET=UTF-8:Paul =C3=89mile Martin\r\nTEL;HOME:0102030405\r\nEND:VCARD\r\n")
open(p("contacts.vcf"), "wb").write(vcf.encode("utf-8"))
ics = ("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Test//FR\r\n"
       "BEGIN:VEVENT\r\nUID:1@test\r\nSUMMARY:Maintenance compresseur\r\nDTSTART:20251201T080000Z\r\nDTEND:20251201T100000Z\r\nLOCATION:Atelier\\, bâtiment B\r\n"
       "DESCRIPTION:Vérifier\\nla pression\r\nBEGIN:VALARM\r\nTRIGGER:-PT15M\r\nACTION:DISPLAY\r\nEND:VALARM\r\nEND:VEVENT\r\n"
       "BEGIN:VEVENT\r\nUID:2@test\r\nSUMMARY:Congés\r\nDTSTART;VALUE=DATE:20251222\r\nDTEND;VALUE=DATE:20260105\r\nRRULE:FREQ=YEARLY\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n")
open(p("agenda.ics"), "wb").write(ics.encode("utf-8"))

# --- EPUB minimal (et version « protegee ») -------------------------------------
def epub(name, drm=False):
    with zipfile.ZipFile(p(name), "w") as z:
        z.writestr(zipfile.ZipInfo("mimetype"), "application/epub+zip")
        z.writestr("META-INF/container.xml", '<?xml version="1.0"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>')
        z.writestr("OEBPS/content.opf", '<?xml version="1.0"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>Le Froid et la Chaleur</dc:title><dc:creator>Anonyme</dc:creator><dc:identifier id="id">x</dc:identifier><dc:language>fr</dc:language></metadata><manifest><item id="c1" href="ch1.xhtml" media-type="application/xhtml+xml"/><item id="c2" href="texte/ch2.xhtml" media-type="application/xhtml+xml"/><item id="img" href="images/pixel.png" media-type="image/png"/><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/></manifest><spine><itemref idref="c1"/><itemref idref="c2"/></spine></package>')
        z.writestr("OEBPS/nav.xhtml", '<?xml version="1.0"?><html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>nav</title></head><body><nav epub:type="toc"><ol><li><a href="ch1.xhtml">Un</a></li><li><a href="texte/ch2.xhtml">Deux</a></li></ol></nav></body></html>')
        z.writestr("OEBPS/ch1.xhtml", '<?xml version="1.0" encoding="utf-8"?><html xmlns="http://www.w3.org/1999/xhtml"><head><title>Un</title></head><body><h1>Chapitre un</h1><p>Le compresseur ronronne doucement.</p><script>alert(1)</script></body></html>')
        z.writestr("OEBPS/texte/ch2.xhtml", '<?xml version="1.0" encoding="utf-8"?><html xmlns="http://www.w3.org/1999/xhtml"><head><title>Deux</title></head><body><h1>Chapitre deux</h1><p>L\'évaporateur givre.</p><img src="../images/pixel.png" alt="pixel"/></body></html>')
        buf = io.BytesIO(); Image.new("RGB", (4, 4), (0, 128, 255)).save(buf, "PNG")
        z.writestr("OEBPS/images/pixel.png", buf.getvalue())
        if drm:
            z.writestr("META-INF/encryption.xml", '<?xml version="1.0"?><encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container" xmlns:enc="http://www.w3.org/2001/04/xmlenc#"><enc:EncryptedData><enc:EncryptionMethod Algorithm="http://www.w3.org/2001/04/xmlenc#aes128-cbc"/><enc:CipherData><enc:CipherReference URI="OEBPS/ch1.xhtml"/></enc:CipherData></enc:EncryptedData></encryption>')
epub("livre.epub")
epub("livre protégé.epub", drm=True)

# --- Programme deguise en image ------------------------------------------------
pe = bytearray(512)
pe[0:2] = b"MZ"
struct.pack_into("<I", pe, 0x3C, 0x80)
pe[0x80:0x84] = b"PE\0\0"
open(p("vacances.jpg"), "wb").write(bytes(pe))
print("ok")
