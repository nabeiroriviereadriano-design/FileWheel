# Liste de contrôle sur un PC Windows

À dérouler après `INSTALLER.bat` (ou avec `FileWheel-Setup-<version>.exe` téléchargé depuis les Releases du dépôt GitHub), car ces points n'ont pas pu être exécutés dans l'environnement de développement. Cochez ce qui fonctionne et notez ce qui ne fonctionne pas (avec le journal : menu de l'icône › *Ouvrir le dossier des journaux*).

## Installation
- [ ] `INSTALLER.bat` compile, crée `FileWheel-Setup-<version>.exe` et le lance (ou : le .exe des Releases s'exécute après *Informations complémentaires › Exécuter quand même* de SmartScreen).
- [ ] Installation : écran Bienvenue aux couleurs de FileWheel, options (démarrage avec Windows, raccourci Bureau), FileWheel lancé à la fin.
- [ ] Mise à jour depuis une version précédente : écran « Mise à jour », réglages conservés, FileWheel relancé.
- [ ] Même version relancée : écran Réparer / Désinstaller.
- [ ] FileWheel apparaît dans le menu Démarrer et dans *Paramètres › Applications installées*.
- [ ] L'icône monochrome est visible près de l'horloge ; son menu fonctionne.

## Roue
- [ ] Glisser un fichier dans l'Explorateur + Alt : la roue apparaît sous le curseur.
- [ ] Même chose sur le Bureau.
- [ ] Lâcher hors de la roue : le fichier est copié/déplacé normalement par Windows.
- [ ] Lâcher au centre ou Échap : rien ne se passe, le fichier reste en place.
- [ ] Relâcher Alt après l'apparition : la barre de menus de l'Explorateur ne s'active pas.
- [ ] Glisser sans Alt, cliquer, appuyer sur Alt seul : pas de roue.
- [ ] Sous-menu (Archive, Plus) : s'ouvre au survol ; dépôt sur un sous-menu → la roue reste et se pilote au clic.
- [ ] Deux écrans / zoom 125–200 % : la roue est nette, à la bonne taille, jamais coupée par le bord.
- [ ] Le fichier d'origine n'est jamais déplacé quand on dépose sur une action « Copier ».

## Actions
- [ ] Copier puis Ctrl+V dans un dossier.
- [ ] Déplacer vers… (conflit de nom : la boîte de dialogue propose « Garder les deux »).
- [ ] Renommer (un fichier, puis plusieurs en lot).
- [ ] Compresser en ZIP / Extraire ici.
- [ ] Corbeille, Supprimer définitivement (confirmation, Entrée = Annuler).
- [ ] Ouvrir avec, Propriétés, Dossier parent, Terminal ici, Créer un raccourci, Partager.

## Roue Express et outils image
- [ ] PDF glissé : PNG, TXT, DOCX, JPG en gros dans la roue ; lâcher sur JPG crée les pages à côté.
- [ ] Image glissée : Recadrer, Fond, Compresser, Annoter, Infos, Convertir, Fichier.
- [ ] Recadrer (proportions, rotation), Fond (dégradé, marge, coins, ombre), Compresser (poids estimé), Annoter (flèche, texte, masquer, flouter, Ctrl+Z), Infos (GPS signalé, copie sans métadonnées).
- [ ] Au centre de la roue : taille du fichier et nom au repos.

## Conversion
- [ ] Image PNG → JPEG via la roue (codecs Windows, sans moteur).
- [ ] Paramètres › Moteurs › installer FFmpeg : la boîte de consentement affiche source, version, taille ; l'installation vérifie l'empreinte.
- [ ] Vidéo MP4 → MP3 ; annulation en cours de conversion (fichier partiel supprimé).
- [ ] Notification de fin ; clic → l'Explorateur sélectionne le résultat.
- [ ] Fenêtre Tâches : progression et temps restant.

## Charte graphique
- [ ] Mode clair et mode sombre (changer dans Windows pendant que FileWheel est ouvert : bascule en direct).
- [ ] Changer la couleur d'accent Windows : boutons principaux, interrupteurs, roue suivent.
- [ ] Paramètres : barre latérale, cartes groupées, interrupteurs, focus clavier visible (Tab).
- [ ] Lecture au Narrateur des boutons et réglages.

## Désinstallation
- [ ] *Applications installées › FileWheel › Désinstaller* : FileWheel s'arrête, tout est retiré ; les réglages ne sont supprimés que si la case est cochée.
- [ ] Après désinstallation : plus de démarrage automatique, plus de raccourci, dossier du programme supprimé.
