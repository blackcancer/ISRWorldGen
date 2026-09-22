# Revue du suivi de déformation matérielle — d880ff07

## Code, exécution et décision

Code exécuté : d880ff076687b4f845d784e8eece64b3fec5bdb9. Base : 8d17f97af996f4ad3ed09c715eb5528926d76bec. Branche de vérification : codex/material-strain-20260922.
Campagne : https://github.com/blackcancer/ISRWorldGen/actions/runs/35733679842 . Les neuf jobs sont SUCCESS : deux jobs de contrôles, six mondes Windows/Linux × trois seeds, puis comparaison.

L'incrément a été intégré sur main par fast-forward non forcé, après relecture du HEAD et vérification de l'ascendance. Il ajoute un observateur explicitement appelé par GenerateWithStrain ; Generate et GenerateWithAssemblage conservent leur comportement. La réussite qualifie le périmètre numérique exécuté, pas le relief, la fracture 2D, un lot complet ou la qualification native. Aucun registre, sauvegarde, profil du jeu, framework ou dépendance n'est modifié. Revue d'un second agent : NOT_RUN, distincte des oracles numériques indépendants ci-dessous.

## Pourquoi ce suivi

Le cumul historique de divergence positive est calculé à position de grille fixe. Il ne représente pas automatiquement la déformation d'une parcelle qui se déplace. Le nouvel observateur suit des marqueurs dans les vitesses réellement calculées par MaterialBoundHistory, avec xdot=v et Fdot=grad(v)*F. Le gradient est celui du même interpolant bilinéaire périodique que la vitesse. Les trajectoires utilisent RK2 et F une exponentielle au milieu du pas, avec sous-pas bornés.

J=det(F) mesure l'expansion de surface. h0/J estime l'amincissement sous l'hypothèse advective incompressible : ce n'est PAS l'épaisseur eulérienne après mélange diffusif du donneur ou redistribution de croûte inférieure. Le suivi ne prétend pas reproduire ces opérations. Les directions proviennent de F F^T ; une déformation isotrope ne reçoit pas une normale arbitrairement alignée sur la grille.

Une rotation rigide ne produit pas d'ouverture. Un cisaillement simple peut allonger une direction tout en conservant J=1 : cet allongement seul ne suffit pas à signaler un amincissement. Une extension compensée par sa compression inverse ne devient pas une ouverture permanente. Ces propriétés sont testées. Les vitesses de ce pipeline restent prescrites/cinématiques ; aucune contrainte, résistance ni rupture mécanique n'est déduite artificiellement du seul tenseur F.

## Contrôles effectivement réalisés

22 nouveaux cas C# et 60 régressions rift/chronologie (18+14+28) ont réussi sur chaque plateforme. Ils couvrent notamment rotation, cisaillement, extension, inversion de charge, orientation, ordre des déformations non commutatives, subdivision, métrique rectangulaire, translation périodique, comparaison analytique d'une trajectoire, refus transactionnel en mémoire, immutabilité des snapshots et reproduction de l'amincissement des rubans avant rupture.

Un calcul réel 64² compare avec/sans observateur le checksum de l'histoire, celui des matériaux et toutes les altitudes : ils sont identiques. Les trois mondes complets ont ensuite été exécutés avec leur état initial d'assemblage, la durée modèle 36, une grille matérielle/altimétrique 512² et 128² marqueurs, sur 1 000 000 × 1 000 000 unités. Chaque génération contrôle aussi le redimensionnement de l'atlas entier vers 131072 et 262144 blocs à une position homologue ; cela ne constitue pas une nouvelle qualification exhaustive de toutes les tailles.

Comparaison Windows/Linux : 311576 valeurs de diagnostic par seed, soit 934728 au total. Écart maximal 6.938893903907228e-18, tolérance absolue inchangée 1e-8 ; classifications identiques. Les trois champs d'altitude par seed ont un écart maximal nul ; les codes des heightmaps 16 bits initiales/finales sont identiques. Ce résultat ne vaut pas garantie pour tous les matériels/runtimes.

Les trois artefacts Linux ont été téléchargés et leurs SHA-256 vérifiés : 10696473303 (-437287116), 10696693135 (20260906), 10697036340 (73). Rapport de comparaison : 10695819569. Contrôles Linux : 10696832509. Les cinq fichiers locaux pertinents (observateur, raccordement Core, lanceur, exporteur et comparateur) correspondent octet à octet à l'archive source réellement exécutée.

Vérifications locales indépendantes :
- Les 24 matrices exportées par le C# sont comparées à scipy.linalg.expm : erreur maximale 4.440892098500626e-16.
- Les 49152 tenseurs finaux sont contrôlés par déterminant NumPy, SVD et résidu des axes propres. Erreurs maximales respectives 8.881784197001252e-16, 8.881784197001252e-16 et 2.6645352591003757e-15. L'identité advective h*J=h0 est également contrôlée.
- Les six PNG16 d'altitude sont décodés avec Pillow, indépendamment de l'encodeur du dépôt : 1572864 pixels correspondent exactement à round(Y*65535/383). Erreur de quantification maximale 0.002922102682433092 bloc.
- Leurs trois champs finaux height.f64le sont IDENTIQUES OCTET À OCTET aux résultats de l'assemblage e1d88001. L'observateur n'améliore ni ne dégrade le terrain.
- Sept contrôles synthétiques d'export vérifient rampe, niveaux sous-marins, refus de réutilisation et précontrôle des sources invalides. Ce n'est pas une nouvelle simulation géographique Python.

## Résultats de repérage, pas une carte de fractures

Chaque monde contient 16384 marqueurs. Seuils fixés avant les résultats : C initiale >=10 km modèle, fraction continentale initiale >=0.8, J>2 avec résolution 1e-9 relative et direction principale résolue. Ce sont des seuils DIAGNOSTIQUES, pas des critères géologiques calibrés.

| Seed | Marqueurs initialement continentaux | Marqueurs candidats à l'amincissement |
|---|---:|---:|
| -437287116 | 6007 | 107 |
| 20260906 | 6023 | 150 |
| 73 | 6027 | 63 |

Ces nombres ne représentent ni des nombres de rifts, ni des superficies de bassins. L'espacement initial des marqueurs est 7812.5 unités ; celui des altitudes est 1953.125. Les deux résolutions ne sont pas interchangeables. Le premier franchissement enregistré est un instant ÉCHANTILLONNÉ, pas une date de rupture exacte.

Les données conservent positions initiales et actuelles, matériaux initiaux, F, J, directions et seuils. Le raster auxiliaire de J utilise le repère matériel INITIAL et une conversion monotone fixe non linéaire déclarée. La superposition rouge utilise les positions ACTUELLES. Elle est séparée des PNG de hauteur et ne dessine pas des frontières reliant arbitrairement les points.

## Fidélité et verdict géographique

Les vues entières annotées des trois seeds ont été examinées. Le regroupement des marqueurs fournit des secteurs à analyser ; il ne démontre pas leur rupture ni une topologie exploitable. Les altitudes solides sont inchangées et toujours trop lisses/schématiques à l'échelle régionale, avec des formes sous-marines artificielles. Le fond marin n'est masqué par aucune eau et le contraste n'est pas ajusté individuellement. Les natives signées elevation-model.f64le sont préservées, ainsi que leur conversion Y=168+12*h pour le PNG16.

Verdict : RELIEF NON ACCEPTÉ. Aucune nouvelle comparaison/calibration ETOPO n'a été réalisée sur ce terrain inchangé. Érosion NON LANCÉE. Aucun événement de rupture ou nouvel apport océanique n'a été créé par l'observateur.

## Échecs de publication conservés et corrections

Run 35732156429 (8e008360) : le C# et les contrôles réussissent mais l'exporteur demande un aperçu gris8 non pris en charge par le writer. Un test synthétique reproduit le refus. Le correctif 80ae4ce4 répète les canaux en RGB8 pour les aperçus ; les vrais PNG gris16 et les calculs C# ne changent pas.

Run 35732661497 (80ae4ce4) : le C#, les PNG et le refus d'écrasement sont exécutés. L'artefact 10696307071 contient le message exact System.IO.IOException: Never overwrite prior evidence. et le reçu preserved=true. Le wrapper pwsh de GitHub propage néanmoins le code natif du refus attendu. d880ff07 vérifie désormais aussi ce message exact, garde le code natif dans le reçu et termine explicitement avec succès APRÈS les assertions. Aucune option continue-on-error ni suppression d'assertion. La nouvelle campagne réussit les six refus avec préservation des hashes.

Référence du wrapper : https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#exit-codes-and-error-action-preference .

## Suite et limites

Le raccordement suivant devra utiliser l'histoire des déformations ET la résistance des matériaux pour résoudre une rupture et une topologie 2D avant d'émettre les événements RiftSpreading. Ne pas convertir automatiquement les points rouges en dorsales, ne pas importer des forces depuis des épaisseurs sans loi matérielle, et ne pas supprimer la croûte résiduelle. La méthode de localisation, les résistances héritées, les fragments conjugués et les frontières d'événements restent à qualifier.

Qualification complète du mod avec les DLL Vintage Story, jeu, MCP Visual Studio et second agent : NOT_RUN. Aucun appel API Vintage Story n'est ajouté. Cet incrément demeure un outil de mesure causal dans le Core, pas un terrain réaliste final ni une prétendue fin des lots L03–L05.
