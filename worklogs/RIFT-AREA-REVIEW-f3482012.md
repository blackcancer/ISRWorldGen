# Projection conservative des rifts — revue f3482012

## Code, campagne et décision

Base main : 4f31f29a1bf11d8b03276d5466947a3fa5a102d7. Code exécuté : f348201209e446f176dbe02112f07247e50da547. Branche : codex/rift-area-20260923. Campagne https://github.com/blackcancer/ISRWorldGen/actions/runs/35796568882 terminée SUCCESS : Windows, Linux, comparaison.

Conserver cette brique testée dans une PR en brouillon, sans fusion automatique dans main et sans changement de générateur par défaut. Elle ne dépend pas des expériences de mécanique des PR5/PR6. Le candidat C# précédemment local est maintenant réellement compilé contre le Core et exécuté. Aucun terrain de monde n'est généré par cette campagne. La validation géographique antérieure reste négative ; érosion NON LANCÉE.

## Périmètre et mécanique conservée

RiftMaterialRaster.Project utilise les fragments continentaux de RiftNecking et les bandes datées issues de RiftSpreadingAdapter/SpreadingTimeline. Les polygones sont intersectés avec les cellules ; leurs fractions surfaciques, colonnes équivalentes et moments d'âge affines sont intégrés. Les paquets restent séparés par origine, événement et flanc, avec géométrie source non repliée. Les fragments continentaux résiduels ne sont pas effacés ; le fond inconnu n'est pas remplacé par du basalte ou un âge nul. Les chevauchements matériels sont refusés, pas résolus arbitrairement comme une subduction.

Le Core est identique octet à octet au candidat initial importé. Les quatre nouveaux tests portent sur les unités physiques, les fragments indépendants jointifs, la reconstruction depuis les paquets et un oracle analytique de centroïde rectangulaire. Les 29 contrôles précédents sont conservés. Le helper de bilan utilise la métrique fournie par le modèle, pas une constante .01.

Les trois cas axial, oblique avec pause et raccord périodique sont des ruptures contrôlées avec mouvements fournis. Chaque raster couvre le cadre complet 1000000 × 1000000 unités, avec 512² cellules et un pas 1953.125 unités. Les matériaux n'occupent qu'une partie de ce cadre ; l'extérieur reste inconnu. Ce ne sont ni trois seeds de mondes ni des heightmaps.

## Exécution réelle

115 contrôles C# distincts par plateforme : 33 de projection, 22 de déformation matérielle et 60 de rupture/chronologie (18+14+28). Les reçus de succès restent explicitement limités à leurs domaines. Six projections sont calculées : trois expériences × Windows/Linux. Les refus de réutilisation des sorties vérifient code de retour, message et empreintes inchangées.

La comparaison CI et sa réexécution locale sur LES DEUX archives finales téléchargées comparent 3932160 paires de valeurs (3 cas × 5 champs × 512²) : écart maximal observé 0, tolérance de champ 1e-8 inchangée. Les paquets, leurs identifiants et leurs sources concordent également ; les neuf flux PNG16 originaux sont identiques entre plateformes. Cela ne garantit pas tous les matériels/runtimes.

Artefacts finaux : Linux 10724495918, Windows 10724266233, comparaison 10723603935. Leurs SHA-256 sont vérifiés :
- Linux : 90769819ac98ea7fa2e67664f7a2cf06e0d5c7651eb2aeab7714ed6809fd4c4f
- Windows : 4bf2a4729cf4a442cafa89fdbb351cb589371c84a9ba2daa74723c119a8539b9
- Comparaison : 9fbaa81421a7a032365ca4ee39da7fd256343feee727bd44c89821db41ed3a2b

## Oracle géométrique indépendant

Les trois sorties C# finales ont été confrontées à NumPy et Shapely 2.1.2 / GEOS 3.13.1. Les géométries océaniques sont d'abord reconstruites depuis l'intégration des phases de mouvement, puis les intersections et centroïdes GEOS sont utilisés indépendamment du clipping C#. L'âge affine est intégré sur les cellules coupées, sans substituer l'âge au centre de cellule.

86351 contributions polygonales sont contrôlées, dont 7270 intersections partielles (pas nécessairement autant de cellules uniques). Erreur maximale en fraction de surface : 5.007105841059456e-14. Erreur maximale en épaisseur continentale équivalente : 1.0338396805309458e-12 km modèle. Erreur maximale en moment d'âge : 8.753886504564434e-12 km équivalents × Myr modèle. Ces unités différentes ne sont pas regroupées en un score physique.

| Cas | Volume continental km³ modèle | Surface océanique créée km² modèle | Volume océanique km³ modèle |
|---|---:|---:|---:|
| Axial | 218000000 | 2025000 | 14175000 |
| Oblique avec pause | 218000000 | 1225000 | 8575000 |
| Raccord périodique | 218000000 | 2025000 | 14175000 |

Les écarts aux intégrales analytiques sont de l'ordre des arrondis. Le cas oblique possède une pause d'ouverture : son apport océanique plus faible est attendu, pas une perte de matière par projection.

La régression de l'ouverture étroite retrouve 1499.9999999999975 km² modèle pour 1500 attendus, alors qu'aucun centre de la grille 128² n'appartient à cette ouverture. Le cas extrudé historique retrouve également les 10125000 km² exacts attendus dans son assertion C#. Ce cas possède une longueur différente des trois cartes et ne doit pas être confondu avec leurs inventaires.

## Encodage et premiers échecs

2359296 pixels des neuf PNG16 Linux sont décodés avec Pillow indépendamment de l'encodeur. Les fractions utilisent code/65535 ; l'âge océanique moyen utilise code*60/65534, avec 65535 réservé à l'absence de matière océanique. Les originaux ne sont pas modifiés. Les aperçus ajoutés à la galerie sont explicitement des fractions ou des âges, jamais de l'altitude ; le damier des aperçus d'âge distingue l'absence de matière d'un âge zéro.

Huit tests locaux de l'exporteur sont exécutés : conservation des sources et reproduction des PNG, refus du dernier champ corrompu avant écriture, NaN même avec hash cohérent, moment sans porteur, fraction négative, absence de COMPLETE et refus de réutilisation avec empreintes conservées. Vingt-six cas de validation des reçus testent les vrais statuts et refusent PASS générique, FAIL, NOT_RUN, nombre incomplet, liste d'échecs et assertion échouée.

La première campagne 35796216905 au commit 1cc128fd a compilé et réussi tous les contrôles C# et exports sur les deux plateformes, mais son comparateur demandait à tort PASS au lieu des statuts normatifs spécifiques des suites. Le correctif f3482012 lit chaque statut exact, le nombre d'assertions et leurs échecs. Il ne supprime aucun contrôle et ne modifie aucune tolérance. Les champs, géométries, paquets et PNG restent identiques entre les deux campagnes ; les manifestes gardent leur commit d'origine. Les preuves du premier échec sont conservées séparément.

## Livraison et suite

La livraison ISRWorldGen_Projection_Rifts_f3482012 contient la source réellement compilée, les deux archives finales, les champs float64, paquets, géométries, PNG16 et rapports. La galerie HTML est autonome, sans script ou appel réseau ; les aperçus oblique et périodique ont été examinés. Aucun test navigateur n'est revendiqué dans cette campagne.

Cette projection est un maillon entre une séparation déjà résolue et une représentation matérielle conservative. Elle ne calcule pas la localisation des fractures d'un monde, leur propagation bidimensionnelle, le mouvement global des plaques, les collisions ou les fermetures de bassins. Ne pas remplacer les échanges océaniques du générateur par ces entrées de laboratoire et ne pas combler les zones inconnues sans histoire causale.

Prochaine dépendance : décision topologique de séparation issue du modèle matériel/mécanique, avec fragments conjugués et événements datés, avant une nouvelle génération mondiale de reliefs. La validation des heightmaps terrestres et sous-marines reste requise avant érosion.

Revue d'un second agent, calibration terrestre renouvelée, qualification Vintage Story avec DLL cible, jeu/MCP : NOT_RUN. Aucun appel API du jeu ni bibliothèque supplémentaire, framework, registre de lot ou sauvegarde n'a été modifié. Les oracles indépendants numériques ne remplacent pas la revue d'un second agent.
