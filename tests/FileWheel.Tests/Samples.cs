using System.Text;
using static FileWheel.Tests.Env;

namespace FileWheel.Tests;

/// <summary>Creation des fichiers d'essai avec les outils reels (ffmpeg, ImageMagick, LibreOffice, Python).</summary>
public static class Samples
{
    public static List<string> Missing { get; } = new();

    public static void Generate()
    {
        var dir = Env.Samples;
        var py = Path.Combine(AppContext.BaseDirectory, "make_samples.py");
        Must("python3", py, dir);

        if (Which("ffmpeg") is not null)
        {
            Must("ffmpeg", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=4", "-f", "lavfi", "-i", "sine=frequency=440:duration=4",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", Sample("clip vidéo.mp4"));
            File.WriteAllText(Sample("sous-titres.srt"), "1\n00:00:00,500 --> 00:00:02,000\nBonjour à tous\n\n2\n00:00:02,500 --> 00:00:03,500\nFin\n");
            Must("ffmpeg", "-hide_banner", "-loglevel", "error", "-i", Sample("clip vidéo.mp4"), "-i", Sample("sous-titres.srt"),
                "-map", "0", "-map", "1", "-c", "copy", "-c:s", "srt", Sample("film.mkv"));
            Must("ffmpeg", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=160x120:rate=15:duration=2", "-c:v", "mpeg4", "-q:v", "5", Sample("ancien.avi"));
            Must("ffmpeg", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=40", "-f", "lavfi", "-i", "sine=duration=40",
                "-c:v", "libx264", "-preset", "veryslow", "-crf", "15", "-c:a", "aac", "-shortest", Sample("long.mp4"));
            Must("ffmpeg", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=660:duration=3", "-ac", "2", Sample("tonalité.wav"));
            var cover = Path.Combine(Root, "pochette.png");
            Must("ffmpeg", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=red:s=64x64", "-frames:v", "1", cover);
            var raw = Path.Combine(Root, "chanson-brute.mp3");
            Must("ffmpeg", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=330:duration=2", "-c:a", "libmp3lame", "-b:a", "128k", raw);
            Must("ffmpeg", "-hide_banner", "-loglevel", "error", "-i", raw, "-i", cover, "-map", "0:a", "-map", "1:v", "-c:a", "copy", "-c:v", "mjpeg",
                "-disposition:v", "attached_pic", "-metadata", "title=Chanson d'été", "-metadata", "artist=FileWheel", "-id3v2_version", "3", Sample("chanson.mp3"));
            Must("ffmpeg", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=550:duration=2", Sample("piste.flac"));
            // MP4 tronque (fichier endommage)
            var bytes = File.ReadAllBytes(Sample("clip vidéo.mp4"));
            File.WriteAllBytes(Sample("endommagé.mp4"), bytes[..(bytes.Length / 3)]);
        }
        else Missing.Add("ffmpeg");

        if (Which("convert") is not null)
        {
            Must("convert", "-size", "200x150", "gradient:blue-yellow", Sample("dégradé.bmp"));
            Must("convert", "-size", "90x60", "xc:green", Sample("vert.tiff"));
            Must("convert", "-size", "64x64", "xc:none", "-fill", "#ff000080", "-draw", "circle 32,32 32,8", Sample("cercle.webp"));
        }
        else Missing.Add("ImageMagick");

        // Textes et donnees
        File.WriteAllText(Sample("notes été.txt"), "Rapport de maintenance — été 2025\n\nCompresseur n°3 : pression 12,5 bar, température −18 °C.\nÀ vérifier : détendeur, vanne « 4 voies ».\n\n" + string.Join("\n", Enumerable.Range(1, 120).Select(i => $"Ligne {i} : relevé normal, aucun défaut constaté sur le circuit frigorifique principal.")), new UTF8Encoding(false));
        File.WriteAllText(Sample("lisez-moi.md"), "# Guide FileWheel\n\nUn **outil** pour _convertir_ des fichiers.\n\n- [x] Roue\n- [ ] Plus\n  - imbriqué\n\n| Format | Statut |\n|---|:---:|\n| PNG | ok |\n\n```cs\nvar x = 1 < 2;\n```\n\n<script>alert('x')</script>\n\n[lien](javascript:alert(1)) et [site](https://example.org)\n", new UTF8Encoding(false));
        File.WriteAllText(Sample("page.html"), "<!DOCTYPE html><html><head><title>Page</title><style>p{color:red}</style></head><body><h1>Titre</h1><p>Premier &amp; paragraphe</p><ul><li>un</li><li>deux</li></ul><script>var x=1;</script><table><tr><td>A</td><td>B</td></tr></table></body></html>", new UTF8Encoding(false));
        File.WriteAllText(Sample("données.json"), """
            {"stations": [
              {"id": 1, "nom": "Chambre froide A", "temp": -18.5, "actif": true, "capteurs": {"pression": 12.5, "humidité": 80}, "tags": ["froid", "négatif"]},
              {"id": 2, "nom": "Quai \"Nord\"", "temp": 4, "actif": false, "capteurs": {"pression": 11}, "tags": []}
            ]}
            """, new UTF8Encoding(false));
        File.WriteAllText(Sample("config.yaml"), """
            # Configuration d'essai
            application: FileWheel
            version: 0.1
            actif: true
            ports: [8080, 8443]
            serveur:
              hote: "127.0.0.1"
              options:
                - nom: compression
                  niveau: 6
                - nom: cache
                  taille: 128
            description: |
              Ligne une
              Ligne deux
            code_postal: "01000"
            vide: ~
            """, new UTF8Encoding(false));
        File.WriteAllText(Sample("inventaire.csv"), "Référence;Désignation;Quantité;Prix\n001;\"Vanne 4 voies; laiton\";12;45.5\n002;\"Détendeur \"\"pro\"\"\";3;120\n003;\"Note\nsur deux lignes\";0;0\n", new UTF8Encoding(true));
        File.WriteAllText(Sample("catalogue.xml"), "<?xml version=\"1.0\" encoding=\"utf-8\"?><catalogue version=\"2\"><article ref=\"A1\"><nom>Vanne</nom><prix>45.5</prix></article><article ref=\"A2\"><nom>Détendeur</nom></article><remarque>Fin</remarque></catalogue>", new UTF8Encoding(false));
        File.WriteAllText(Sample("piège.xml"), "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><r>&x;</r>");
        File.WriteAllText(Sample("cassé.json"), "{\"a\": 1,\n \"b\": [1, 2,\n \"c\" }");
        // 3D : cube ASCII
        var cube = new StringBuilder("solid cube\n");
        float[][] v = [[0, 0, 0], [10, 0, 0], [10, 10, 0], [0, 10, 0], [0, 0, 10], [10, 0, 10], [10, 10, 10], [0, 10, 10]];
        int[][] f = [[0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7], [0, 1, 5], [0, 5, 4], [1, 2, 6], [1, 6, 5], [2, 3, 7], [2, 7, 6], [3, 0, 4], [3, 4, 7]];
        foreach (var tri in f)
        {
            cube.Append("facet normal 0 0 0\n outer loop\n");
            foreach (var i in tri) cube.Append($"  vertex {v[i][0]} {v[i][1]} {v[i][2]}\n");
            cube.Append(" endloop\nendfacet\n");
        }
        cube.Append("endsolid cube\n");
        File.WriteAllText(Sample("cube.stl"), cube.ToString());
        // Police
        var font = Directory.EnumerateFiles("/usr/share/fonts", "DejaVuSans.ttf", SearchOption.AllDirectories).FirstOrDefault();
        if (font is not null) File.Copy(font, Sample("police.ttf"), true); else Missing.Add("police TTF");
        File.WriteAllBytes(Sample("vide.png"), []);
        File.WriteAllBytes(Sample("faux.png"), Encoding.ASCII.GetBytes("ceci n'est pas une image mais du texte"));
        // Image sans extension
        File.Copy(Sample("logo transparent.png"), Sample("image_sans_extension"), true);
        File.Copy(Sample("logo transparent.png"), Sample("png déguisé.jpg"), true);

        // Documents bureautiques via LibreOffice (moteur reel)
        if (Which("soffice") is not null)
        {
            var lo = Path.Combine(Root, "lo-gen");
            Directory.CreateDirectory(lo);
            var profile = new Uri(Path.Combine(lo, "profil") + "/").AbsoluteUri;
            File.WriteAllText(Path.Combine(lo, "rapport.html"), "<html><body><h1>Rapport annuel</h1><p>Texte du rapport avec accents : éèàçù.</p><table border=1><tr><td>A</td><td>B</td></tr></table></body></html>");
            Must("soffice", "--headless", "-env:UserInstallation=" + profile, "--infilter=HTML (StarWriter)", "--convert-to", "docx:MS Word 2007 XML", "--outdir", lo, Path.Combine(lo, "rapport.html"));
            File.Copy(Path.Combine(lo, "rapport.docx"), Sample("rapport annuel.docx"), true);
            File.WriteAllText(Path.Combine(lo, "diapos.fodp"), FodpSample());
            Must("soffice", "--headless", "-env:UserInstallation=" + profile, "--convert-to", "pptx", "--outdir", lo, Path.Combine(lo, "diapos.fodp"));
            File.Copy(Path.Combine(lo, "diapos.pptx"), Sample("présentation.pptx"), true);
            Must("soffice", "--headless", "-env:UserInstallation=" + profile, "--convert-to", "pdf", "--outdir", lo, Path.Combine(lo, "rapport.docx"));
            File.Copy(Path.Combine(lo, "rapport.pdf"), Sample("rapport.pdf"), true);
        }
        else Missing.Add("LibreOffice");
    }

    private static string FodpSample() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
          xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"
          xmlns:presentation="urn:oasis:names:tc:opendocument:xmlns:presentation:1.0" office:version="1.2" office:mimetype="application/vnd.oasis.opendocument.presentation">
          <office:body><office:presentation>
            <draw:page draw:name="Diapo1"><draw:frame svg:x="2cm" svg:y="2cm" svg:width="20cm" svg:height="3cm"><draw:text-box><text:p>Première diapositive</text:p></draw:text-box></draw:frame></draw:page>
            <draw:page draw:name="Diapo2"><draw:frame svg:x="2cm" svg:y="2cm" svg:width="20cm" svg:height="3cm"><draw:text-box><text:p>Deuxième diapositive</text:p></draw:text-box></draw:frame></draw:page>
            <draw:page draw:name="Diapo3"><draw:frame svg:x="2cm" svg:y="2cm" svg:width="20cm" svg:height="3cm"><draw:text-box><text:p>Troisième</text:p></draw:text-box></draw:frame></draw:page>
          </office:presentation></office:body>
        </office:document>
        """;
}
