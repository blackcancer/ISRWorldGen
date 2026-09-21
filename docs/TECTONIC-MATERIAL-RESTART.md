# Reprise tectonique : état matériel commun, référence 1 000 000

Base : `87d7a77880bfb446a23f4b9bb37b2bcce22526a8`. Priorité utilisateur : tectonique avant relief, relief solide au-dessus ET au-dessous de la mer, contrôle par comparaison terrestre avant érosion. Écriture sur main autorisée explicitement. Aucun statut de lot n'est modifié.

## Périmètre livré

Nouveau noyau expérimental `Geology/Evolution`, appelé uniquement par `WorldGen.TectonicEvolution`. Les anciens `PlateAtlas`, `RawReliefModel` et leurs consommateurs restent inchangés. Le nouvel historique n'appelle aucune forme de massif héritée, aucun bruit de relief ni aucune incision. Les groupes continentaux initiaux sont des assemblages connexes de terranes Voronoï, séparés par une réserve de domaine océanique ; l'état initial est une hypothèse explicite, pas une Terre reconstituée.

Des domaines de pilotage Voronoï mobiles imposent une vitesse. Une méthode de volumes finis transporte l'épaisseur continentale, l'épaisseur océanique, le moment volumique d'âge et un traceur de croûte océanique initiale. La compression et l'extension changent donc des quantités matérielles AVANT l'altitude. L'ouverture après amincissement produit du basalte d'âge zéro ; les contacts convergents océan/continent ou océan/océan choisissent le côté inférieur en fonction du matériau et de l'âge. Les volumes recyclés et les moments d'âge retirés sont comptabilisés à chaque pas.

L'étalement de la croûte continentale surépaissie redistribue de la matière : ce n'est PAS une érosion ni un effacement de sommets dans une image. Une réponse de flottabilité de colonne et un terme de refroidissement océanique donnent une altitude solide commune. Les paramètres sont un modèle réduit à calibrer, pas des propriétés géophysiques validées.

**Limite essentielle : modèle cinématique évolutif, pas équilibre mécanique global des forces.** Les domaines directeurs ne sont pas les trajectoires matérielles de plaques rigides. La subduction est un recyclage volumique polarisé, pas encore une flexure résolue ni un arc magmatique. L'âge est une moyenne transportée, pas un simple bruit ou une distance à la dorsale ; la provenance par dorsale et les distributions d'âges multiples restent à ajouter. Ne pas présenter ces fonctions absentes comme livrées.

## Échelles

`TectonicScalePlan` fixe le grand axe de référence à **1 000 000 unités**. `FullAtlasScaled` adapte cet atlas entier à la taille choisie. Pour un carré, 131072, 262144 et 1000000 blocs échantillonnent le même historique complet ; pas de crop central, pas d'agrandissement d'un seul continent. Les rapports contrôlent chaque position relative et les composantes terrestres >=1 % de la surface. La taille native du monde n'est pas réécrite.

Le rapport d'aspect est conservé par une transformation isotrope. Changer seulement le rapport largeur/longueur impose un nouvel atlas complet adapté : il est interdit d'étirer ou découper discrètement le précédent. Domaine expérimental : axes 8192..1024000 blocs, aspect <=4. La résolution est une entrée distincte (32..512 par axe), et n'est JAMAIS annoncée comme du détail au bloc. Un atlas 512² sur un million de blocs a un pas de 1953.125 blocs ; sa version 131072² a un pas de 256 blocs. Aucune résolution n'est inventée par l'export.

## Fermeture et unités expérimentales

L'expérience utilise une fermeture **périodique de l'atlas préparatoire entier** pour suivre les volumes sans sorties arbitraires. C'est une hypothèse de laboratoire explicite, pas une modification du monde fini natif ni un tuilage de chunks. Cette fermeture n'est pas qualifiée pour la V1 ; la décision entre préparation sphérique, domaine tampon ou flux de frontière déclarés reste à prendre avant l'intégration native. Les identifiants de plaque exportés sont ceux du pilotage, pas une fausse provenance matérielle.

1 unité horizontale de référence = 0.01 km modèle ; temps en Myr modèle ; épaisseurs en km équivalents ; volumes en km³ modèle. Conversion verticale d'essai : **Y = 168 + 12 × altitude_km_modèle**, constante et sans clamp, puis PNG16 **Y = code × 383 / 65535**. Il n'y a ni niveau d'eau substitué au fond marin, ni contraste automatique. Le datum, les densités effectives et les coefficients thermiques sont explicitement provisoires.

## Tests et publication

Exécutable C# sans nouvelles dépendances, référencé au vrai Core. Cas de conservation, positivité, CFL, translation, cisaillement pur, inversion convergence/ouverture, polarité, contacts opposés au bord périodique, repos, vieillissement, immutabilité, déterminisme, historique régénéré à taille réduite et lectures concurrentes. La campagne calcule les trois seeds fixes -437287116, 73, 20260906 et produit états initial/final, bilans, dix champs numériques et PNG16.

```powershell
dotnet run --project testsrc/WorldGen.TectonicEvolution/WorldGen.TectonicEvolution.csproj -c Release -- .local/tectonic-history 512
python tools/export_tectonic_history.py --root .local/tectonic-history
```

L'exécutable refuse un dossier de preuves existant. L'export refuse des PNG déjà présents. Une interruption conserve le dossier incomplet pour diagnostic ; une relance utilise un NOUVEAU dossier, ne supprime rien automatiquement. La CI s'exécute dans un runner neuf, sans secrets ni processus du jeu, et conserve la source exacte. Les snapshots de publication sont ajoutés par commit enfant et mise à jour fast-forward uniquement ; si main avance, nouvelle lecture et réconciliation, jamais force-push.

La compilation et les tests ne valent pas acceptation morphologique. La revue métier et la revue indépendante restent distinctes. Les résultats réellement exécutés sont à consulter dans le rapport CI et la passation ; ne rien déduire de ce document préparatoire. L'érosion, les eaux et la validation native restent suspendues pour ce candidat.

## Références et exclusions

- NOAA, création de croûte aux divergences et effets de vitesse : https://oceanexplorer.noaa.gov/ocean-fact/mid-ocean-ridge/
- NOAA NCEI, données de comparaison terrestre et sous-marine : https://www.ncei.noaa.gov/products/etopo-global-relief-model ; ETOPO2022 DOI 10.25921/fd45-gt74.
- API Vintage Story : https://apidocs.vintagestory.at/api/Vintagestory.API.Common.ModSystem.html. Aucun nouvel appel natif ni changement de cycle de vie. La compatibilité native ne peut pas être affirmée sur un test du Core.

Défauts encore possibles : diffusion numérique du transport de premier ordre, dépendance de la topologie initiale à la discrétisation, absorption océanique simplifiée, absence de flexure/fosse/arc, faible détail des marges, couplage forces/vitesses absent. Ces limites doivent rester visibles sur les images et la feuille de validation ; aucune érosion ne doit les masquer.
