# Analyse des grandes emprises — L03/L04/L05

La cartographie réaliste reste le critère produit. Un test numérique réussi ne
valide pas les montagnes, le tracé des bassins ou l'alimentation des fleuves.
Cette campagne élargit les données observées, pas seulement la taille des PNG.

## Domaines calculés

- `wide-analysis` : 262 144 × 262 144 blocs, 512 × 512 mesures réelles, pas de
  512 blocs, 128 sites. Profil de diagnostic dérivé explicitement de `balanced`,
  avec quota et budget déclarés ; aucun profil de partie existante n'est modifié.
- `vast-expeditions` : 1 024 000 × 1 024 000 blocs, 512 × 512 mesures réelles,
  pas de 2 000 blocs, 128 sites, selon le catalogue actuel.
- Seeds fixées avant calcul : -437287116, 73 et 20260906, sans élimination des
  résultats visuellement peu convaincants. Une répétition Windows du premier cas
  complète les six calculs Linux ; ne pas généraliser cette comparaison à tout.

Ce ne sont pas des zooms d'un même monde ni des essais à densité d'atlas égale.
La résolution continentale de `vast-expeditions` ne résout pas les crêtes étroites
ou les petits ruisseaux. Les manifestes donnent le pas réel et la largeur minimale
des crêtes, afin que la perte de détail reste visible.

## Contrôles

Les calculs utilisent directement les classes Core reprises dans les lots L03,
L04 et L05 : modèle continental continu scellé, soulèvements tectoniques,
transport d'humidité métrique conservatif, drainage géométrique et couplage
préparatoire d'incision. Le modèle complet de sédimentation et de lacs L06 reste
à développer/qualifier. Aucun terrain de joueur n'est généré ou modifié ici.

La continentalité est exprimée en distance à la côte divisée par 16 384 blocs,
non en nombre fixe de pixels. Les paramètres de pluie ne sont pas augmentés
pour rendre l'intérieur d'une grande carte artificiellement humide.

Chaque export inclut la carte entière, les régions d'atlas, les plaques, le
réseau potentiel sec/humide, les séparations de bassins et les précipitations.
Une heightmap 16 bits conserve une quantification déclarée ; les JSON conservent
les valeurs calculées. Les trois plus grands bassins sont montrés EN ENTIER,
par découpe de leurs boîtes englobantes dans le même calcul global : pas de
nouvelle source d'eau ni d'océan ajouté à une bordure de découpe.

Le seuil graphique des axes affichés est fixé à 50 000 000 blocs² d'aire drainée
pour ces grandes cartes. Cela ne modifie pas le réseau ou ses débits et ne
représente pas une largeur de rivière. Les valeurs de pluie et d'altitude
utilisent des palettes fixes, sans renormalisation par carte.

## Exécution séparée

Le projet `testsrc/WorldGen.LargeGeography.Tests` est volontairement séparé de
la suite de développement habituelle. Il ne supprime ni ne masque aucun test
existant. Le workflow `Large geographic domains` conserve les sorties lourdes
sept jours dans ses artefacts.

Pour un lancement explicite depuis un terminal à la racine du dépôt :

```powershell
$env:ISR_LARGE_PROFILE = 'wide-analysis'
$env:ISR_LARGE_SEED = '-437287116'
$env:ISR_SPATIAL_DIAGNOSTICS_ROOT = Join-Path $PWD ('.local/large-' + [Guid]::NewGuid().ToString('N'))
dotnet test testsrc/WorldGen.LargeGeography.Tests/WorldGen.LargeGeography.Tests.csproj -c Release --filter FullyQualifiedName~LargeExtentDiagnosticTests
python tools/review_large_geography.py --self-test --root $env:ISR_SPATIAL_DIAGNOSTICS_ROOT
```

Un dossier de revue existant est refusé ; les sources ne sont jamais remplacées.
Le manifeste final est écrit après les images. Une sortie interrompue ne vaut pas
un succès : utiliser un nouveau répertoire. Pas d'accès au MCP, de lancement du
jeu, de reset du registre ou de publication du mod. Revue géographique indépendante,
compilation de la solution native et validation en jeu : NOT_RUN pour cette campagne.
