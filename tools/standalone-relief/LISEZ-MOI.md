# Générateur de relief ISRWorldGen — outil autonome expérimental

Cette archive contient le moteur C# de génération de relief brut et son programme de commande. Ce n'est PAS un mod prêt à installer dans une sauvegarde Vintage Story. Le moteur ne lit ni ne modifie vos parties. Il ne lance aucune érosion.

## Exécution de l'archive compilée

Prérequis : environnement d'exécution .NET 10. Depuis le dossier extrait :

```powershell
dotnet .\bin\WorldGen.IntegratedRelief.dll generate .\cartes-seed73 73 512 96 evolving 1000000 1000000
```

Les arguments sont, dans l'ordre : un **nouveau** dossier de sortie, la seed entière, la résolution réellement calculée (32/64/128/256/512), la durée modèle (0 à 100 Myr), le mouvement (`stationary` ou `evolving`), la largeur et la longueur du monde en blocs. La mécanique est résolue au maximum en 128 × 128 ; une sortie 512 × 512 n'est pas une mécanique 512 × 512. Les dimensions admises sont 8192 à 1024000 blocs, avec rapport d'aspect au plus 4. Un même aspect utilise l'atlas entier redimensionné, jamais un recadrage.

Pour vérifier rapidement le programme, sans génération de monde :

```powershell
dotnet .\bin\WorldGen.IntegratedRelief.dll --self-test
```

Depuis le dépôt source et la branche `codex/integrated-relief-20260924`, avec le SDK de `global.json` :

```powershell
dotnet run --project .\testsrc\WorldGen.IntegratedRelief\WorldGen.IntegratedRelief.csproj -c Release -- generate .\artifacts\monde73 73 512 96 evolving 262144 262144
```

## Sorties

Ouvrir `Galerie.html` dans le dossier calculé. `png/height-16bit.png` et `png/initial-height-16bit.png` sont de vraies hauteurs solides, au-dessus ET au-dessous du niveau marin, avec la même conversion : `Y = code * 383 / 65535`. Les aperçus 8 bits ne sont pas les fichiers numériques. `elevation-model.f64le` conserve les altitudes natives signées en kilomètres modèle ; `Y = 168 + 12 * altitudeNative`. Aucun contraste individuel, ombrage, masque d'eau, écrêtage ni érosion ne modifie ces valeurs.

Les fichiers float64 sont en little endian, ordre ligne Z puis X. Le manifeste contient leurs unités, dimensions et SHA-256. Les journaux de matière, d'équilibre mécanique et de mouvements imposés sont conservés. Un dossier `.partial-*` après interruption n'est PAS une campagne terminée ; conserver son diagnostic et utiliser une nouvelle destination pour un autre essai. Une destination préexistante est refusée, jamais effacée.

## Provenance et portée

`package.json` identifie le commit de compilation et les empreintes des binaires. Hors CI, le programme peut marquer un calcul `LOCAL_UNCOMMITTED` : conserver ce manifeste avec `package.json` plutôt que lui inventer un commit. Les deux systèmes d'exploitation testent le même paquet géré produit sous Linux, pas deux reconstructions présentées comme un unique binaire.

Le relief provient du transport des matériaux, de leur résistance viscoplastique, de la pression des colonnes et du refroidissement océanique. Les changements de direction du forçage basal sont un scénario déclaré, pas une simulation du manteau terrestre. L'âge initial des océans reste un a priori uniforme, la rupture topologique mondiale et la flexure des subductions ne sont pas intégrées. Les tests numériques ne valent pas acceptation géographique. La qualification native Vintage Story reste séparée.
