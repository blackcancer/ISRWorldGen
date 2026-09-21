# Revue géographique — relief, bassins et déficit d'eau intérieur

Base du code : `deb03606d2136f2a9d7005e11017090da17a6c16`.
Source mesurée : série PNG `8f23fb57e446311d72c621033842c2bac6d6525e`,
seed `-437287116`, profil balanced, 256 x 256 centres de cellules espacés de 512 blocs.
SHA-256 de fields.json (archive Linux publiée) :
`7c2849b4fc39da8ffb77a9ff5202cdc25ef07445c4f8e8f2adcbfb3083180054`.

## Décision et périmètre

Le retour utilisateur rejette la crédibilité géographique de cette série : relief
très doux, bassins rectilignes, hydrologie essentiellement côtière. Les assertions
numériques réussies ne qualifient pas le réalisme. Ne pas présenter cette carte
comme un monde final ni contourner le constat par lissage, changement de seed,
augmentation arbitraire des débits, étirement automatique de la palette ou baisse
des seuils d'affichage. La palette et les diagnostics sont améliorés ici ; les
algorithmes de génération ne sont PAS corrigés par ce commit.

Aucun contrat partagé, registre d'état, format de sauvegarde, dépendance ou appel
Vintage Story n'est modifié. Aucun lot n'est clos ou rouvert automatiquement.

## Constats mesurés, à ne pas généraliser à toutes les seeds

- 27 844 cellules terrestres ; mer à Y=168 ; maximum échantillonné Y=222.29158394368912,
  soit +54.29158394368912 blocs ; médiane terrestre +21.30169032839197 blocs.
- Pente médiane résolue au pas 512 : environ 0.03914 degré. Ce n'est ni une pente
  à l'échelle du bloc, ni une mesure d'altitude maximale non échantillonnée.
- 26 282 cellules terrestres sur 27 844 (94.39017382559977 %) produisent zéro
  ruissellement. 26 162 ont un débit accumulé nul. Seulement 412 nœuds terrestres
  passent le filtre de l'image hydrologique existante.
- 10 574 liens sur 25 256 nœuds terrestres non relevés virtuellement et disposant
  d'une descente stricte (41.86727906240101 %) ne suivent pas la pente descendante
  maximale de la surface de routage. La comparaison utilise dénivelé/distance,
  y compris la distance diagonale, et une tolérance documentée.
- 2 588 cellules terrestres sont sous la surface virtuelle ; 1 520 liens montent
  dans le relief PHYSIQUE. Ce n'est pas une preuve d'erreur de Priority-Flood :
  il construit une surface virtuelle. Mais ses liens ne sont pas, à eux seuls,
  des lits fluviaux physiquement praticables sur le terrain non rempli.
- 37 692 terminaux marins : la fixture déclare chaque pixel marin terminal ;
  907 identités d'exutoire reçoivent des cellules terrestres. Ne pas appeler ces
  37 692 pixels autant de grands bassins versants. Ne pas corriger en fusionnant
  tous les fleuves qui débouchent dans le même océan.

## Causes établies dans le code

`SpatialDiagnosticExportTests` utilise un seul vent (1,0), une frontière fermée,
une condensation de base 0.12 par cellule, aucune source continentale d'humidité,
un sol uniforme et un potentiel ET 0.3 multiplié par 0.5.

`PrecipitationSolver` reporte l'humidité résiduelle au seul voisin sous le vent.
Sur un trajet terrestre plat sans apport, elle est multipliée par 0.88 à chaque
pas ; elle tombe à 1 % après environ 36 pas (18 445 blocs au pas de cette série),
avant les pertes orographiques. Le coefficient de base dépend du nombre de
cellules traversées, pas de leur distance physique. La température est validée à
l'entrée, mais n'intervient pas dans les opérations d'évaporation/condensation de
ce solveur. Ce sont des limites du modèle actuel, pas un simple défaut du PNG.

`WaterBudgetSolver` consomme en ET min(pluie, 0.3*0.5). La pluie <= 0.15 donne
zéro ruissellement ET zéro recharge avec ces entrées ; la carte représente bien
un déficit du calcul, pas seulement un réseau caché par la palette.

`DepressionTopologyBuilder` choisit le receveur au premier passage de la file
Priority-Flood. `DrainageCell` porte hauteur et voisinage, mais aucune distance
horizontale d'arête : cette API ne calcule pas une descente géométrique maximale.
Le résultat topologique est stable, sans être un solveur D8 de plus forte pente.
La grille à huit directions et les plateaux virtuels favorisent les orientations
techniques. Les bassins dépendent néanmoins des hauteurs : ne pas prétendre que
le relief est totalement ignoré.

`LandscapeFamilyCatalog` donne RuggedRanges une longueur macro de 34 000 blocs
et une amplitude normalisée 0.28. Le budget de cette fixture autorise +128 blocs,
mais n'en impose pas l'utilisation. Le comptage de catégories donne une seule
cellule RuggedRanges sur 64. La faiblesse des montagnes résolues est confirmée ;
attribuer quantitativement tout le problème au seul lissage serait prématuré.

## Corrections à développer, ordre de priorité

1. Requalification morphologique L03 : spectre horizontal/vertical, largeur et
   proéminence des crêtes, continuité des chaînes liées aux frontières tectoniques,
   transitions entre provinces. Ne pas utiliser la cellule Voronoï comme une
   montagne indépendante. Comparer plusieurs seeds fixées à l'avance, à échelle
   régionale puis à 16/32/64 blocs sur des fenêtres fixes. L'érosion ne doit pas
   servir d'excuse pour des montagnes initiales absentes.
2. Requalification L04 : coefficients de transport en distance modèle, influence
   explicite de la température, conditions aux limites et régimes de vent,
   apports continentaux seulement avec un bilan conservatif. Test analytique de
   la même traversée physique sous plusieurs résolutions. Garder des cas secs ;
   ne pas imposer une rivière sur chaque cellule d'un monde désertique.
3. Requalification L05 : séparer résolution des dépressions, direction physique
   de descente et routage des plateaux ; introduire une géométrie/distance
   explicite plutôt que déduire des coordonnées depuis les IDs. Représenter les
   lacs et leurs seuils, et les exutoires côtiers terrestres. Tester plan incliné,
   vallée en V, selle, crête séparatrice, plateau, lac à seuil et symétries/rotations.
4. L06/L07 : incision et profils de lit à partir de la même topologie/bilan ;
   recalcul et stabilisation AVANT publication du monde ; contrôles après
   quantification en blocs. Pas de rivières dessinées indépendamment du relief.

Toute modification des décisions publiées exige une décision d'intégration sur
les versions et invalidations concernées. Cette liste n'autorise pas un contournement
des dépendances natives, de la revue indépendante ni de L18.

## Livraison effective de ce commit

`tools/audit_spatial_quality.py` mesure les champs, vérifie voisinage, bassins,
monotonie virtuelle et absence de cycles, puis crée un nouveau dossier `review/`.
Cinq PNG et leurs aperçus x4 : heightmap topographique, pente, audit du routage,
axes potentiels secs/humides et production locale de ruissellement. La heightmap
16 bits et les quatorze exports d'origine sont conservés sans modification.

Palette fixe en blocs relatifs à la mer : vert -> jaune/ocre -> brun -> gris/blanc,
avec bathymétrie bleue séparée. Pas d'ombrage ni d'exagération verticale. Les
hauteurs +96/+128 ne sont pas artificiellement atteintes sur la carte actuelle.
Les légendes, limites de palette, hashes de pixels/source/code et mesures sont
dans quality-report.json ; geographicAcceptance reste NOT_EVALUATED. La galerie
explique explicitement cette séparation. Les images ne prétendent pas résoudre
les défauts du générateur.

```text
python tools/audit_spatial_quality.py --self-test --root CHEMIN_PARENT_DES_FIXTURES
```

Le workflow Spatial diagnostic PNGs exécute maintenant cet audit après le calcul
C# et le premier export. Il publie le dossier review avec les autres artefacts.
Un dossier review existant est refusé ; champs et manifeste sont relus inchangés.
Le marqueur de complétude quality-report.json est écrit en dernier. Une
interruption n'autorise pas à considérer un dossier partiel comme achevé.

## Validation et limites

Avant publication : self-test Python exécuté localement, incluant diagonal vs
cardinal, pente d'un plan avec pas anisotrope, palette, océan/dépression sèche,
cycle sur plateau et refus d'écrasement avec conservation des octets.
L'export a été réellement exécuté sur l'archive ci-dessus. Les PNG sont des
transformations des données, pas une génération d'image artistique.
La validation Windows/Linux du workflow reste à consulter dans le run associé.
C# local, solution complète, Vintage Story, MCP et revue indépendante : NOT_RUN.
Aucune affirmation d'achèvement des correctifs géographiques.

Références algorithmiques pour le chantier suivant (pas des validations du code) :
https://hydrology.usu.edu/taudem/taudem5/help53/D8FlowDirections.html
https://richdem.readthedocs.io/en/latest/flow_metrics.html
https://www.esri.com/arcgis-blog/products/product/imagery/hypsometric-tinting
