# Plasticité régularisée — revue c1575a02

## Code et décision

Code exécuté : c1575a0212d757ba1b3ccbc2095fd80ce5556380. Parent : c0437a8953f9e0932606134bad882addb92ca4b6, candidat de la PR5. Branche : codex/yield-strength-20260922. Main vérifiée au début : 4f31f29a1bf11d8b03276d5466947a3fa5a102d7, non modifiée.
Campagne : https://github.com/blackcancer/ISRWorldGen/actions/runs/35787182371 — terminée SUCCESS.
Décision : conserver l'expérience exécutable et ses preuves, sans fusion automatique ni remplacement du générateur. Acceptation géographique REJECTED. Érosion NON LANCÉE. Aucun lot DONE.

## Mécanisme et limites physiques

ViscoplasticSheet minimise une énergie convexe de membrane, avec quatre points de quadrature par cellule MAC. Un seul invariant combine les déformations normales et le cisaillement. Sous le seuil, tau=2*mu*r. Au-delà, tau=Y+2*a*mu*(r-Y/(2*mu)). Le ratio a=.15 est une viscosité post-seuil explicitement modélisée : la loi permet une surcontrainte, elle n'est PAS une plasticité parfaite à contrainte strictement plafonnée. Ce n'est ni une élasticité stockant puis libérant son énergie, ni une fracture en traction, ni une loi frictionnelle Drucker-Prager.

Le solveur Newton semi-lisse / gradient conjugué utilise le vrai résidu d'équilibre réassemblé, tolérance 1e-12. Aucun écrêtage de contrainte après résolution, aucun repli silencieux sur les vitesses prescrites. La dissipation et le travail extérieur sont contrôlés.

ContinentalStrainMemory distingue maintenant les sémantiques Viscous et PlasticExcess. L'excès plastique, nul sous le seuil, est transporté avec le porteur continental et ses redistributions inférieures. Dans ce candidat il affaiblit le SEUIL, pas la viscosité ductile. La source vient de la quadrature mécanique 128² et est répartie de façon constante sur ses sous-cellules matérielles 512² ; elle ne constitue pas une résolution mécanique 512².

Charges continentale 700 / océanique 350 : charges NORMALISÉES DU MODÈLE, pas Pa, pas poids de plaque, pas enveloppe intégrée de pression calibrée. Ratio post-seuil .15, résidu de résistance .35, échelle de mémoire .5, support 10000 unités sont des hypothèses expérimentales fixes. Pas de guérison, de rupture topologique, de séparation automatique des plaques ou de nouvelle naissance océanique. Le prior d'âge océanique uniforme et les anciennes règles d'échanges océaniques demeurent inchangés.

Les deux modes ductile/yield utilisent la MÊME nouvelle quadrature et les mêmes états matériels initiaux. En mode ductile, la plasticité est désactivée et sa mémoire reste exactement nulle. La nouvelle quadrature retrouve l'ancienne membrane pour une viscosité homogène ; l'égalité n'est PAS annoncée pour l'ancien opérateur à coins harmoniques hétérogènes. La comparaison ne mélange donc pas cet effet de discrétisation avec celui du seuil.

Generate, GenerateWithAssemblage et l'ancien chemin sans option Yield restent présents. L'option nulle est omise de la sérialisation historique. Le nouvel accès est explicite, ViscoplasticWorld.Generate. MaterialBoundHistory.cs n'est pas changé par cet incrément. Aucun appel Vintage Story, dépendance, framework, sauvegarde ou registre n'est modifié.

## Exécution et vérifications

134 contrôles C# distincts par plateforme : 23 nouveaux, 29 affaiblissement, 22 déformation, 60 rift/chronologie. Les deux archives de contrôles ont été téléchargées et leurs inventaires vérifiés. Douze histoires complètes : seeds -437287116,20260906,73 × ductile/yield × Windows/Linux. Durée 36 Myr modèle, atlas complet 1000000² unités, altitudes/matériaux 512², mécanique 128². Pas 1953.125 blocs pour les altitudes, 7812.5 unités pour la mécanique. Contrôle du redimensionnement intact vers 131072 et 262144 à un point homologue, pas une certification exhaustive des tailles.

La CI compare 10 champs par expérience, soit 15728640 paires numériques. Erreur maximale observée tous champs : 7.034373084024992e-12 ; hauteurs : 2.8990143619012088e-12 bloc. Tolérance absolue interplateforme 1e-8 inchangée ; codes PNG16 identiques. Ce n'est pas une garantie tous matériels/runtimes. Douze refus de réutilisation des sorties conservent leur message attendu, code non nul et empreintes.

Vérifications locales indépendantes :
- SHA-256 des six archives de mondes Linux, des deux archives de contrôles, du rapport interplateforme et des 60 champs float64 Linux.
- 3145728 pixels PNG16 initial/final décodés par Pillow ; erreur de quantification maximale 0.00292210257964598 bloc. Conversion Y=code*383/65535 identique partout. Aucun masque marin, écrêtage, ombrage ou contraste individuel.
- Altitudes natives signées préservées ; relation Y=168+12*h retrouvée exactement. Contrôle porteur/moment/concentration, production explicite et inventaires par origine. Accord travail-dissipation maximal 3.783561170704855e-13 relatif dans les sorties Linux.
- Six fichiers du nouveau calcul/lanceur correspondent octet à octet à la source archivée par la CI.
- Oracle dense indépendant : assemblage B et énergie à partir de la quadrature sur une fixture hétérogène 8², soit 128 inconnues. BFGS rencontre une perte de précision ; un raffinement dense indépendant scipy.optimize.root converge. Écart maximal aux vitesses C# : 1.9455270727775087e-13. Résidu indépendant du C# : 9.246577430070196e-14 relatif. La plus petite valeur propre de la tangente est environ 1. Ce n'est pas une simulation de monde en Python.
- Comparaison locale de 256 valeurs de la fixture Windows/Linux : maximum 4.440892098500626e-16.

Artefacts Linux ductile/yield : 10720996450 / 10720657323 (-437287116), 10720043646 / 10720996491 (20260906), 10720813344 / 10720013552 (73). Contrôles : 10720852400 Linux, 10720243497 Windows. Rapport : 10720364424. Les mondes Windows complets n'ont pas été retéléchargés localement : leur comparaison est celle de la CI exécutée.

## Effet sur les reliefs

| Seed | Écart maximal de hauteur | Écart RMS | Cellules changées de plus de 1 bloc | Voisinages quasi plats ductile / yield |
|---|---:|---:|---:|---:|
| -437287116 | 79.1283 blocs | 7.4270 blocs | 58.5056 % | 20.5093 / 28.1170 % |
| 20260906 | 69.9390 blocs | 7.2149 blocs | 50.7519 % | 26.8772 / 30.2296 % |
| 73 | 84.2293 blocs | 6.2190 blocs | 41.9174 % | 37.9704 / 40.4350 % |

Diagnostic de quasi-planéité identique aux expériences précédentes : dénivelé <=1 bloc dans le voisinage périodique 9×9. Ce ne sont pas des plaines disjointes ni un score géologique. Les maxima Y ductile/yield sont 193.5106/227.2659, 203.0069/249.0441 et 196.0829/242.8926. L'écart maximal du tableau n'est pas l'augmentation du sommet maximal.

Les six vues entières ont été examinées côte à côte, à conversion fixe. Les crêtes se concentrent davantage et deviennent plus marquées. Mais plusieurs ressemblent encore à des bandes étroites ; les masses continentales demeurent lisses, les rubans et jonctions océaniques trop géométriques, et de grands fonds restent peu structurés. Le diagnostic de quasi-planéité empire sur les trois seeds. Cela ne justifie pas de supprimer les plaines, mais interdit de confondre quelques sommets plus hauts avec un progrès géographique global.

## Retour aux références terrestres

Les trois extraits ETOPO 2022 déjà conservés dans ISRWorldGen_Reliefs_3b713044.zip ont été relus : Alpes–Pô–Ligurien, Andes–fosse chilienne, Norvège–plateau–bassin. Chaque JSON source décompressé retrouve son SHA-256 et toutes ses valeurs correspondent au float64 d'altitude stocké. Pas de nouvelle acquisition présentée comme telle.

Une moyenne surfacique des cellules de référence les ramène à 19.53125 km, le pas du modèle à .01 km/unité. Les dernières cellules partielles sont identifiées et exclues des statistiques à pas régulier. La métrique reste une approximation équirectangulaire à latitude centrale. Les images utilisent la même conversion verticale déclarée Y=168+12*(mètres/1000), sans masque marin et sans étirement individuel ; les originaux sont conservés.

Les différences d'altitude aux distances 19.53125,39.0625,78.125,156.25 km sont documentées. Au premier pas, les médianes des trois mondes yield sont environ 9.87,8.97,5.93 mètres modèle contre 102.42,96.60,61.61 mètres pour les trois RÉGIONS ETOPO. Ce n'est pas une comparaison statistique représentative de deux planètes : leur mélange plaines/montagnes/océans diffère, ETOPO décrit des surfaces déjà érodées, et ces écarts ne calibrent aucun paramètre. Les formes ont été examinées aussi visuellement. Aucun PASS géologique ne découle d'un histogramme ou de ces trois régions.

## Galerie et passation

La livraison conserve neuf états lisibles (3 initiaux communs, 6 finaux), les champs exacts et 18 transects systématiques avec CSV, à échelle Y0..383 fixe. Le survol lit le float64 d'altitude ; h modèle affiché est une conversion affine, distincte du tampon natif préservé. La source compilée est fournie.

Contrôle navigateur local : Chromium 144.0.7559.96 existant via Playwright set_content. Le navigateur géré par défaut n'était pas installé ; l'exécutable système a été utilisé sans modifier sa sécurité. Le test du survol a révélé un décalage dû aux bordures CSS et à l'arrondi des coordonnées du test ; la conversion dans le rectangle intérieur et le point de test ont été corrigés. Les six changements d'affichage, les pixels/valeurs contrôlés, le survol exact et le téléchargement PNG16 octet à octet passent sans erreur JavaScript. Les images/profils relatifs sont vérifiés sur disque, pas par cette injection about:blank. Ce n'est pas une qualification exhaustive multi-navigateurs.

Suite : distinguer les limites viscoplastiques, la localisation et la rupture topologique réellement ouverte ; raccorder cette dernière aux sources océaniques datées sans effacer la croûte résiduelle. Le socle thermique et l'histoire initiale des océans ne doivent pas rester un âge uniforme dissimulé sous une palette. Ne pas baisser arbitrairement les seuils pour fabriquer des sommets.

Revue d'un second agent, enveloppe de résistance pression/température calibrée, qualification native Vintage Story/MCP : NOT_RUN. Érosion NON LANCÉE. Branche expérimentale non fusionnée.

Références de méthode, distinctes des hypothèses du candidat : Glerum et al. 2018, https://se.copernicus.org/articles/9/267/2018/ ; documentation ASPECT ViscoPlastic ; ETOPO, https://www.ncei.noaa.gov/products/etopo-global-relief-model, DOI 10.25921/fd45-gt74.
