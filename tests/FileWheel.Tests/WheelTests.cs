using FileWheel.Core.Actions;
using FileWheel.Core.Formats;
using FileWheel.Core.Input;
using FileWheel.Core.Settings;
using FileWheel.Core.Wheel;
using static FileWheel.Tests.T;

namespace FileWheel.Tests;

/// <summary>Logique de declenchement, geometrie et navigation de la roue (independante de Windows).</summary>
public static class WheelTests
{
    private static DragGestureDetector NewDetector(GestureOptions? o = null) =>
        new() { Options = o ?? new GestureOptions { DelayMs = 80, MinDistancePx = 10, ItemCheck = ItemCheckPolicy.None } };

    /// <summary>Simule : appui, deplacement par pas, touches, et renvoie la premiere sortie non vide.</summary>
    private static GestureOutput Drag(DragGestureDetector d, DragSource src, int[] keysBefore, int distance, long durationMs, int[]? keysDuring = null)
    {
        long t = 1000;
        GestureOutput last = GestureOutput.Nothing;
        foreach (var k in keysBefore) { var o = d.OnKeyDown(k, t); if (o.Action != GestureAction.None) last = o; }
        d.OnMouseDown(500, 400, src, t);
        int steps = 20;
        for (int i = 1; i <= steps; i++)
        {
            t += durationMs / steps;
            if (keysDuring is not null && i == steps / 2) foreach (var k in keysDuring) d.OnKeyDown(k, t);
            var o = d.OnMouseMove(500 + distance * i / steps, 400 + distance * i / (2 * steps), t);
            if (o.Action != GestureAction.None) last = o;
        }
        var tick = d.OnTick(t + 200);
        return tick.Action != GestureAction.None ? tick : last;
    }

    public static async Task RunAll()
    {
        Suite("Roue — déclenchement (souris + clavier)");

        await Run("Alt + glisser dans l'Explorateur affiche la roue", () =>
        {
            var d = NewDetector();
            var o = Drag(d, DragSource.Explorer, [VK.LMenu], 60, 300);
            Equal(GestureAction.ShowWheel, o.Action, "action");
            Check(o.X > 500 && o.Y > 400, "la roue s'ouvre près du curseur");
        });
        await Run("Fonctionne aussi sur le Bureau", () =>
            Equal(GestureAction.ShowWheel, Drag(NewDetector(), DragSource.Desktop, [VK.LMenu], 60, 300).Action, "action"));
        await Run("Aucun déclenchement sans la touche", () =>
            Equal(GestureAction.None, Drag(NewDetector(), DragSource.Explorer, [], 200, 600).Action, "action"));
        await Run("Aucun déclenchement pour un simple clic (pas de déplacement)", () =>
            Equal(GestureAction.None, Drag(NewDetector(), DragSource.Explorer, [VK.LMenu], 3, 600).Action, "action"));
        await Run("Aucun déclenchement pour une pression isolée sur Alt", () =>
        {
            var d = NewDetector();
            d.OnKeyDown(VK.LMenu, 0);
            d.OnTick(500);
            var o = d.OnKeyUp(VK.LMenu, 600, out var mask);
            Equal(GestureAction.None, o.Action, "action");
            Check(!mask, "le relâchement d'Alt n'est pas masqué (barre de menus normale)");
            Check(!d.IsWheelShown, "roue fermée");
        });
        await Run("Aucun déclenchement hors Explorateur/Bureau", () =>
            Equal(GestureAction.None, Drag(NewDetector(), DragSource.Other, [VK.LMenu], 100, 300).Action, "action"));
        await Run("Le délai d'apparition est respecté", () =>
        {
            var d = NewDetector(new GestureOptions { DelayMs = 400, MinDistancePx = 10, ItemCheck = ItemCheckPolicy.None });
            d.OnKeyDown(VK.LMenu, 0);
            d.OnMouseDown(0, 0, DragSource.Explorer, 0);
            Equal(GestureAction.None, d.OnMouseMove(40, 0, 50).Action, "avant le délai");
            Equal(GestureAction.None, d.OnTick(300).Action, "toujours avant");
            Equal(GestureAction.ShowWheel, d.OnTick(460).Action, "après le délai");
        });
        await Run("Combinaison exacte : Ctrl+Alt ne déclenche pas une combinaison « Alt »", () =>
            Equal(GestureAction.None, Drag(NewDetector(), DragSource.Explorer, [VK.LMenu, VK.LControl], 80, 300).Action, "action"));
        await Run("AltGr (Ctrl gauche + Alt droite) n'est pas pris pour Alt", () =>
            Equal(GestureAction.None, Drag(NewDetector(), DragSource.Explorer, [VK.LControl, VK.RMenu], 80, 300).Action, "action"));
        await Run("Combinaison configurable : Ctrl+Maj", () =>
        {
            var d = NewDetector(new GestureOptions { Chord = new ActivationChord(true, false, true, false), DelayMs = 50, ItemCheck = ItemCheckPolicy.None });
            Equal(GestureAction.ShowWheel, Drag(d, DragSource.Explorer, [VK.LControl, VK.LShift], 80, 300).Action, "Ctrl+Maj");
            var d2 = NewDetector(new GestureOptions { Chord = new ActivationChord(true, false, true, false), DelayMs = 50, ItemCheck = ItemCheckPolicy.None });
            Equal(GestureAction.None, Drag(d2, DragSource.Explorer, [VK.LMenu], 80, 300).Action, "Alt seul ne déclenche plus");
        });
        await Run("Coexistence avec les raccourcis Windows : Alt+Tab avant l'affichage annule l'armement", () =>
            Equal(GestureAction.None, Drag(NewDetector(new GestureOptions { DelayMs = 400, ItemCheck = ItemCheckPolicy.None }), DragSource.Explorer, [VK.LMenu], 80, 300, keysDuring: [VK.Tab]).Action, "action"));
        await Run("Codes synthétiques (0xE8, 0xFF, 0x07, VK_PACKET) pendant le geste : ignorés, la roue s'affiche", () =>
            Equal(GestureAction.ShowWheel, Drag(NewDetector(new GestureOptions { DelayMs = 400, ItemCheck = ItemCheckPolicy.None }), DragSource.Explorer, [VK.LMenu], 80, 600, keysDuring: [0xE8, 0xFF, 0x07, 0xE7]).Action, "action"));
        await Run("Coexistence avec les raccourcis Windows : Alt+Tab roue affichée => la roue s'efface", () =>
        {
            var d = NewDetector();
            Equal(GestureAction.ShowWheel, Drag(d, DragSource.Explorer, [VK.LMenu], 60, 300).Action, "affichée");
            Equal(GestureAction.HideWheel, d.OnKeyDown(VK.Tab, 9000).Action, "effacée au profit d'Alt+Tab");
            Equal(GestureAction.None, d.OnTick(9500).Action, "ne revient pas");
        });
        await Run("Échap ferme la roue affichée", () =>
        {
            var d = NewDetector();
            Equal(GestureAction.ShowWheel, Drag(d, DragSource.Explorer, [VK.LMenu], 60, 300).Action, "affichée");
            var o = d.OnKeyDown(VK.Escape, 5000);
            Equal(GestureAction.HideWheel, o.Action, "fermée");
            Equal(HideReason.Escape, o.Reason, "raison");
            Equal(GestureAction.None, d.OnMouseMove(800, 800, 6000).Action, "ne se rouvre pas pendant ce glisser");
        });
        await Run("Relâchement prématuré de la touche : pas de roue", () =>
        {
            var d = NewDetector(new GestureOptions { DelayMs = 300, ItemCheck = ItemCheckPolicy.None });
            d.OnKeyDown(VK.LMenu, 0);
            d.OnMouseDown(0, 0, DragSource.Explorer, 0);
            d.OnMouseMove(50, 0, 50);
            d.OnKeyUp(VK.LMenu, 100, out _);
            Equal(GestureAction.None, d.OnTick(600).Action, "pas de roue");
        });
        await Run("Relâchement de la touche après affichage : la roue reste (par défaut) et Alt est masqué", () =>
        {
            var d = NewDetector();
            Drag(d, DragSource.Explorer, [VK.LMenu], 60, 300);
            var o = d.OnKeyUp(VK.LMenu, 5000, out var mask);
            Equal(GestureAction.None, o.Action, "roue toujours visible");
            Check(mask, "relâchement d'Alt masqué (pas de barre de menus de l'Explorateur)");
            Check(d.IsWheelShown, "roue visible");
        });
        await Run("Option « fermer au relâchement de la touche »", () =>
        {
            var d = NewDetector(new GestureOptions { CloseOnChordRelease = true, DelayMs = 50, ItemCheck = ItemCheckPolicy.None });
            Drag(d, DragSource.Explorer, [VK.LMenu], 60, 300);
            var o = d.OnKeyUp(VK.LMenu, 5000, out _);
            Equal(GestureAction.HideWheel, o.Action, "fermée");
            Equal(HideReason.ChordReleased, o.Reason, "raison");
        });
        await Run("Relâchement de la souris hors de la roue : fermeture (le dépôt natif a lieu)", () =>
        {
            var d = NewDetector();
            Drag(d, DragSource.Explorer, [VK.LMenu], 60, 300);
            var o = d.OnMouseUp(6000);
            Equal(GestureAction.HideWheel, o.Action, "fermée");
            Equal(HideReason.MouseReleased, o.Reason, "raison");
        });
        await Run("Vérification d'élément : zone vide (sélection rectangulaire) => pas de roue", () =>
        {
            var d = NewDetector(new GestureOptions { DelayMs = 50, ItemCheck = ItemCheckPolicy.Strict });
            d.OnKeyDown(VK.LMenu, 0);
            d.OnMouseDown(0, 0, DragSource.Explorer, 0);
            d.OnItemCheck(false, 10);
            d.OnMouseMove(100, 0, 50);
            Equal(GestureAction.None, d.OnTick(500).Action, "pas de roue");
        });
        await Run("Vérification d'élément : mode tolérant après délai sans réponse", () =>
        {
            var d = NewDetector(new GestureOptions { DelayMs = 50, ItemCheck = ItemCheckPolicy.Lenient, ItemCheckTimeoutMs = 300 });
            d.OnKeyDown(VK.LMenu, 0);
            d.OnMouseDown(0, 0, DragSource.Explorer, 0);
            d.OnMouseMove(100, 0, 50);
            Equal(GestureAction.None, d.OnTick(200).Action, "attend la vérification");
            Equal(GestureAction.ShowWheel, d.OnTick(420).Action, "affiche après le délai");
        });
        await Run("Désactivation temporaire", () =>
        {
            var d = NewDetector();
            d.Enabled = false;
            Equal(GestureAction.None, Drag(d, DragSource.Explorer, [VK.LMenu], 60, 300).Action, "désactivée");
        });
        await Run("Bureau désactivable séparément", () =>
        {
            var d = NewDetector(new GestureOptions { OnDesktop = false, DelayMs = 50, ItemCheck = ItemCheckPolicy.None });
            Equal(GestureAction.None, Drag(d, DragSource.Desktop, [VK.LMenu], 60, 300).Action, "Bureau");
            Equal(GestureAction.ShowWheel, Drag(NewDetector(new GestureOptions { OnDesktop = false, DelayMs = 50, ItemCheck = ItemCheckPolicy.None }), DragSource.Explorer, [VK.LMenu], 60, 300).Action, "Explorateur");
        });
        await Run("Combinaisons invalides refusées et avertissements", () =>
        {
            Check(!new ActivationChord(false, false, false, false).IsValid, "combinaison vide refusée");
            Check(!new ActivationChord(false, false, false, false, VK.Escape).IsValid, "Échap refusée");
            Check(new ActivationChord(true, true, false, false).Warnings().Any(w => w.Contains("AltGr")), "avertissement AltGr");
            var s = new AppSettings();
            s.Trigger.Alt = false;
            s.Normalize();
            Check(s.Trigger.Chord == ActivationChord.Default, "paramètre invalide remplacé par Alt");
        });

        Suite("Roue — écrans multiples et facteurs d'échelle");
        var monitors = new List<MonitorInfo>
        {
            new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 96),
            new(new PixelRect(1920, -200, 1920 + 3840, -200 + 2160), new PixelRect(1920, -200, 1920 + 3840, -200 + 2100), 192),
            new(new PixelRect(-1280, 0, 0, 1024), new PixelRect(-1280, 0, 0, 1024), 120),
        };
        await Run("100 % : diamètre 320 px, centré sur le curseur", () =>
        {
            var r = ScreenGeometry.PlaceWheel(monitors, 800, 500, 320, 0, 0, out var m);
            Equal(96, m.Dpi, "écran");
            Equal(320, r.Width, "diamètre");
            Equal(800, r.Left + r.Width / 2, "centre X");
        });
        await Run("200 % (écran 4K) : diamètre physique doublé", () =>
        {
            var r = ScreenGeometry.PlaceWheel(monitors, 3000, 800, 320, 0, 0, out var m);
            Equal(192, m.Dpi, "DPI");
            Equal(640, r.Width, "diamètre");
        });
        await Run("125 % sur un écran à gauche (coordonnées négatives)", () =>
        {
            var r = ScreenGeometry.PlaceWheel(monitors, -600, 300, 320, 0, 0, out var m);
            Equal(120, m.Dpi, "DPI");
            Equal(400, r.Width, "diamètre");
            Check(r.Left < 0 && r.Right <= 0, "reste sur l'écran de gauche");
        });
        await Run("Bord d'écran : la roue est recadrée pour rester visible (barre des tâches exclue)", () =>
        {
            var r = ScreenGeometry.PlaceWheel(monitors, 1910, 1075, 320, 0, 0, out _);
            Check(r.Right <= 1920 && r.Bottom <= 1040, $"rectangle {r} dans la zone de travail");
        });
        await Run("Décalage configurable appliqué à l'échelle de l'écran", () =>
        {
            var r = ScreenGeometry.PlaceWheel(monitors, 3000, 800, 320, 50, 0, out _);
            Equal(3000 + 100, r.Left + r.Width / 2, "centre décalé de 50 DIP = 100 px à 200 %");
        });
        await Run("Curseur entre deux écrans : écran le plus proche", () =>
        {
            var m = ScreenGeometry.MonitorAt(monitors, 1000, 1100);
            Equal(96, m.Dpi, "écran le plus proche");
        });

        Suite("Roue — géométrie et navigation");
        await Run("Test de zone : centre, secteurs, extérieur", () =>
        {
            var l = new WheelLayout(160, 50, 8);
            Equal(WheelLayout.Center, l.HitTest(0, 10), "centre");
            Equal(0, l.HitTest(0, -100), "haut = secteur 0");
            Equal(2, l.HitTest(100, 0), "droite = secteur 2");
            Equal(4, l.HitTest(0, 100), "bas = secteur 4");
            Equal(6, l.HitTest(-100, 0), "gauche = secteur 6");
            Equal(WheelLayout.Outside, l.HitTest(200, 0), "extérieur");
            Equal(7, l.HitTest(-60, -80), "haut-gauche");
        });
        var sel = new List<SelectedEntry> { new("/tmp/a.png", false, FormatCatalog.Get("png")) };
        var menu = WheelMenuBuilder.Build(sel, ClassicSettings(), null, null);
        await Run("Survol prolongé d'une branche : ouverture du sous-menu pendant le glisser", () =>
        {
            var w = new WheelInteraction(menu, 300);
            int idx = menu.Children.FindIndex(c => c.Id == "more");
            w.OnPointer(idx, 0);
            Check(!w.InSubmenu, "pas encore");
            w.OnPointer(idx, 320);
            Check(w.InSubmenu && w.Current.Id == "more", "sous-menu Plus ouvert");
        });
        await Run("Dépôt sur une feuille : exécution", () =>
        {
            var w = new WheelInteraction(menu);
            int idx = menu.Children.FindIndex(c => c.Id == "copy");
            Equal(WheelOutcome.Execute, w.OnRelease(idx, 0), "résultat");
            Equal(ActionIds.Copy, w.Selected!.Command, "commande");
        });
        await Run("Dépôt sur une branche : la roue reste ouverte en mode clic", () =>
        {
            var w = new WheelInteraction(menu);
            int idx = menu.Children.FindIndex(c => c.Id == "convert");
            Equal(WheelOutcome.StayOpen, w.OnRelease(idx, 0), "résultat");
            Equal(WheelMode.Click, w.Mode, "mode");
            Check(w.InSubmenu, "sous-menu ouvert");
            Equal(WheelOutcome.StayOpen, w.OnRelease(WheelLayout.Center, 10), "centre = retour");
            Check(!w.InSubmenu, "retour au menu principal");
        });
        await Run("Dépôt au centre ou Échap : annulation", () =>
        {
            Equal(WheelOutcome.Cancel, new WheelInteraction(menu).OnRelease(WheelLayout.Center, 0), "centre");
            Equal(WheelOutcome.Cancel, new WheelInteraction(menu).OnEscape(), "Échap");
        });
        await Run("Action indisponible : jamais exécutée, raison affichée", () =>
        {
            var dirSel = new List<SelectedEntry> { new("/tmp/dossier", true, null) };
            var m = WheelMenuBuilder.Build(dirSel, ClassicSettings(), null, null);
            var w = new WheelInteraction(m);
            int idx = m.Children.FindIndex(c => c.Id == "openwith");
            Equal(WheelOutcome.StayOpen, w.OnRelease(idx, 0), "résultat");
            Check(w.Selected is null, "rien d'exécuté");
            Check(w.Message?.Contains("dossiers") == true, "raison : " + w.Message);
        });
        await Run("Action destructive marquée (confirmation obligatoire), hors du menu principal", () =>
        {
            Check(!menu.Children.Any(c => c.Destructive), "aucune action destructive au premier niveau");
            var del = menu.Children.First(c => c.Id == "more").Children.First(c => c.Id == "delete");
            Check(del.Destructive, "Supprimer définitivement est destructif");
        });

        Suite("Roue — adaptation au type de fichier");
        var conv = Env.Engine.Registry;
        WheelItem BuildFor(params string[] paths) =>
            WheelMenuBuilder.Build(paths.Select(p => SelectedEntry.FromPath(p, detectContent: true)).ToList(), ClassicSettings(), conv, null);
        WheelItem ExpressFor(params string[] paths) =>
            WheelMenuBuilder.Build(paths.Select(p => SelectedEntry.FromPath(p, detectContent: true)).ToList(), new AppSettings(), conv, null);
        await Run("MP4 : conversions vidéo et extraction audio mises en avant", () =>
        {
            var m = BuildFor(Env.Sample("clip vidéo.mp4"));
            var c = m.Children.First(x => x.Id == "convert");
            var labels = c.Children.Select(x => x.Label).ToList();
            Check(labels.Any(l => l.Contains("WebM")), "WebM proposé : " + string.Join(", ", labels));
            Check(labels.Any(l => l.StartsWith("Audio")), "extraction audio proposée");
            Check(!labels.Any(l => l.Contains("MP4")), "pas de MP4 vers MP4");
        });
        await Run("DOCX : PDF mis en avant", () =>
        {
            if (!File.Exists(Env.Sample("rapport annuel.docx"))) Skip("LibreOffice absent : pas de DOCX d'essai");
            var labels = BuildFor(Env.Sample("rapport annuel.docx")).Children.First(x => x.Id == "convert").Children.Select(x => x.Label).ToList();
            Equal("PDF", labels[0], "premier choix");
        });
        await Run("Image : formats d'image et redimensionnement", () =>
        {
            var labels = BuildFor(Env.Sample("photo exif.jpg")).Children.First(x => x.Id == "convert").Children.Select(x => x.Label).ToList();
            Check(labels.Contains("PNG") && labels.Contains("Redimensionner…"), string.Join(", ", labels));
        });
        await Run("Archive : extraction et recompression", () =>
        {
            var m = BuildFor(Env.Sample("photos été.zip"));
            var a = m.Children.First(x => x.Id == "archive");
            Equal("Extraire", a.Label, "libellé");
            Check(a.Children.Any(x => x.Command == ActionIds.ExtractHere) && a.Children.Any(x => x.Argument == "7z-recompress"), "extraire + convertir");
        });
        await Run("Programme déguisé : conversion désactivée", () =>
        {
            var m = BuildFor(Env.Sample("vacances.jpg"));
            var c = m.Children.First(x => x.Id == "convert");
            Check(!c.Enabled, "Convertir désactivé pour un exécutable déguisé");
        });
        await Run("Sélection multiple : libellés et actions adaptés", () =>
        {
            var m = BuildFor(Env.Sample("photo exif.jpg"), Env.Sample("logo transparent.png"));
            Check(m.Children.First(x => x.Id == "rename").Label == "Renommer en lot", "renommage en lot");
            Check(!m.Children.First(x => x.Id == "openwith").Enabled, "Ouvrir avec : un seul fichier");
            Check(m.Children.First(x => x.Id == "copy").Label.Contains("(2)"), "compteur");
        });
        await Run("Pas plus de 10 secteurs par anneau", () =>
        {
            var m = BuildFor(Env.Sample("clip vidéo.mp4"));
            void Walk(WheelItem i) { Check(i.Children.Count <= 12, $"{i.Label} : {i.Children.Count} secteurs"); foreach (var c in i.Children) Walk(c); }
            Walk(m);
            Check(m.Children.Count <= 10, "anneau principal");
        });

        Suite("Roue express (formats et outils directement dans la roue)");
        await Run("Par défaut : disposition express", () => Equal(WheelMenuLayout.Express, new AppSettings().Wheel.Layout, "disposition"));
        await Run("Fichier convertible (vidéo) : formats cibles en gros, puis Autres formats et Fichier", () =>
        {
            var m = ExpressFor(Env.Sample("clip vidéo.mp4"));
            var faces = m.Children.Where(c => c.TextFace).ToList();
            Check(faces.Count >= 2, "formats affichés : " + string.Join(", ", m.Children.Select(WheelMenuBuilder.CaptionOf)));
            Check(faces.All(f => f.Command == ActionIds.ConvertQuickTarget && f.Caption is { Length: > 0 and <= 5 }), "libellés courts (MP3, WEBM…)");
            Equal("file", m.Children[^1].Id, "Fichier en dernier");
            Check(m.Children.Any(c => c.Command == ActionIds.ConvertDialog), "Autres formats");
            Check(m.Children.Count <= 8, "8 secteurs au plus");
        });
        await Run("« Fichier » contient les actions classiques (copier, déplacer, renommer, corbeille, plus)", () =>
        {
            var f = ExpressFor(Env.Sample("clip vidéo.mp4")).Children.First(c => c.Id == "file");
            foreach (var id in new[] { "copy", "moveto", "rename", "recycle", "more" })
                Check(f.Children.Any(c => c.Id == id), "contient " + id);
            Check(!f.Children.Any(c => c.Id == "convert"), "pas de doublon Convertir");
        });
        await Run("Image : outils (recadrer, fond, compresser, annoter, infos) + Convertir + Fichier", () =>
        {
            var m = ExpressFor(Env.Sample("photo exif.jpg"));
            var cmds = m.Children.Select(c => c.Command).ToList();
            foreach (var c in new[] { ActionIds.ImageCrop, ActionIds.ImageBackground, ActionIds.ImageCompress, ActionIds.ImageAnnotate, ActionIds.ImageMetadata })
                Check(cmds.Contains(c), "outil " + c);
            var conv2 = m.Children.First(c => c.Id == "convert");
            Check(conv2.Children.Any(c => c.TextFace && c.Caption == "PNG"), "PNG en gros dans Convertir");
            Check(m.Children.All(c => c.Enabled), "tout disponible pour un JPEG");
        });
        await Run("Plusieurs images : seuls les outils par lot restent actifs", () =>
        {
            var m = ExpressFor(Env.Sample("photo exif.jpg"), Env.Sample("logo transparent.png"));
            Check(m.Children.First(c => c.Command == ActionIds.ImageCompress).Enabled, "compresser : par lot");
            Check(!m.Children.First(c => c.Command == ActionIds.ImageCrop).Enabled, "recadrer : un seul fichier");
        });
        await Run("Dossier, archive, programme : disposition classique", () =>
        {
            Check(ExpressFor(Env.Sample("photos été.zip")).Children.Any(c => c.Id == "archive"), "archive");
            Check(!ExpressFor(Env.Sample("vacances.jpg")).Children.Any(c => c.TextFace), "programme déguisé : aucun format proposé");
        });
        await Run("Aperçu des roues express (journal)", () =>
        {
            foreach (var f in new[] { "clip vidéo.mp4", "rapport.pdf", "rapport annuel.docx", "chanson.mp3", "photo exif.jpg", "config.yaml" })
            {
                if (!File.Exists(Env.Sample(f))) continue;
                var m = ExpressFor(Env.Sample(f));
                Console.WriteLine($"    {f} : " + string.Join(" | ", m.Children.Select(WheelMenuBuilder.CaptionOf)));
            }
        });
        await Run("Libellés courts des secteurs (12 caractères au plus)", () =>
        {
            void Walk(WheelItem i) { foreach (var c in i.Children) { var cap = WheelMenuBuilder.CaptionOf(c); Check(cap.Length is > 0 and <= 12, $"{c.Label} → {cap}"); Walk(c); } }
            Walk(ExpressFor(Env.Sample("photo exif.jpg")));
            Walk(ExpressFor(Env.Sample("clip vidéo.mp4")));
        });

        Suite("Glisser-déposer — sécurité");
        await Run("La roue ne répond jamais « Déplacer » à la source OLE", () =>
        {
            Equal(OleDropEffect.Copy, DropSafety.ChooseEffect(OleDropEffect.Copy | OleDropEffect.Move | OleDropEffect.Link), "tout autorisé");
            Equal(OleDropEffect.Link, DropSafety.ChooseEffect(OleDropEffect.Move | OleDropEffect.Link), "copie interdite");
            Equal(OleDropEffect.None, DropSafety.ChooseEffect(OleDropEffect.Move), "déplacement seul : refus");
        });
    }

    private static AppSettings ClassicSettings()
    {
        var s = new AppSettings();
        s.Wheel.Layout = WheelMenuLayout.Classic;
        return s;
    }
}
