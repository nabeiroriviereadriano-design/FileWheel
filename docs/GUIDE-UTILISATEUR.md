# Guide d'utilisation de FileWheel

## 1. Ouvrir la roue

1. Dans l'Explorateur ou sur le Bureau, **commencez à faire glisser** un fichier (ou plusieurs, ou un dossier).
2. Pendant le glisser, **maintenez Alt** (combinaison modifiable dans *Paramètres › Déclenchement*).
3. La roue apparaît sous le curseur. Amenez les fichiers sur une action et **lâchez** : l'action s'exécute.

Ce qui ne déclenche jamais la roue : un clic simple, un glisser sans la touche, une pression isolée sur Alt, Ctrl+Alt ou AltGr quand la combinaison est « Alt », un glisser hors de l'Explorateur et du Bureau, une sélection rectangulaire dans une zone vide.

Pour annuler : **Échap**, lâcher au **centre** de la roue, ou lâcher **hors de la roue** (le dépôt Windows normal a alors lieu là où vous lâchez).

### Sous-menus

Un secteur marqué d'un petit point ouvre un sous-menu quand on le **survole un instant** (un arc indique l'ouverture). Le centre sert alors de bouton « Retour ». Si vous lâchez les fichiers sur un sous-menu, la roue reste ouverte et se pilote **au clic** ; un clic à l'extérieur ou Échap la ferme.

### Disposition « Express » (par défaut)

La roue s'adapte au fichier glissé :

| Fichier | Secteurs |
|---|---|
| Document, PDF, audio, vidéo, données… | Les **formats cibles en gros** (ex. PDF : PNG, TXT, DOCX, JPG) : lâchez dessus, c'est converti à côté de l'original. Puis **Autres** (tous les formats et réglages) et **Fichier**. |
| Image | **Recadrer**, **Fond**, **Compresser**, **Annoter**, **Infos** (métadonnées), **Convertir** (formats en gros), **Fichier**. |
| Dossier, archive, programme | Disposition classique (ci-dessous). |

Au centre : la **taille du fichier** et son nom au repos, la description du secteur survolé sinon. **Fichier** contient Copier, Déplacer vers, Ouvrir avec, Archive, Renommer, Corbeille et Plus.

### Disposition « Classique » (Paramètres › Roue › Disposition)

| Secteur | Contenu |
|---|---|
| Convertir | Jusqu'à 5 conversions directes, « Redimensionner… » pour les images, « Conversion rapide… », « Convertisseur avancé… » |
| Copier | Copie dans le presse-papiers : collez ensuite avec Ctrl+V où vous voulez |
| Déplacer vers… | Dossiers récents / « Envoyer vers », ou « Choisir un dossier… » |
| Ouvrir avec… | Boîte « Ouvrir avec » de Windows (un seul fichier) |
| Archive | Fichiers : compresser en ZIP, 7Z, TAR.GZ, ou « Compresser… » (niveau, mot de passe). Archives : extraire ici, extraire vers…, vérifier, convertir en 7Z / ZIP |
| Renommer | Un élément : nouveau nom (extension préservée). Plusieurs : renommage en lot avec aperçu |
| Corbeille | Placer dans la Corbeille (confirmation activable dans *Paramètres › Roue*) |
| Plus | Ouvrir, dossier parent, terminal ici, propriétés, copier chemin / nom, raccourci, partager, envoyer vers, favoris, mes actions, **supprimer définitivement** (toujours confirmé) |

Jusqu'à deux actions personnalisées peuvent être placées directement sur la roue principale.

Les actions impossibles sont **grisées** et le centre de la roue explique pourquoi.

## 2. Outils image

Chaque outil ouvre une fenêtre avec aperçu ; le résultat est enregistré **à côté de l'original** avec un suffixe (jamais d'écrasement).

| Outil | Ce qu'il fait | Résultat |
|---|---|---|
| Recadrer | Cadre déplaçable avec poignées, proportions Libre / 1:1 / 4:3 / 3:4 / 16:9 / 9:16, rotation ↺ ↻, miroir | `photo recadrée.jpg` |
| Fond | Dégradés et couleurs unies (ou transparent), marge, coins arrondis, ombre, cadre Image / 1:1 / 4:3 / 16:9 | `photo fond.png` |
| Compresser | JPEG ou PNG, qualité, taille maximale (4K, 2560, 1920, 1280), poids estimé en direct ; plusieurs images à la fois | `photo compressée.jpg` |
| Annoter | Flèche, cadre, stylo, texte, **Masquer** (rectangle noir) et **Flouter** (pixellisation), couleurs, épaisseur, Ctrl+Z | `photo annotée.jpg` |
| Infos | Dimensions, appareil, date, réglages de prise de vue, **position GPS** (signalée), logiciel, auteur ; copie sans métadonnées | `photo sans métadonnées.jpg` |

Les photos de téléphone sont redressées automatiquement (orientation EXIF). Les formats que Windows sait lire (PNG, JPEG, BMP, GIF, TIFF, ICO, et WebP/HEIC/AVIF si les extensions Windows sont installées) sont acceptés ; le résultat garde le format d'origine quand Windows sait l'écrire, sinon il est enregistré en PNG.

## 3. Convertir

- **Conversion rapide** : depuis la roue, *Convertir › JPEG* (par exemple). Le fichier converti est créé à côté de l'original avec un nom libre (`photo (2).jpg` si `photo.jpg` existe déjà). Une notification signale la fin ; cliquez dessus pour ouvrir le dossier.
- **Conversion rapide…** : fenêtre de conversion avec toutes les cibles possibles, leurs options (qualité, résolution, codec, pages…) et un avertissement clair des pertes éventuelles avant de lancer.
- **Convertisseur avancé** (menu de l'icône › Convertisseur) : liste de fichiers par glisser-déposer, choix de la cible commune, dossier de sortie, profils enregistrables, conversion par lots.

Règles toujours appliquées :
- l'original n'est jamais modifié ni écrasé ; un fichier existant n'est jamais remplacé sans règle explicite de votre part (*Paramètres › Conversion › En cas de nom déjà pris*) ;
- le résultat est écrit dans un fichier temporaire, vérifié (taille, en-tête, lisibilité), puis renommé : une conversion interrompue ne laisse pas de fichier à moitié écrit ;
- si une conversion n'est pas possible (moteur absent, fichier endommagé, protégé par DRM ou mot de passe), un message clair l'explique.

### Moteurs

Certaines conversions utilisent un programme externe. Dans *Paramètres › Moteurs* :
- **FFmpeg, ImageMagick, 7-Zip, PDFium** : bouton « Télécharger et installer ». FileWheel affiche la source (dépôt GitHub officiel), la version et la taille, puis vérifie l'empreinte SHA-256 publiée avant d'installer dans `%LOCALAPPDATA%\FileWheel\engines`. Rien n'est téléchargé sans votre clic.
- **LibreOffice, Calibre** : installés comme des applications ordinaires via winget (avec votre accord) ; FileWheel les détecte ensuite.
- Vous pouvez aussi indiquer le chemin d'un moteur déjà présent sur le PC.

## 4. Tâches et historique

Clic sur l'icône FileWheel de la zone de notification : fenêtre **Tâches**, avec progression, temps restant estimé, annulation, nouvel essai, ouverture du résultat. La mise en pause n'est proposée que si le moteur la permet (aucun aujourd'hui : l'annulation est toujours possible).

L'**historique** (*Paramètres › Historique*) garde le nom des fichiers, la conversion, la date et le résultat — jamais le contenu. Recherche, désactivation et effacement disponibles.

## 5. Paramètres

| Section | Réglages principaux |
|---|---|
| Général | Roue activée, démarrage avec Windows, niveau du journal |
| Déclenchement | Combinaison de touches, délai d'apparition, distance minimale, Bureau / Explorateur, un ou plusieurs fichiers, fermeture au relâchement de la touche |
| Roue | Disposition Express / Classique, libellés sous les icônes, diamètre, décalage, opacité, flou (expérimental), animations, délai des sous-menus, confirmation Corbeille |
| Conversion | Dossier de sortie, suffixe, conflits de noms, tâches simultanées, priorité, espace libre minimal, plafonds d'extraction |
| Moteurs | Installation, mise à jour, chemin manuel, désinstallation des moteurs |
| Envoyer vers et favoris | Dossiers proposés dans la roue, favoris accessibles depuis l'icône |
| Actions personnalisées | Lancer un programme, copier/déplacer vers un dossier, convertir — avec règles de sécurité (voir SECURITE.md) |
| Historique | Consultation, recherche, effacement |

Les réglages sont enregistrés immédiatement dans `%APPDATA%\FileWheel\settings.json`.

## 6. Icône près de l'horloge

Monochrome (blanche sur barre sombre, noire sur barre claire). Grisée quand la roue est désactivée ; les secteurs se remplissent pendant une conversion ou une copie ; une pastille **verte** (terminé) ou **rouge** (échec) reste jusqu'au prochain clic. Clic droit › **Dépannage** : tester l'affichage de la roue, mode diagnostic, dossier des journaux.

## 7. Mettre à jour, désinstaller

Lancez simplement un `FileWheel-Setup` plus récent : FileWheel est fermé, mis à jour puis relancé, vos réglages conservés. Pour désinstaller : *Paramètres Windows › Applications › FileWheel*, ou relancez le même `FileWheel-Setup` › Désinstaller.

## 8. En cas de problème

- **La roue n'apparaît pas** : FileWheel est-il lancé (icône près de l'horloge) ? La roue est-elle activée ? L'Explorateur est-il lancé en administrateur alors que FileWheel ne l'est pas (ou l'inverse) ? Essayez une autre combinaison dans *Paramètres › Déclenchement*.
- **Journal** : menu de l'icône › *Ouvrir le dossier des journaux* (`%LOCALAPPDATA%\FileWheel\logs`). Le journal ne contient ni le contenu de vos fichiers ni votre nom d'utilisateur.
