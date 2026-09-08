# Cartographie de recette des sorties spatiales

## Règle

Un test qui calcule ou matérialise un champ spatial produit une image de diagnostic lorsqu’elle rend son oracle plus contrôlable : grille de hauteur, humidité, précipitations, vents, relief, strates, drainage, débit, sols et fertilité, potentiel de minerai, végétation, neige/glace ou occupation. Les images complètent les assertions déterministes et les parcours en jeu ; elles ne constituent jamais seules un PASS.

Un test scalaire, une exception, un contrat, une API ou une mesure de performance ne crée pas artificiellement une image. La mission consigne pourquoi une carte est pertinente ou pourquoi elle ne l’est pas.

## Contrat d’artefact

Chaque campagne qui génère une carte écrit sous un répertoire ignoré, par exemple `artifacts/maps/<lot>/<test>/<seed>/`, avec :

- un PNG sans perte par couche utile ;
- `manifest.json` UTF-8 : commit, version, seed, profil, dimensions, emprise, projection, couche, unité, valeur manquante, min/max observés et de palette, checksum SHA-256 et paramètres de rendu ;
- l’oracle numérique associé (JSON, TRX ou rapport) et son statut réel PASS/FAIL/BLOCKED/NOT_RUN ;
- une palette et une convention documentées. Les valeurs inconnues, masquées, hors domaine et zéro restent visuellement distinctes.

Les rasters ont origine, dimensions et emprise explicites. Une carte de vent inclut direction et intensité : des flèches peuvent être ajoutées, sans supprimer le raster de magnitude ni les valeurs source. Aucun lissage, interpolation, recadrage ou normalisation par image ne masque une discontinuité ; la palette est gelée par campagne.

## Couches habituelles

| Domaine | Couches de diagnostic |
|---|---|
| Relief et géologie | heightmap, hauteur de roche, pente, formation/strate, épaisseur, faille/intrusion |
| Climat | température, précipitation, humidité, évapotranspiration, vent direction/intensité |
| Hydrologie | accumulation, direction de flux, débit, recharge, lac/océan/connectivité, bassin |
| Sols et ressources | substrat, dépôt, humidité du sol, fertilité, salinité, potentiel et occurrences de minerai |
| Végétation et saison | habitat, densité/couvert, classes de végétation, neige, glace, fonte |

La liste est indicative : une tâche ne produit que les cartes nécessaires à son oracle et aux effets qu’elle modifie.

## Revue et conservation

Les images de la même seed/configuration sont comparées quand le champ est déterministe. Une revue humaine contrôle les motifs évidents (pavage, bandes de bord, valeurs manquantes, inversion d’axe, palette trompeuse) à l’échelle carte ; elle complète les mesures et ne les remplace pas. Les artefacts volumineux restent hors Git, mais leurs manifestes, hashes, chemin local et verdict sont référencés dans le worklog et la passation. Les captures du jeu restent requises pour la matérialisation, l’ergonomie ou le rendu réel.
