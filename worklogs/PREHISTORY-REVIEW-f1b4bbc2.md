# Préhistoire océanique — exécution et revue f1b4bbc2

## Décision

Le candidat cumulatif n'est plus seulement du C# préparé : il est compilé et exécuté sur Windows et Linux. Les anciennes entrées de génération restent disponibles. Les fonctions explicites de chronologie et de continuation sont conservées pour la suite, sans promotion géographique, sans modification de main ou de la PR de rhéologie, sans érosion. La présente revue ne vaut pas revue d'un second agent.

Code Core qualifié : fae4d91b2d8ed0d32fedc57c75a3079ecbcd9e02. Code de campagne : f1b4bbc2e645114670790935e928b1aca8fcf35d (Core identique). Correction du test négatif PowerShell et récupération : d1b89d74c200fb47686eca2f922980c16289471f (Core et runner numérique inchangés).

## Qualification exécutée

Run 35692495018 : 62 contrôles C# PASS sur Windows et Linux, puis comparaison PASS. Les 11 fichiers C#/projet/comparateur ont été confrontés octet à octet au dernier payload local ; seule la capture du journal dans le workflow diffère. SDK 10.0.400, runtime hôte 10.0.12 dans les artefacts récupérés. Aucun changement de global.json, NuGet ou appel à l'API Vintage Story.

Le bassin analytique 512² a des dates, altitudes historiques et réponses thermiques séparées identiques entre plateformes (écart maximal 0 dans cette campagne). Il reste un cas contrôlé, pas une nouvelle planète. Les tests incluent trois régressions matérielles 128² de durée 36 et des continuations aux pas alignés. Aucun test Python n'a été compté comme un test C#.

## Expérience sur le monde entier

Run de génération 35692828873 : six calculs Windows/Linux × seeds -437287116, 20260906 et 73 ont produit chacun les trois états 0/36/72. Atlas entier 1 000 000 × 1 000 000, 512² cellules matérielles et altimétriques, pas 1953,125 unités/blocs sur ce monde. Il ne s'agit pas du solveur non linéaire 128² de la branche de rhéologie : les vitesses restent prescrites et attachées aux matériaux. Le même calendrier à mouvements constants est prolongé, pas remplacé par des événements aléatoires.

L'état continental initial est apparié au budget des assemblages e1d88001. Les nouvelles sorties à 0 et 36 ont été comparées aux vrais anciens champs d'altitude : écart maximal 0 pour les six comparaisons, et identités d'assemblage identiques. Ce contrôle inter-version complète les tests comparant deux entrées du Core refactorisé.

Les altitudes natives signées sont exportées directement dans elevation-model.f64le. Les Y en blocs sont leur conversion inchangée Y=168+12*km modèle. Pas de masque marin, déplacement du niveau de référence, contraste par carte, nouvelle colonne thermique additionnée ou érosion. Tous les champs float64 sont accompagnés de leur empreinte.

## Échec de workflow et récupération explicite

La campagne 35692828873 reste FAILED : le test de refus d'écrasement a propagé son code natif attendu après avoir produit ses preuves de refus et de préservation. Les six calculs numériques, leurs COMPLETE.json et leurs fichiers ont été conservés. Ne pas présenter le workflow original comme réussi.

La récupération 35693519463 est PASS. Le script corrigé a été compilé/testé avec le runner réel sur Windows/Linux, refuse un dossier sentinelle intact et termine explicitement avec succès après les assertions. Le job de comparaison récupère les six artefacts originaux sans relancer les mondes, exige leurs reçus de complétude, leurs empreintes et leur continuité, puis compare les 8 champs × 3 temps × 3 seeds. Écart maximal observé 0, avec tolérance inchangée de 1e-8 et codes de heightmap PNG16 identiques. Le contrôle négatif inclut le message IOException attendu pour ne pas accepter un échec de compilation comme un refus d'écrasement.

## Résultat de l'expérience de durée

La part héritée porte sur le VOLUME de croûte océanique de toutes les colonnes, y compris mixtes/émergées. Elle ne désigne ni la surface de l'océan ni un volume d'eau.

| Seed | Volume océanique hérité à 36 | À 72 | Sommet au-dessus de Y168 à 36 / 72 |
|---|---:|---:|---:|
| -437287116 | 90,3362 % | 80,6441 % | 37,2288 / 81,2829 blocs |
| 20260906 | 92,9375 % | 85,1317 % | 49,3322 / 96,1429 blocs |
| 73 | 92,1306 % | 84,5669 % | 48,9583 / 98,9706 blocs |

Les neuf heightmaps entières ont été examinées avec la même conversion grise fixe. Des soulèvements plus forts apparaissent, mais les grandes masses restent largement héritées de l'initialisation et les bandes océaniques demeurent trop schématiques. Les profils continus exposent encore de longs fonds à faible variation et des raccordements abrupts de certaines bandes. Augmenter seulement la durée renforce des reliefs sans restituer la préhistoire manquante. Ces résultats ne permettent ni d'accepter les cartes ni d'attribuer chaque défaut à un paramètre isolé.

La couverture demeure INCOMPLETE_INHERITED_FORMATION_HISTORY. L'âge uniforme initial de 50 est conservé comme témoin explicite ; à 72, la matière héritée non mélangée porte 122. La variation de l'âge moyen d'une cellule ne prouve pas le remplacement de toute sa matière. Ne pas injecter un âge aléatoire pour faire disparaître ce diagnostic.

## Cartographie de livraison

Réexport RCV depuis les float64 exacts, sans nouveau calcul de terrain. Neuf rasters gris16, soit 2 359 296 pixels indépendamment décodés avec Pillow ; 54 transects systématiques. Les 72 champs sources Linux ont été vérifiés contre leurs hashes. Les manifestes d'origine sont préservés ; un adaptateur de lecture ajoute uniquement les extrema mesurés requis par RCV et le rôle initial-height à t=0. Il n'altère aucun octet d'altitude. Les données natives originales restent dans data/, distinctes de la reconstruction affine secondaire du lecteur RCV existant.

Les cartes régionales fines, une nouvelle comparaison statistique ETOPO, un modèle thermique complet couplé aux cohortes et une revue de deuxième agent n'ont pas été exécutés. Les liens exacts de sources, champs et rapports sont fournis dans le manifeste de livraison.

## Reprise

Le blocage de compilation des candidats cumulatifs est levé. Avant d'allonger davantage la simulation, construire et vérifier la provenance des portions héritées : événements de naissance et transport/recyclage réellement liés, avec couverture des zones non expliquées. Des épisodes prescrits analytiques et une continuation conservatrice sont des outils, pas encore une reconstruction automatique d'une lithosphère géologiquement plausible. La géométrie de subduction, la mobilité des limites et la réponse du manteau demeurent des travaux distincts.

Acceptation du relief brut : REJECTED. Érosion : NOT_RUN. Qualification native Vintage Story, MCP Visual Studio et revue indépendante d'un second agent : NOT_RUN. Aucun registre de lot ou monde personnel modifié.
