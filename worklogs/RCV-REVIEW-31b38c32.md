# Revue du lecteur de relief continu — 31b38c32

## Décision et périmètre

Exporteur exécuté : `31b38c327cc4838022ec319b704e25e0ae09cb2f`, intégré sur main par fast-forward non forcé depuis `d5ea4a8db8ea0292158a1957604a0de967b5e947`, après vérification de l'ascendance et de la campagne. Six ajouts seulement : exporteur, lecteur HTML, tests numériques, tests navigateur, workflow et compte rendu d'implémentation. Aucun fichier C#, paramètre géologique, registre, sauvegarde ou DLL n'est modifié. La branche de mécanique expérimentale n'est pas fusionnée.

Les nouvelles vues sont une RÉÉDITION des altitudes existantes, pas une nouvelle génération de mondes. La réussite concerne les contrôles d'export et d'affichage exécutés. Elle ne constitue ni une conformité exhaustive à RCV-1.0, ni une acceptation géographique.

## Tests effectivement exécutés

Run https://github.com/blackcancer/ISRWorldGen/actions/runs/35658564473 : les deux jobs numériques Windows/Linux et le job navigateur sont tous SUCCESS. Chaque job numérique exécute 23 tests ; ils couvrent notamment le décodage Pillow indépendant, l'arrondi et la plage fixes, la rampe traversant le seuil marin, l'indépendance aux étiquettes, les coordonnées/périodicités, les sources corrompues et les sept frontières d'interruption d'export. Les tests locaux ont également réussi.

Le vrai Chromium 141.0.7390.37 a exécuté neuf assertions sur une fixture synthétique rectangulaire : calques désactivés au chargement, résolution source annoncée, déplacement de l'isoligne sur un canvas séparé, conservation du tableau float64 et de l'image de base, couleur indépendante du repère marin, lecture exacte sous le curseur, six transects, téléchargement octet-à-octet et absence d'erreur JavaScript. Cette fixture ne représente pas un monde réel et ne prouve pas un essai navigateur exhaustif de toutes les grandes galeries.

Artefacts récupérés et SHA-256 vérifiés : 10666293452 (Windows), 10665328508 (Linux), 10665038803 (navigateur). Aucun test C# ou natif n'est requis pour attribuer ces résultats à l'exporteur ; aucun nouveau test du générateur C# n'est revendiqué.

## Réexport des données C# réelles

Source : archive `ISRWorldGen_Revue_Nonlineaire_48b8bb98.zip`, SHA-256 `90151653ecd692a30b8621578d4792ac867e5cc1306d28d31bc8cb01f36bfcc1`. Générateur source : `48b8bb985c9d0a57f97d17427d1d6e9c27c4f9c7`. Méthodes : homogeneous, heterogeneous, powerlaw ; seeds -437287116, 20260906, 73 ; états initial/final.

18 champs de hauteur ont été vérifiés et réexportés, soit 4 718 592 pixels PNG16 et 108 profils systématiques. Les float64, les SHA-256 de source, les extrema et les encodages sont vérifiés ; les sources n'ont pas été modifiées. Les reçus COMPLETE et leurs hashes ont été relus. Le lecteur principal contient les six vues powerlaw (trois seeds, avant/après) ; la comparaison complète conserve les 18 champs.

Chaque monde couvre 1 000 000 × 1 000 000 blocs ; grille altimétrique/matérielle 512², pas 1 953,125 blocs ; mécanique 128². Aucune interpolation d'image ne devient une mesure supplémentaire. Le lecteur commence sans calques, utilise Y0-383-v1 partout et propose la valeur du float64 au centre de cellule, les données brutes, le PNG16, une palette séquentielle sans seuil marin, l'isoligne optionnelle et les six coupes avec CSV. Les traits des coupes relient les mesures ; l'interpolation transversale et l'exagération graphique sont déclarées.

Les fichiers d'altitude native originaux ne figurent pas dans ces anciennes sources. L'inversion de la conversion Y=168+12*h est donc fournie comme RECONSTRUCTION affine en km modèle, jamais comme récupération bit-à-bit du tampon natif original. Le repère marin réglable dans le lecteur ne modifie pas ce datum source.

## Observation sur le terrain, sans réglage cosmétique

Les trois vues powerlaw finales entières ont été examinées, ainsi que la coupe systématique X50 de la seed 20260906 comparant état initial et final. L'impression de grands domaines à niveaux différents ne disparaît pas avec les calques masqués : le relief calculé contient réellement de vastes zones de faible variation, avec des raccords concentrés aux marges et des bandes sous-marines encore schématiques. Dans la coupe X50, le large fond déjà peu variable dans l'état initial reste peu accidenté après tectonique. Aucun aplat d'eau n'est utilisé pour produire ce constat.

La quantification d'aperçu contribue aussi à la perte de lecture : l'affichage gris n'utilise ici que 75, 82 et 78 codes distincts pour les trois champs finaux, alors que les float64 sont beaucoup plus nombreux. Un pas gris de Y0-383 correspond à environ 1,502 bloc ; le curseur et les CSV permettent de lire les variations plus petites sans étirer le contraste.

Diagnostic fixé avant mesure : fraction des centres dont le voisinage 9×9 présente un dénivelé total <=1 bloc. Le rayon est quatre cellules, soit 7 812,5 blocs ; les voisinages se recouvrent et incluent tout le monde, pas seulement l'océan. Résultats : 21,2296 % (-437287116), 26,6167 % (20260906), 37,3844 % (73). Ce n'est ni une mesure de surfaces planes disjointes, ni un score de réalisme, ni une raison suffisante pour rejeter toute plaine. Ce diagnostic confirme la faiblesse locale du contraste observé dans ces champs, à leur résolution.

Verdict géographique antérieur maintenu : REJECTED. La palette ne doit pas fabriquer de dorsales ou de relief dans ces secteurs. La correction suivante concerne l'état matériel initial et l'histoire océanique qui déterminent les altitudes ; elle exige des expériences causales séparées, pas une modification des pixels. Érosion NON LANCÉE.

## Limites restantes

Aucun raffinement géographique, champ natif original supplémentaire, choix automatique de transects géologiques ciblés, ombrage ou carte de différence intégrée n'est ajouté. Les profils superposés et les données exactes permettent une comparaison, mais ne remplacent pas ces parties restantes du protocole. Les exports avec NoData sont refusés, pas publiés partiellement comme complets.

La qualification du redimensionnement de l'atlas dans le générateur n'est pas réexécutée par ce lecteur. La comparaison ETOPO n'a pas été refaite ; aucune calibration terrestre nouvelle n'est affirmée. Revue d'un second agent, génération/qualification native Vintage Story et MCP Visual Studio : NOT_RUN. Le nouveau compte rendu ne déclare aucun lot DONE.
