using FileWheel.Core.Actions;
using FileWheel.Core.Formats;
using FileWheel.Core.Settings;

namespace FileWheel.Core.Wheel;

/// <summary>Un element glisse sur la roue.</summary>
public sealed record SelectedEntry(string Path, bool IsDirectory, FormatInfo? Format)
{
    public FormatCategory Category => IsDirectory ? FormatCategory.Unknown : Format?.Category ?? FormatCategory.Unknown;

    public static SelectedEntry FromPath(string path, bool detectContent = false)
    {
        if (Directory.Exists(path)) return new SelectedEntry(path, true, null);
        FormatInfo? f = null;
        if (detectContent)
        {
            try { f = FormatDetector.Detect(path).Format; } catch { }
        }
        f ??= FormatCatalog.FromFileName(path);
        return new SelectedEntry(path, false, f);
    }
}

/// <summary>Cible de conversion proposee directement dans la roue.</summary>
/// <param name="TargetFormatId">Cle de la capacite de conversion.</param>
/// <param name="Caption">Libelle court affiche en gros dans le secteur (extension : « JPG », « DOCX »).</param>
/// <param name="Detail">Precision affichee au centre (« Audio seulement », « Pages en images »...).</param>
public sealed record QuickTarget(string TargetFormatId, string Label, string Glyph, bool Available, string? Reason = null,
    string? Caption = null, string? Detail = null);

/// <summary>Fournit les conversions rapides (implemente par le moteur de conversion).</summary>
public interface IConversionMenuSource
{
    /// <summary>Quelques cibles pertinentes pour la selection, les plus utiles d'abord.</summary>
    IReadOnlyList<QuickTarget> QuickTargets(IReadOnlyList<SelectedEntry> selection);
}

/// <summary>
/// Construit la roue adaptee a la selection : 8 secteurs principaux au plus,
/// les actions secondaires dans des sous-menus, les actions incompatibles desactivees
/// avec une raison lisible.
/// </summary>
public static class WheelMenuBuilder
{
    /// <summary>Formats que les outils image savent ouvrir (codecs Windows) ; les autres passent d'abord par une conversion.</summary>
    public static readonly IReadOnlySet<string> ImageToolFormats =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "png", "jpg", "bmp", "gif", "tiff", "ico", "webp", "heic", "avif", "jxr" };

    public static WheelItem Build(IReadOnlyList<SelectedEntry> sel, AppSettings settings,
        IConversionMenuSource? conversions, IReadOnlyList<CustomAction>? customActions) =>
        settings.Wheel.Layout == WheelMenuLayout.Classic
            ? BuildClassic(sel, settings, conversions, customActions)
            : BuildExpress(sel, settings, conversions, customActions);

    /// <summary>
    /// Disposition « express » (facon glisser-deposer-convertir) :
    /// - images : les outils (Recadrer, Fond, Compresser, Annoter, Infos) + Convertir + Fichier ;
    /// - autres fichiers convertibles : les formats cibles en gros (DOCX, JPG, MP3...) + Autres formats + Fichier ;
    /// - dossiers, programmes, archives ou fichiers sans conversion : disposition classique.
    /// « Fichier » regroupe les actions de la disposition classique (copier, deplacer, renommer, corbeille...).
    /// </summary>
    public static WheelItem BuildExpress(IReadOnlyList<SelectedEntry> sel, AppSettings settings,
        IConversionMenuSource? conversions, IReadOnlyList<CustomAction>? customActions)
    {
        if (sel.Count == 0 || sel.Any(s => s.IsDirectory || s.Category is FormatCategory.Executable or FormatCategory.Archive))
            return BuildClassic(sel, settings, conversions, customActions);

        bool single = sel.Count == 1;
        bool allImages = sel.All(s => s.Category == FormatCategory.Image);
        var targets = conversions?.QuickTargets(sel) ?? [];
        if (!allImages && targets.Count == 0)
            return BuildClassic(sel, settings, conversions, customActions);

        var root = new WheelItem { Id = "root", Label = "FileWheel" };
        if (allImages)
        {
            bool readable = sel.All(s => s.Format is { } f && ImageToolFormats.Contains(f.Id));
            string? why = readable ? null : "Convertir d'abord en PNG";
            root.Children.Add(Tool("tool.crop", "Recadrer", "RECADRER", Glyphs.Crop, ActionIds.ImageCrop, "Formats 1:1, 16:9, rotation", single, readable, why));
            root.Children.Add(Tool("tool.bg", "Ajouter un fond", "FOND", Glyphs.Background, ActionIds.ImageBackground, "Dégradé, marge, ombre", single, readable, why));
            root.Children.Add(Tool("tool.compress", "Compresser", "COMPRESSER", Glyphs.Compress, ActionIds.ImageCompress, "Fichier plus léger", true, readable, why));
            root.Children.Add(Tool("tool.annotate", "Annoter / masquer", "ANNOTER", Glyphs.Annotate, ActionIds.ImageAnnotate, "Flèches, texte, zones cachées", single, readable, why));
            root.Children.Add(Tool("tool.meta", "Métadonnées", "INFOS", Glyphs.Metadata, ActionIds.ImageMetadata, "Voir et effacer (GPS…)", single, readable, why));
            var convert = BuildConvert(sel, conversions, false, false);
            root.Children.Add(new WheelItem
            {
                Id = convert.Id, Label = convert.Label, Glyph = convert.Glyph, Caption = "CONVERTIR", Tooltip = convert.Tooltip,
                Enabled = convert.Enabled, DisabledReason = convert.DisabledReason,
            }.WithChildren(convert.Children.Select(TextFaceIfTarget)));
        }
        else
        {
            foreach (var t in targets.Take(5))
                root.Children.Add(new WheelItem
                {
                    Id = "convert." + t.TargetFormatId, Label = t.Label, Caption = t.Caption ?? t.Label.ToUpperInvariant(), TextFace = true,
                    Glyph = t.Glyph, Command = ActionIds.ConvertQuickTarget, Argument = t.TargetFormatId,
                    Tooltip = t.Detail ?? "L'original est conservé", Enabled = t.Available, DisabledReason = t.Reason,
                });
            root.Children.Add(new WheelItem
            {
                Id = "convert.dialog", Label = "Autres formats…", Caption = "AUTRES", Glyph = Glyphs.Convert, Command = ActionIds.ConvertDialog,
                Tooltip = "Tous les formats et réglages",
            });
        }

        // Actions perso placees a la racine (sans depasser 8 secteurs)
        if (customActions is not null)
            foreach (var a in customActions.Where(a => a.Placement == CustomActionPlacement.Root).Take(2))
                if (root.Children.Count < 7) root.Children.Add(CustomItem(a, sel));

        var classic = BuildClassic(sel, settings, conversions, null);
        var file = new WheelItem { Id = "file", Label = "Fichier", Caption = "FICHIER", Glyph = Glyphs.Folder, Tooltip = "Copier, déplacer, renommer…" };
        foreach (var c in classic.Children.Where(c => c.Id != "convert")) file.Children.Add(c);
        root.Children.Add(file);
        return root;
    }

    private static readonly Dictionary<string, string> Captions = new()
    {
        ["copy"] = "COPIER", ["moveto"] = "DÉPLACER", ["openwith"] = "OUVRIR AVEC", ["rename"] = "RENOMMER", ["recycle"] = "CORBEILLE",
        ["more"] = "PLUS", ["convert"] = "CONVERTIR", ["archive"] = "ARCHIVE", ["open"] = "OUVRIR", ["parent"] = "DOSSIER",
        ["terminal"] = "TERMINAL", ["properties"] = "PROPRIÉTÉS", ["copypath"] = "CHEMIN", ["copyname"] = "NOM", ["shortcut"] = "RACCOURCI",
        ["share"] = "PARTAGER", ["sendto"] = "ENVOYER", ["favorite"] = "FAVORI", ["custom"] = "MES ACTIONS", ["delete"] = "SUPPRIMER",
        ["convert.dialog"] = "AUTRES", ["convert.adv"] = "AVANCÉ", ["convert.resize"] = "TAILLE", ["moveto.pick"] = "CHOISIR",
        ["custom.new"] = "NOUVELLE", ["archive.extracthere"] = "ICI", ["archive.extractto"] = "VERS…", ["archive.test"] = "VÉRIFIER",
        ["archive.zip"] = "ZIP", ["archive.7z"] = "7Z", ["archive.tgz"] = "TAR.GZ", ["archive.custom"] = "OPTIONS",
        ["archive.to7z"] = "→ 7Z", ["archive.tozip"] = "→ ZIP",
    };

    /// <summary>Libelle court d'un secteur (majuscules, 12 caracteres au plus).</summary>
    public static string CaptionOf(WheelItem i)
    {
        if (i.Caption is { Length: > 0 } c) return c;
        if (Captions.TryGetValue(i.Id, out var known)) return known;
        var label = System.Text.RegularExpressions.Regex.Replace(i.Label, @"\s*\(.*?\)|…", "").Trim().ToUpperInvariant();
        if (label.Length > 12 && label.Contains(' ')) label = label[..label.IndexOf(' ')];
        return label.Length > 12 ? label[..11] + "…" : label;
    }

    private static WheelItem Tool(string id, string label, string caption, string glyph, string command, string tooltip,
        bool allowed, bool readable, string? why) => new()
    {
        Id = id, Label = label, Caption = caption, Glyph = glyph, Command = command, Tooltip = tooltip,
        Enabled = allowed && readable,
        DisabledReason = !readable ? why : allowed ? null : "Un seul fichier à la fois",
    };

    private static WheelItem TextFaceIfTarget(WheelItem i) => i.Command != ActionIds.ConvertQuickTarget ? i : new WheelItem
    {
        Id = i.Id, Label = i.Label, Caption = i.Caption ?? i.Label.ToUpperInvariant(), TextFace = true, Glyph = i.Glyph, Command = i.Command,
        Argument = i.Argument, Tooltip = i.Tooltip, Enabled = i.Enabled, DisabledReason = i.DisabledReason,
    };

    public static WheelItem BuildClassic(IReadOnlyList<SelectedEntry> sel, AppSettings settings,
        IConversionMenuSource? conversions, IReadOnlyList<CustomAction>? customActions)
    {
        var root = new WheelItem { Id = "root", Label = "FileWheel" };
        if (sel.Count == 0)
        {
            root.Children.Add(WheelItem.Disabled("none", "Aucun fichier", Glyphs.Cancel, "Aucun fichier reconnu"));
            return root;
        }

        bool single = sel.Count == 1;
        bool anyDir = sel.Any(s => s.IsDirectory);
        bool allFiles = !anyDir;
        bool allArchives = allFiles && sel.All(s => s.Category == FormatCategory.Archive);
        bool anyExecutable = sel.Any(s => s.Category == FormatCategory.Executable);
        string countLabel = single ? "" : $" ({sel.Count})";

        // 1. Convertir
        root.Children.Add(BuildConvert(sel, conversions, anyDir, anyExecutable));

        // 2. Copier (presse-papiers)
        root.Children.Add(new WheelItem
        {
            Id = "copy", Label = "Copier" + countLabel, Glyph = Glyphs.Copy, Command = ActionIds.Copy,
            Tooltip = "Puis Ctrl+V où vous voulez",
        });

        // 3. Deplacer vers
        root.Children.Add(BuildMoveTo(settings));

        // 4. Ouvrir avec
        var openWith = new WheelItem
        {
            Id = "openwith", Label = "Ouvrir avec…", Glyph = Glyphs.OpenWith, Command = ActionIds.OpenWith,
            Tooltip = "Choisir l'application",
        };
        if (!single || anyDir)
        {
            openWith.Enabled = false;
            openWith.DisabledReason = anyDir ? "Pas pour les dossiers" : "Un seul fichier à la fois";
        }
        root.Children.Add(openWith);

        // 5. Archive
        root.Children.Add(BuildArchive(sel, allArchives, single));

        // 6. Renommer
        root.Children.Add(new WheelItem
        {
            Id = "rename", Label = single ? "Renommer" : "Renommer en lot", Glyph = Glyphs.Rename, Command = ActionIds.Rename,
            Tooltip = single ? "Changer le nom" : "Selon un modèle",
        });

        // 7. Corbeille
        root.Children.Add(new WheelItem
        {
            Id = "recycle", Label = "Corbeille" + countLabel, Glyph = Glyphs.Recycle, Command = ActionIds.Recycle,
            Tooltip = "Récupérable",
        });

        // 8. Plus
        root.Children.Add(BuildMore(sel, settings, customActions, single, anyDir, anyExecutable));

        // Actions perso placees a la racine (2 au plus pour ne pas surcharger)
        if (customActions is not null)
        {
            foreach (var a in customActions.Where(a => a.Placement == CustomActionPlacement.Root).Take(2))
                root.Children.Insert(root.Children.Count - 1, CustomItem(a, sel));
        }
        return root;
    }

    private static WheelItem BuildConvert(IReadOnlyList<SelectedEntry> sel, IConversionMenuSource? conversions, bool anyDir, bool anyExecutable)
    {
        var item = new WheelItem
        {
            Id = "convert", Label = "Convertir", Glyph = Glyphs.Convert,
            Tooltip = "L'original est conservé",
        };
        if (anyDir)
        {
            item.Enabled = false;
            item.DisabledReason = "Pas pour les dossiers";
            item.Children.Add(Leaf("convert.adv", "Convertisseur avancé…", Glyphs.Settings, ActionIds.ConvertAdvanced));
            return item;
        }
        if (anyExecutable)
        {
            item.Enabled = false;
            item.DisabledReason = "Jamais pour les programmes";
            item.Children.Add(Leaf("convert.adv", "Convertisseur avancé…", Glyphs.Settings, ActionIds.ConvertAdvanced));
            return item;
        }

        var targets = conversions?.QuickTargets(sel) ?? [];
        foreach (var t in targets.Take(6))
        {
            var leaf = new WheelItem
            {
                Id = "convert." + t.TargetFormatId, Label = t.Label, Glyph = t.Glyph, Command = ActionIds.ConvertQuickTarget,
                Caption = t.Caption, Argument = t.TargetFormatId, Tooltip = t.Detail ?? "Réglages par défaut",
                Enabled = t.Available, DisabledReason = t.Reason,
            };
            item.Children.Add(leaf);
        }
        if (sel.All(s => s.Category == FormatCategory.Image))
            item.Children.Add(Leaf("convert.resize", "Redimensionner…", Glyphs.Resize, ActionIds.ImageResize, "Changer la taille"));
        item.Children.Add(Leaf("convert.dialog", "Conversion rapide…", Glyphs.Lightning, ActionIds.ConvertDialog, "Format et réglages"));
        item.Children.Add(Leaf("convert.adv", "Convertisseur avancé…", Glyphs.Settings, ActionIds.ConvertAdvanced, "Tous les formats, par lot"));
        if (targets.Count == 0)
        {
            item.Tooltip = "Voir le convertisseur";
        }
        return item;
    }

    private static WheelItem BuildMoveTo(AppSettings settings)
    {
        var item = new WheelItem
        {
            Id = "moveto", Label = "Déplacer vers…", Glyph = Glyphs.MoveTo, Command = ActionIds.MoveTo,
            Tooltip = "Vers un dossier",
        };
        var folders = settings.SendToFolders.Where(Directory.Exists).Take(6).ToList();
        if (settings.LastMoveFolder is { } last && Directory.Exists(last) && !folders.Contains(last, StringComparer.OrdinalIgnoreCase))
            folders.Insert(0, last);
        if (folders.Count == 0) return item; // feuille : ouvre directement le selecteur

        foreach (var f in folders.Take(6))
            item.Children.Add(new WheelItem
            {
                Id = "moveto." + f, Label = FolderLabel(f), Glyph = Glyphs.Folder, Command = ActionIds.MoveTo, Argument = f,
                Tooltip = "Déplacer dans ce dossier",
            });
        item.Children.Add(Leaf("moveto.pick", "Choisir un dossier…", Glyphs.Add, ActionIds.MoveTo));
        return item;
    }

    private static WheelItem BuildArchive(IReadOnlyList<SelectedEntry> sel, bool allArchives, bool single)
    {
        var item = new WheelItem { Id = "archive", Label = allArchives ? "Extraire" : "Compresser", Glyph = allArchives ? Glyphs.Extract : Glyphs.Archive };
        if (allArchives)
        {
            item.Children.Add(Leaf("archive.extracthere", "Extraire ici", Glyphs.Extract, ActionIds.ExtractHere, "Dans un nouveau dossier"));
            item.Children.Add(Leaf("archive.extractto", "Extraire vers…", Glyphs.Folder, ActionIds.ExtractTo));
            item.Children.Add(Leaf("archive.test", "Vérifier l'intégrité", Glyphs.Check, ActionIds.TestArchive));
            item.Children.Add(new WheelItem { Id = "archive.to7z", Label = "Convertir en 7Z", Glyph = Glyphs.Archive, Command = ActionIds.CompressAs, Argument = "7z-recompress" });
            item.Children.Add(new WheelItem { Id = "archive.tozip", Label = "Convertir en ZIP", Glyph = Glyphs.Archive, Command = ActionIds.CompressAs, Argument = "zip-recompress" });
        }
        else
        {
            item.Children.Add(Leaf("archive.zip", "Compresser en ZIP", Glyphs.Archive, ActionIds.Zip, single ? "À côté du fichier" : "Toute la sélection"));
            item.Children.Add(new WheelItem { Id = "archive.7z", Label = "Compresser en 7Z", Glyph = Glyphs.Archive, Command = ActionIds.CompressAs, Argument = "7z" });
            item.Children.Add(new WheelItem { Id = "archive.tgz", Label = "Compresser en TAR.GZ", Glyph = Glyphs.Archive, Command = ActionIds.CompressAs, Argument = "tgz" });
            item.Children.Add(new WheelItem { Id = "archive.custom", Label = "Compresser…", Glyph = Glyphs.Settings, Command = ActionIds.CompressAs, Argument = "", Tooltip = "Format, niveau, mot de passe" });
        }
        return item;
    }

    private static WheelItem BuildMore(IReadOnlyList<SelectedEntry> sel, AppSettings settings, IReadOnlyList<CustomAction>? customActions,
        bool single, bool anyDir, bool anyExecutable)
    {
        var more = new WheelItem { Id = "more", Label = "Plus", Glyph = Glyphs.More, Tooltip = "Autres actions" };
        more.Children.Add(Leaf("open", single ? "Ouvrir" : $"Ouvrir ({sel.Count})", Glyphs.Open, ActionIds.Open,
            anyExecutable ? "Confirmation demandée" : "Application par défaut"));
        more.Children.Add(Leaf("parent", "Dossier parent", Glyphs.Folder, ActionIds.OpenParent, "Afficher dans l'Explorateur"));
        more.Children.Add(Leaf("terminal", "Terminal ici", Glyphs.Terminal, ActionIds.Terminal, "Dans le dossier parent"));
        more.Children.Add(Leaf("properties", "Propriétés", Glyphs.Info, ActionIds.Properties));
        more.Children.Add(Leaf("copypath", single ? "Copier le chemin" : "Copier les chemins", Glyphs.Path, ActionIds.CopyPath));
        more.Children.Add(Leaf("copyname", single ? "Copier le nom" : "Copier les noms", Glyphs.Tag, ActionIds.CopyName));
        more.Children.Add(Leaf("shortcut", "Créer un raccourci", Glyphs.Shortcut, ActionIds.Shortcut));
        var share = Leaf("share", "Partager", Glyphs.Share, ActionIds.Share, "Partage Windows");
        if (anyDir) { share.Enabled = false; share.DisabledReason = "Pas pour les dossiers"; }
        more.Children.Add(share);

        var sendTo = new WheelItem { Id = "sendto", Label = "Envoyer vers", Glyph = Glyphs.Send, Tooltip = "Copie vers vos dossiers" };
        foreach (var f in settings.SendToFolders.Where(Directory.Exists).Take(8))
            sendTo.Children.Add(new WheelItem { Id = "sendto." + f, Label = FolderLabel(f), Glyph = Glyphs.Folder, Command = ActionIds.SendTo, Argument = f, Tooltip = "Copier dans ce dossier" });
        if (sendTo.Children.Count == 0)
        {
            sendTo.Enabled = false;
            sendTo.DisabledReason = "À définir dans Paramètres";
            sendTo.Children.Add(Leaf("sendto.settings", "Paramètres", Glyphs.Settings, ActionIds.Settings));
        }
        more.Children.Add(sendTo);
        more.Children.Add(Leaf("favorite", "Ajouter aux favoris", Glyphs.Favorite, ActionIds.AddFavorite, "Accès depuis l'icône"));

        var mine = new WheelItem { Id = "custom", Label = "Mes actions", Glyph = Glyphs.Lightning };
        if (customActions is not null)
        {
            foreach (var a in customActions.Where(a => a.Placement == CustomActionPlacement.Submenu).Take(10))
                mine.Children.Add(CustomItem(a, sel));
        }
        mine.Children.Add(Leaf("custom.new", "Nouvelle action…", Glyphs.Add, ActionIds.NewCustomAction));
        more.Children.Add(mine);

        more.Children.Add(new WheelItem
        {
            Id = "delete", Label = "Supprimer définitivement", Glyph = Glyphs.DeleteForever, Command = ActionIds.DeletePermanently,
            Destructive = true, Tooltip = "Irréversible, avec confirmation",
        });
        return more;
    }

    private static WheelItem CustomItem(CustomAction a, IReadOnlyList<SelectedEntry> sel)
    {
        var item = new WheelItem { Id = "custom." + a.Id, Label = a.Name, Glyph = a.Glyph, Command = ActionIds.Custom, Argument = a.Id };
        var refused = sel.FirstOrDefault(s => !a.Accepts(s.Path, s.IsDirectory, sel.Count));
        if (refused is not null)
        {
            item.Enabled = false;
            item.DisabledReason = "Type de fichier non accepté";
        }
        return item;
    }

    private static WheelItem Leaf(string id, string label, string glyph, string command, string tooltip = "") =>
        new() { Id = id, Label = label, Glyph = glyph, Command = command, Tooltip = tooltip };

    private static string FolderLabel(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
