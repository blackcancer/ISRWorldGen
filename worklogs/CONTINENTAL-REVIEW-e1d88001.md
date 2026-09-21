# Revue des assemblages continentaux — e1d88001

Code exécuté : e1d880010eecf898f6f25222b8483733c3a541b9. Base comparative : c74ca9019eb4eeb412adbf4a041c38d2f2b4058e.
Campagne : https://github.com/blackcancer/ISRWorldGen/actions/runs/35623141113
Décision : conserver l'initialisation alternative explicite et ses preuves ; NE PAS accepter le relief complet ni autoriser l'érosion.

## Exécution réelle
Les six jobs Windows/Linux × seeds (-437287116, 73, 20260906) ont compilé le vrai Core et exécuté chacun 63 contrôles (47 historiques et 16 nouveaux), puis une histoire 512² complète de durée modèle 36. Le septième job de comparaison interplateforme est réussi. Pas de remplacement par une simulation Python. Les trois altitudes finales ont un écart maximal Windows/Linux de 0 dans cette campagne ; les pixels PNG16 avant/après sont identiques. Cela ne démontre pas une universalité entre tous les runtimes et matériels.

La branche de vérification a été intégrée par fast-forward non forcé sur main après lecture du HEAD c74ca901 et vérification de l'ascendance. Le champ par défaut de `MaterialBoundHistory.Generate` n'est pas remplacé. Le candidat est demandé par `GenerateWithAssemblage`. Aucun registre, monde personnel, profil de lancement, paramètre natif ou bibliothèque NuGet n'est modifié. Pas de force-push ni de reconstruction d'historique.

Artefacts Linux téléchargés : 10650269104 (seed -437287116), 10650645207 (73), 10650254403 (20260906). Rapport interplateforme : 10650865155. Base Linux : 10647703785, run 35618268142. Les SHA-256 d'archives et de tous les champs float64 ont été vérifiés. Les deux heightmaps numériques principales de chaque seed ont été décodées avec Pillow, indépendamment de l'encodeur du dépôt ; tous les pixels correspondent à round(Y*65535/383). Les champs anciens `initial-height` correspondent aux nouveaux `legacy-initial-height` à moins de 1e-8 bloc.

## Ce que l'incrément réalise
L'ancien quota égal et le placement systématique au plus loin ne commandent plus le candidat. Les assemblages initiaux sont séparés des plaques mécaniques. Ils combinent des terrains voisins, des budgets de surface inégaux, des orientations de structure et des épaisseurs initiales distinctes. Leur nombre est ici 6, 5 et 5 pour les seeds -437287116, 73 et 20260906. Les provinces adjacentes peuvent former une seule terre émergée ; province, continent visible et plaque mécanique ne sont pas synonymes.

Le volume continental initial est strictement apparié à la référence de chaque seed. Les épaisseurs sont ajustées UNE FOIS dans l'état initial par un multiplicateur borné et documenté. Il ne s'agit ni d'une retouche de heightmap, ni d'un contraste automatique, ni d'un déplacement du niveau marin. Le volume océanique initial n'est pas apparié : cette comparaison ne prétend pas conserver toutes les conditions initiales. La densité et le transport sont inchangés. Le poids total d'une plaque n'est pas un paramètre aléatoire ajouté au modèle.

Le graphe d'assemblage et son orientation restent des hypothèses initiales procédurales. Ce n'est pas une simulation mécanique de la formation des premiers continents. Le terme « accrétion » décrit ici la règle d'assemblage du graphe, pas un nouvel équilibre de forces démontré. Les observations géologiques motivent une initialisation hétérogène, elles ne calibrent pas notre distribution de quotas.

## Mesures des terres visibles (pas une définition géologique du continent)
Connexité à 4 voisins avec raccord périodique. Seuil de compte rendu : composantes >=0,5% du monde. La compacité sur raster dépend du pas de mesure. Les trois mondes couvrent chacun 1 000 000 × 1 000 000 blocs avec un pas de 1953,125 blocs ; les cartes plus petites utilisent le même atlas ENTIER, jamais un recadrage.

| Seed | Anciennes terres initiales (% du monde) | Nouvelles terres initiales (% du monde) | Nouvelles terres finales (% du monde) |
|---|---|---|---|
| -437287116 | 8,299 ; 8,155 ; 8,012 ; 7,954 | 12,627 ; 11,064 | 8,864 ; 6,175 ; 2,197 |
| 20260906 | 8,672 ; 8,500 ; 8,463 ; 8,144 | 23,495 ; 6,904 ; 1,864 | 10,865 ; 4,387 ; 3,186 ; 0,782 |
| 73 | 8,396 ; 8,265 ; 8,164 ; 8,123 | 21,468 ; 1,903 | 14,452 ; 5,378 ; 1,498 |

Ces mesures montrent la disparition de l'obligation de quatre terres presque égales, PAS une amélioration universelle de diversité. En particulier les deux terres initiales de -437287116 sont encore proches en surface (rapport 1,14). Leur apparition dépend de l'épaisseur et de la compensation locale, pas du seul quota matériel. Les anciennes terres finales de cette seed avaient même une dispersion de surfaces plus grande ; ne pas présenter la variance comme un score de réalisme à maximiser.

## Verdict visuel et limites
Les trois nouvelles heightmaps et leurs états initiaux ont été examinés en entier, avec la même conversion Y0..383 et sans masque marin. Les ensembles de 73 et 20260906 montrent des échelles plus différenciées. Sur -437287116, le regroupement laisse encore deux grands lobes initiaux proches. Les contours demeurent trop dépendants de la croissance anisotrope du graphe, plusieurs transitions sous-marines restent anguleuses, et le relief régional est trop lisse. Les maxima finaux sont respectivement Y205,229 ; Y216,958 ; Y217,332 pour -437287116, 73, 20260906. La réussite du budget ne justifie pas leur acceptation.

Acceptation géographique : NON ACCEPTE. Érosion : NON LANCEE. Forces/torques, résistance de rupture héritée, colonnes thermiques du manteau, changements d'appartenance mécanique après accrétion et flexure des zones de subduction ne sont pas résolus par cet incrément. La comparaison statistique avec ETOPO n'a pas été refaite sur ce candidat ; ne pas prétendre à une calibration terrestre. Revue d'un second agent, compilation complète avec les DLL Vintage Story et recette native/MCP : NOT_RUN.

## Suite précise
L'état initial peut désormais exprimer une hétérogénéité de matériaux indépendante des plaques. La prochaine évolution doit exploiter cette hétérogénéité dans une résistance/déformation et une histoire de fragmentation, au lieu de poursuivre une simple retouche des contours ou d'augmenter arbitrairement les sommets. Conserver les trois seeds, les mêmes emprises, le budget et les heightmaps brutes ; compléter l'ensemble de seeds avant toute qualification statistique.
