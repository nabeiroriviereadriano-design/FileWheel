# FileWheel — résultats des tests automatiques

Exécution du 29/10/2026 22:06. Environnement : Ubuntu 24.04.5 LTS, .NET 10.0.13. Moteurs : FFmpeg présent, FFprobe présent, ImageMagick présent, 7-Zip présent, PDFium présent, LibreOffice présent, Calibre (ebook-convert) présent.

**204 réussis, 0 en échec, 10 non exécutés** sur 214.

## Roue — déclenchement (souris + clavier)

| Test | Résultat | Détail |
|---|---|---|
| Alt + glisser dans l'Explorateur affiche la roue | OK |  |
| Fonctionne aussi sur le Bureau | OK |  |
| Aucun déclenchement sans la touche | OK |  |
| Aucun déclenchement pour un simple clic (pas de déplacement) | OK |  |
| Aucun déclenchement pour une pression isolée sur Alt | OK |  |
| Aucun déclenchement hors Explorateur/Bureau | OK |  |
| Le délai d'apparition est respecté | OK |  |
| Combinaison exacte : Ctrl+Alt ne déclenche pas une combinaison « Alt » | OK |  |
| AltGr (Ctrl gauche + Alt droite) n'est pas pris pour Alt | OK |  |
| Combinaison configurable : Ctrl+Maj | OK |  |
| Coexistence avec les raccourcis Windows : Alt+Tab avant l'affichage annule l'armement | OK |  |
| Codes synthétiques (0xE8, 0xFF, 0x07, VK_PACKET) pendant le geste : ignorés, la roue s'affiche | OK |  |
| Coexistence avec les raccourcis Windows : Alt+Tab roue affichée => la roue s'efface | OK |  |
| Échap ferme la roue affichée | OK |  |
| Relâchement prématuré de la touche : pas de roue | OK |  |
| Relâchement de la touche après affichage : la roue reste (par défaut) et Alt est masqué | OK |  |
| Option « fermer au relâchement de la touche » | OK |  |
| Relâchement de la souris hors de la roue : fermeture (le dépôt natif a lieu) | OK |  |
| Vérification d'élément : zone vide (sélection rectangulaire) => pas de roue | OK |  |
| Vérification d'élément : mode tolérant après délai sans réponse | OK |  |
| Désactivation temporaire | OK |  |
| Bureau désactivable séparément | OK |  |
| Combinaisons invalides refusées et avertissements | OK |  |

## Roue — écrans multiples et facteurs d'échelle

| Test | Résultat | Détail |
|---|---|---|
| 100 % : diamètre 320 px, centré sur le curseur | OK |  |
| 200 % (écran 4K) : diamètre physique doublé | OK |  |
| 125 % sur un écran à gauche (coordonnées négatives) | OK |  |
| Bord d'écran : la roue est recadrée pour rester visible (barre des tâches exclue) | OK |  |
| Décalage configurable appliqué à l'échelle de l'écran | OK |  |
| Curseur entre deux écrans : écran le plus proche | OK |  |

## Roue — géométrie et navigation

| Test | Résultat | Détail |
|---|---|---|
| Test de zone : centre, secteurs, extérieur | OK |  |
| Survol prolongé d'une branche : ouverture du sous-menu pendant le glisser | OK |  |
| Dépôt sur une feuille : exécution | OK |  |
| Dépôt sur une branche : la roue reste ouverte en mode clic | OK |  |
| Dépôt au centre ou Échap : annulation | OK |  |
| Action indisponible : jamais exécutée, raison affichée | OK |  |
| Action destructive marquée (confirmation obligatoire), hors du menu principal | OK |  |

## Roue — adaptation au type de fichier

| Test | Résultat | Détail |
|---|---|---|
| MP4 : conversions vidéo et extraction audio mises en avant | OK |  |
| DOCX : PDF mis en avant | OK |  |
| Image : formats d'image et redimensionnement | OK |  |
| Archive : extraction et recompression | OK |  |
| Programme déguisé : conversion désactivée | OK |  |
| Sélection multiple : libellés et actions adaptés | OK |  |
| Pas plus de 10 secteurs par anneau | OK |  |

## Roue express (formats et outils directement dans la roue)

| Test | Résultat | Détail |
|---|---|---|
| Par défaut : disposition express | OK |  |
| Fichier convertible (vidéo) : formats cibles en gros, puis Autres formats et Fichier | OK |  |
| « Fichier » contient les actions classiques (copier, déplacer, renommer, corbeille, plus) | OK |  |
| Image : outils (recadrer, fond, compresser, annoter, infos) + Convertir + Fichier | OK |  |
| Plusieurs images : seuls les outils par lot restent actifs | OK |  |
| Dossier, archive, programme : disposition classique | OK |  |
| Aperçu des roues express (journal) | OK |  |
| Libellés courts des secteurs (12 caractères au plus) | OK |  |

## Glisser-déposer — sécurité

| Test | Résultat | Détail |
|---|---|---|
| La roue ne répond jamais « Déplacer » à la source OLE | OK |  |

## Opérations sur les fichiers

| Test | Résultat | Détail |
|---|---|---|
| Noms Windows : caractères interdits, noms réservés, points finaux | OK |  |
| Chemins d'archive : « zip slip » bloqué | OK |  |
| Noms uniques « (2) », « (3) » et doubles extensions | OK |  |
| Copie : conflit => renommage, original et destination intacts | OK |  |
| Copie : politique « Ignorer » et « Demander » (appliquer à tous) | OK |  |
| Déplacement : la source disparaît seulement après réussite | OK |  |
| Déplacement d'un dossier dans lui-même refusé | OK |  |
| Copie de dossier récursive (accents, espaces) | OK |  |
| Annulation pendant une grosse copie : pas de fichier partiel | OK |  |
| Gros fichier (1 Gio) copié et vérifié | OK |  |
| Espace disque insuffisant : refus avant de commencer | OK |  |
| Renommage : validation, conflit, casse seule | OK |  |
| Renommage en lot avec modèle {nom} et {n:000} | OK |  |
| Suppression définitive : éléments absents signalés, pas d'exception | OK |  |

## Actions personnalisées — sécurité

| Test | Résultat | Détail |
|---|---|---|
| Arguments : un jeton = un argument, aucune interprétation par un shell | OK |  |
| Modèle mal formé refusé ; programme relatif refusé | OK |  |
| Programme modifié depuis l'enregistrement : détecté | OK |  |
| Interpréteur de commandes signalé | OK |  |
| Filtre d'extensions et de dossiers | OK |  |

## Paramètres et stockage

| Test | Résultat | Détail |
|---|---|---|
| Fichier de paramètres corrompu : valeurs par défaut + copie de sauvegarde | OK |  |
| Valeurs hors bornes corrigées | OK |  |
| Enregistrement atomique puis relecture | OK |  |

## Détection des formats

| Test | Résultat | Détail |
|---|---|---|
| photo exif.jpg -> jpg | OK |  |
| logo transparent.png -> png | OK |  |
| anime.gif -> gif | OK |  |
| dégradé.bmp -> bmp | OK |  |
| vert.tiff -> tiff | OK |  |
| cercle.webp -> webp | OK |  |
| clip vidéo.mp4 -> mp4 | OK |  |
| film.mkv -> mkv | OK |  |
| ancien.avi -> avi | OK |  |
| tonalité.wav -> wav | OK |  |
| chanson.mp3 -> mp3 | OK |  |
| piste.flac -> flac | OK |  |
| photos été.zip -> zip | OK |  |
| liens.tar.gz -> tgz | OK |  |
| livre.epub -> epub | OK |  |
| rapport annuel.docx -> docx | OK |  |
| présentation.pptx -> pptx | OK |  |
| rapport.pdf -> pdf | OK |  |
| cube.stl -> stl | OK |  |
| police.ttf -> ttf | OK |  |
| données.json -> json | OK |  |
| config.yaml -> yaml | OK |  |
| inventaire.csv -> csv | OK |  |
| catalogue.xml -> xml | OK |  |
| lisez-moi.md -> md | OK |  |
| page.html -> html | OK |  |
| message.eml -> eml | OK |  |
| contacts.vcf -> vcf | OK |  |
| agenda.ics -> ics | OK |  |
| boite.mbox -> mbox | OK |  |
| Extension trompeuse : PNG nommé .jpg => contenu réel + avertissement | OK |  |
| Fichier sans extension identifié par signature | OK |  |
| Programme déguisé en image : signalé comme exécutable | OK |  |
| Fichier vide et contenu incohérent | OK |  |
| PDF tronqué signalé | OK |  |
| Lecture limitée à l'en-tête (fichier de 2 Gio détecté instantanément) | OK |  |

## Moteurs détectés

| Test | Résultat | Détail |
|---|---|---|
| Détection des moteurs externes | OK |  |
| Moteur absent : capacité affichée « à installer », jamais disponible | OK |  |

## Images (ImageMagick)

| Test | Résultat | Détail |
|---|---|---|
| JPEG -> PNG : dimensions conservées, sans perte | OK |  |
| Orientation EXIF appliquée (120×80 orienté => 80×120) | OK |  |
| Métadonnées : conservées par défaut, supprimées sur demande (GPS) | OK |  |
| PNG transparent -> JPEG : avertissement + fond blanc | OK |  |
| PNG transparent -> WebP : transparence conservée | OK |  |
| Redimensionnement « largeur 60 » avec ratio conservé | OK |  |
| GIF animé -> WebP : animation conservée ; -> PNG : 1re image + avertissement | OK |  |
| Image -> ICO multi-tailles | OK |  |
| BMP -> TIFF -> PNG (aller-retour) | OK |  |
| SVG : refus des références externes (sécurité) | OK |  |
| Image endommagée : erreur claire, pas de plantage | OK |  |

## PDF

| Test | Résultat | Détail |
|---|---|---|
| Images JPEG + PNG transparent -> un PDF (intégré, sans recompression) | OK |  |
| PNG entrelacé (Adam7) et 16 bits décodés par le moteur intégré | OK |  |
| TXT accentué -> PDF intégré, texte relu à l'identique (pdftotext) | OK |  |
| PDF -> pages PNG (PDFium) : une image par page, dimensions à 100 ppp | OK |  |
| PDF -> TXT (PDFium) : texte extrait | OK |  |
| PDF : plage de pages et pages inexistantes | OK |  |
| PDF endommagé : erreur claire | OK |  |

## Documents (LibreOffice)

| Test | Résultat | Détail |
|---|---|---|
| DOCX -> PDF : texte présent | OK |  |
| DOCX -> ODT -> DOCX | OK |  |
| PPTX -> PDF puis diapositives en PNG (3 images) | OK |  |
| Markdown -> PDF (rendu intégré + LibreOffice) | OK |  |
| XLSX (intégré) -> PDF (LibreOffice) | OK |  |
| PDF -> DOCX : reconstruction approximative annoncée | OK |  |

## Vidéo (FFmpeg)

| Test | Résultat | Détail |
|---|---|---|
| MKV (H.264/AAC + sous-titres) -> MP4 : remuxage sans perte, sous-titres convertis | OK |  |
| MP4 -> WebM : réencodage VP9/Opus annoncé avec perte | OK |  |
| AVI (MPEG-4) -> MP4 : réencodage H.264, résolution 120p | OK |  |
| Découpe 1 s -> 3 s et suppression de l'audio | OK |  |
| Vidéo -> GIF animé | OK |  |
| Miniature JPEG et séquence d'images (1 par seconde) | OK |  |
| Vidéo endommagée (tronquée) : erreur claire, aucune sortie | OK |  |
| Annulation d'une conversion longue : processus arrêté, temporaire supprimé | OK |  |
| Copie impossible demandée explicitement : refus explicite | OK |  |

## Audio (FFmpeg)

| Test | Résultat | Détail |
|---|---|---|
| Vidéo -> MP3 (extraction audio) | OK |  |
| Vidéo -> M4A : piste AAC copiée sans réencodage | OK |  |
| MP3 -> FLAC : avertissement « ne restaure pas la qualité », pochette et titre conservés | OK |  |
| WAV -> Opus profil voix : mono | OK |  |
| Normalisation, 44,1 kHz et découpe | OK |  |
| Fusion de 3 fichiers audio en un seul | OK |  |

## Données

| Test | Résultat | Détail |
|---|---|---|
| JSON imbriqué -> CSV aplati (point-virgule) : colonnes « parent.enfant » | OK |  |
| JSON imbriqué -> CSV en mode « refuser » : erreur explicite | OK |  |
| CSV (point-virgule, guillemets, multi-lignes, accents) -> JSON typé | OK |  |
| CSV -> XLSX (intégré) -> CSV : aller-retour identique ; XLSX validé par LibreOffice | OK |  |
| XML -> JSON (attributs @, éléments répétés en tableau) -> XML | OK |  |
| XML avec DTD / entité externe : refusé (XXE) | OK |  |
| JSON invalide : erreur avec numéro de ligne | OK |  |
| YAML -> JSON : comparé à PyYAML (référence) | OK |  |
| JSON -> YAML relu par PyYAML à l'identique | OK |  |
| YAML avec ancres : refus explicite | OK |  |
| CSV -> XLSX : injection de formule neutralisée à la sortie CSV | OK |  |

## Texte, Markdown, HTML, livres

| Test | Résultat | Détail |
|---|---|---|
| Markdown -> HTML : titres, tâches, tableau, code ; script et javascript: neutralisés | OK |  |
| HTML -> TXT : scripts et styles ignorés, listes et entités | OK |  |
| EPUB -> HTML (intégré) : chapitres, image incluse, script retiré | OK |  |
| EPUB protégé (DRM) : refusé | OK |  |
| EPUB -> AZW3 et -> PDF (Calibre) | OK |  |

## 3D, polices, e-mails

| Test | Résultat | Détail |
|---|---|---|
| STL -> OBJ -> relu : 12 triangles, 8 sommets, 10 × 10 × 10 | OK |  |
| STL -> PLY -> relu : 12 triangles, 8 sommets, 10 × 10 × 10 | OK |  |
| STL -> 3MF -> relu : 12 triangles, 8 sommets, 10 × 10 × 10 | OK |  |
| STL -> GLB -> relu : 12 triangles, 8 sommets, 10 × 10 × 10 | OK |  |
| GLB validé par une lecture JSON indépendante (Python) | OK |  |
| TTF -> WOFF -> TTF : tables identiques (vérifié par fontTools) | OK |  |
| EML -> TXT : en-têtes décodés, texte, pièce jointe listée | OK |  |
| EML -> HTML : script, onclick et image espion retirés | OK |  |
| MBOX -> 3 fichiers EML | OK |  |
| VCF (vCard 3.0 et 2.1 quoted-printable) -> CSV | OK |  |
| ICS -> CSV : événements, journée entière, répétition, alarmes ignorées | OK |  |

## Gestionnaire de conversion — garanties

| Test | Résultat | Détail |
|---|---|---|
| Accents et espaces dans les noms ; conflit => « (2) » | OK |  |
| Politique « Ignorer » : tâche ignorée, fichier existant intact | OK |  |
| Politique « Écraser » explicite : remplace la sortie, jamais la source | OK |  |
| Manque d'espace disque : refus avant conversion | OK |  |
| Programme déguisé : conversion refusée | OK |  |
| Contenu réel différent de l'extension : conversion choisie refusée avec explication | OK |  |
| Réglage invalide refusé avant lancement | OK |  |
| File d'attente : lot de 6 dont 1 fichier endommagé, les autres réussissent | OK |  |
| File d'attente : pause (aucune nouvelle tâche), reprise, annulation d'une tâche en attente | OK |  |
| Historique : enregistrement, recherche, suppression, désactivation | OK |  |
| Recherche de formats : « WebP » | OK |  |
| Nettoyage des temporaires abandonnés | OK |  |

## Archives — création, extraction, vérification

| Test | Résultat | Détail |
|---|---|---|
| ZIP (intégré) : création d'un dossier avec accents, vérification CRC, extraction identique | OK |  |
| ZIP corrompu : vérification en échec | OK |  |
| TAR.GZ (intégré) : plusieurs éléments | OK |  |
| GZ d'un seul fichier, puis GZ de plusieurs => TAR.GZ | OK |  |
| 7Z avec mot de passe et en-têtes chiffrés (7-Zip) | OK |  |
| ZIP chiffré AES-256 (7-Zip) puis extraction (7-Zip) | OK |  |
| XZ et BZ2 (7-Zip) | OK |  |
| Mot de passe refusé pour TAR.GZ (format sans chiffrement) | OK |  |

## Archives — sécurité

| Test | Résultat | Détail |
|---|---|---|
| Zip slip : entrées « ../ », absolues, lecteur et flux NTFS ignorées ; rien hors du dossier | OK |  |
| Zip slip via 7-Zip : extraction refusée | NON EXÉCUTÉ | 7-Zip de cet environnement normalise les chemins : impossible de fabriquer l'archive piégée |
| Bombe de décompression (80 Mio de zéros, taux ×1000) : arrêtée | OK |  |
| TAR avec liens symboliques et matériels : liens ignorés, évasion refusée | OK |  |
| Extraction : jamais d'écrasement (dossier numéroté) | OK |  |
| Conversion d'archive ZIP -> 7Z via le convertisseur (contenu identique) | OK |  |
| Extraction via le convertisseur (dossier de sortie validé) | OK |  |
| RAR : extraction seulement avec 7-Zip complet (jamais de création) | NON EXÉCUTÉ | aucune archive RAR d'essai (outil rar non libre absent de l'environnement) |

## Partie Windows — non exécutable dans cet environnement

| Test | Résultat | Détail |
|---|---|---|
| Crochets clavier/souris réels (WH_MOUSE_LL / WH_KEYBOARD_LL) et masquage du menu Alt | NON EXÉCUTÉ | nécessite Windows : à valider sur PC avec docs/VERIFICATION-WINDOWS.md (le code C# est compilé et vérifié par typage ici) |
| Roue en cible de dépôt OLE au-dessus de l'Explorateur et du Bureau (CF_HDROP, effet Copier) | NON EXÉCUTÉ | nécessite Windows : à valider sur PC avec docs/VERIFICATION-WINDOWS.md (le code C# est compilé et vérifié par typage ici) |
| Glisser-déposer natif intact quand le fichier est lâché hors de la roue | NON EXÉCUTÉ | nécessite Windows : à valider sur PC avec docs/VERIFICATION-WINDOWS.md (le code C# est compilé et vérifié par typage ici) |
| Corbeille, Propriétés, Ouvrir avec, Partager, raccourci .lnk (API Shell) | NON EXÉCUTÉ | nécessite Windows : à valider sur PC avec docs/VERIFICATION-WINDOWS.md (le code C# est compilé et vérifié par typage ici) |
| Icône de la zone de notification, bulles, démarrage avec Windows (clé Run) | NON EXÉCUTÉ | nécessite Windows : à valider sur PC avec docs/VERIFICATION-WINDOWS.md (le code C# est compilé et vérifié par typage ici) |
| Fenêtres WPF : thème hybride clair/sombre, DPI par écran, lecteur d'écran | NON EXÉCUTÉ | nécessite Windows : à valider sur PC avec docs/VERIFICATION-WINDOWS.md (le code C# est compilé et vérifié par typage ici) |
| Conversions par les codecs Windows (WIC) | NON EXÉCUTÉ | nécessite Windows : à valider sur PC avec docs/VERIFICATION-WINDOWS.md (le code C# est compilé et vérifié par typage ici) |
| Installateur FileWheel-Setup.exe sur un vrai Windows (installer, mettre à jour, désinstaller) | NON EXÉCUTÉ | nécessite Windows : à valider sur PC avec docs/VERIFICATION-WINDOWS.md (le code C# est compilé et vérifié par typage ici) |

