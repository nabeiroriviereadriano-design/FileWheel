# Charte graphique — hybride Windows 11 + macOS

FileWheel est une application Windows : elle garde les repères qui font qu'on s'y sent chez soi (couleur d'accent, icônes, police, boutons de fenêtre), et emprunte à macOS sa sobriété, ses surfaces groupées et ses contrôles « doux ». Tout est défini dans `src/FileWheel.App/Ui/Theme.cs`.

## Ce qui vient de chaque système

| Élément | Windows 11 | macOS | Choix FileWheel |
|---|---|---|---|
| Couleur d'accent | Accent système | Bleu système | **Accent de Windows**, éclairci en mode sombre |
| Icônes | Segoe Fluent Icons | SF Symbols | **Segoe Fluent Icons** |
| Police | Segoe UI Variable | SF Pro | **Segoe UI Variable** (Display pour les titres) |
| Boutons de fenêtre | Réduire / Agrandir / Fermer à droite | Feux tricolores à gauche | **Windows** (ne pas perturber les réflexes) |
| Fond de fenêtre | Mica | Gris neutre | **Gris neutre macOS** (#F5F5F7 / #1F1F21) |
| Barre latérale des paramètres | Liste + pilule d'accent | Barre teintée, sélection arrondie | **Barre teintée macOS + pilule d'accent Windows** |
| Groupes de réglages | Cartes « Settings » | Groupes arrondis | **Cartes arrondies 11 px**, fines séparations entre lignes |
| Titres de page | 28 px Semibold | Grand titre | **26 px Segoe UI Variable Display** |
| Cases à cocher des réglages | Interrupteur | Interrupteur | **Interrupteur** (libellé à gauche, bascule à droite) |
| Choix courts | Liste déroulante | Contrôle segmenté | **Contrôle segmenté en pilule** (2 à 5 choix) |
| Boutons | Plats, coins 4 px | « Push » à léger relief | **Coins 7 px, léger dégradé et liseré inférieur** ; plats au survol |
| Bouton principal | Accent | Accent | **Accent à dégradé**, texte blanc ou noir selon le contraste |
| Ordre des boutons de dialogue | Principal à gauche | Principal à droite | **Principal à droite**, Annuler à gauche, partout |
| Pied de boîte de dialogue | Bandeau teinté | — | **Bandeau teinté** avec séparateur |
| Anneau de focus clavier | Double contour | Halo d'accent | **Halo d'accent 3 px** |
| Champs, listes, défilement | Thème Fluent | — | **Thème Fluent de WPF** |
| Roue | — | Verre dépoli, ombre douce | **Disque « verre »** (dégradé vertical, reflet sur le bord supérieur, ombre radiale), **secteur actif en accent Windows** à dégradé |

## Palette

| Jeton | Clair | Sombre |
|---|---|---|
| Fond de fenêtre | #F5F5F7 | #1F1F21 |
| Barre latérale | #EAEAEE | #29292C |
| Surface (cartes) | #FFFFFF | #2B2B2E |
| Texte principal | #1D1D1F | #F5F5F7 |
| Texte secondaire | #6E6E73 | #98989F |
| Interrupteur éteint | #E1E1E6 | #48484D |
| Danger | #D72C1F | #FF453A |
| Accent | accent Windows | accent Windows + 22 % de blanc |

## Règles

- **Clair/sombre automatique** et changement d'accent appliqués **en direct** (écoute de WM_SETTINGCHANGE / WM_DWMCOLORIZATIONCOLORCHANGED), sans redémarrer.
- **Aucun effet bitmap sur du texte** : les ombres sont des calques séparés, pour garder le lissage ClearType.
- **Rayons** : contrôles 7 px, cartes 11 px, interrupteur et segments en pilule.
- **Espacements** : marges de page 24 px, cartes 14–16 px, lignes de réglage ≥ 30 px de haut.
- **Accessibilité** : contraste du texte sur l'accent calculé (texte noir sur accent clair), focus clavier toujours visible, Échap ferme les dialogues, Entrée valide (sauf pour une action destructive, où Entrée = Annuler).

## Logo et icônes (0.2.0)

- **Logo** : tuile « squircle » (forme des icônes macOS) en bleu d'accent Windows à léger dégradé, roue blanche à 6 secteurs dont celui du haut en bleu clair (le secteur survolé). Sous 32 px, la tuile remplit le carré et la roue s'épaissit. Sources : `assets/filewheel.svg`, `assets/logo_gen.py`, `assets/logo_icon.py`.
- **Icône de notification** : monochrome comme les icônes système de Windows 11 (blanche sur barre sombre, noire sur barre claire), dessinée à la volée : grisée si la roue est désactivée, secteurs qui se remplissent pendant une tâche, pastille verte / rouge en fin de tâche.
- **Installateur** : bandeau latéral au dégradé bleu du logo.

## Roue Express (0.2.0)

- Formats cibles écrits **en gros** (gras, taille ≈ diamètre / 16,5), ajustés à la largeur du secteur.
- Outils et actions : icône Fluent + **libellé court en majuscules** (≈ diamètre / 33), désactivable (*Paramètres › Roue*).
- Secteurs en **tuiles de verre** séparées par un fin interstice plus transparent ; secteur survolé en dégradé d'accent.
- Centre au repos : taille du fichier (gras) et nom.
