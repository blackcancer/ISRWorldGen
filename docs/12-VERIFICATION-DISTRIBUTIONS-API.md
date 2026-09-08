# Vérification ciblée : distributions natives
**Revue de sources publiques — 8 septembre 2026. La cible locale n’a pas été exécutée ici.**

Le dépôt public VSEssentials consulté porte le commit 06673b67318e002332b6aa8b3d0308f11d2aba99 (message 1.22.7). Ce numéro n’est pas imposé au développeur : les DLL installées, leur runtime et leurs sources correspondantes restent la cible à verrouiller. Les chemins ci-dessous sont des références techniques, pas une garantie d’accès public à tous les membres.

## DIST-01 — GenRockStrataNew
Le générateur natif des strates est un consommateur à auditer, également appelé par la prospection. Les cartes de strates seules ne garantissent pas une géologie 3D compatible.

Source primaire : https://github.com/anegostudios/vsessentialsmod/blob/06673b67318e002332b6aa8b3d0308f11d2aba99/Systems/WorldGen/Standard/ChunkGen/2.GenRockStrata/GenRockStrata.cs

## DIST-02 — IMapRegion / IMapChunk
Cartes de climat, forêt, arbustes, roches et minerais exposées ; leur existence n’en fait pas un encodage libre. IMapChunk distingue TopRockIdMap, RainHeightMap et SnowAccum.

Source primaire : https://apidocs.vintagestory.at/api/Vintagestory.API.Common.IMapRegion.html

## DIST-03 — BESoilNutrition
L’état agricole contient nutriments, réserve initiale et humidité ; sa création dépend de la variante de fertilité du sol. Ne pas confondre avec Block.Fertility.

Source primaire : https://github.com/anegostudios/vssurvivalmod/blob/849fa8cad9e392368566efc7474e73db6404a145/BlockEntity/BESoilNutrition.cs

## DIST-04 — GenBlockLayers et ClimateCondition
GenBlockLayers intervient dans les sols et herbes ; les consommateurs de climat/fertilité et les mutations de cartes doivent être inventoriés.

Source primaire : https://github.com/anegostudios/vsessentialsmod/blob/06673b67318e002332b6aa8b3d0308f11d2aba99/Systems/WorldGen/Standard/ChunkGen/4.GenBlockLayers/GenBlockLayers.cs

## DIST-05 — GenDeposits
Charge les définitions worldgen/deposits, sous-gisements et cartes de potentiel ; différentes conditions et facteurs de quantité sont appliqués.

Source primaire : https://github.com/anegostudios/vsessentialsmod/blob/06673b67318e002332b6aa8b3d0308f11d2aba99/Systems/WorldGen/Standard/ChunkGen/5.GenDeposits/GenDeposits.cs

## DIST-06 — ProPickWorkSpace
Crée des instances de GenRockStrataNew et GenDeposits ; il faut raccorder cette reconstruction au modèle ISRWorldGen si leurs sorties diffèrent.

Source primaire : https://github.com/anegostudios/vssurvivalmod/blob/849fa8cad9e392368566efc7474e73db6404a145/Systems/Prospecting/ProPickWorkSpace.cs

## DIST-07 — GenVegetationAndPatches
Lit forêt, arbustes, climat et heightmap ; répartit patches avant/après arbres et expose des modificateurs de placement liés aux structures.

Source primaire : https://github.com/anegostudios/vsessentialsmod/blob/06673b67318e002332b6aa8b3d0308f11d2aba99/Systems/WorldGen/Standard/ChunkGen/8.GenVegetationAndPatches/GenVegetationAndPatches.cs

## DIST-08 — TreeSupplier et BlockPatchConfig
Le choix d’arbre recalcule une fertilité climatique ; le sol du modèle ne sera pas automatiquement consommé. Les restrictions des patches doivent être vérifiées séparément.

Source primaire : https://github.com/anegostudios/vsessentialsmod/blob/06673b67318e002332b6aa8b3d0308f11d2aba99/Systems/WorldGen/Standard/ChunkGen/8.GenVegetationAndPatches/Treegen/TreeSupplier.cs

## DIST-09 — ForestFloorSystem
Le sol forestier et les patches sous/sur arbres dépendent des arbres, du climat et du système de patches. Supprimer ce dernier sans adapter les consommateurs est risqué.

Source primaire : https://github.com/anegostudios/vsessentialsmod/blob/06673b67318e002332b6aa8b3d0308f11d2aba99/Systems/WorldGen/Standard/ChunkGen/8.GenVegetationAndPatches/Treegen/ForestFloorSystem.cs

## DIST-10 — WeatherSimulationSnowAccum
Traite neige, chargement des colonnes et horodatages. Lit snowAccum et active un drapeau global de gel ; cela ne suffit pas à prouver chaque chemin de fonte.

Source primaire : https://github.com/anegostudios/vsessentialsmod/blob/06673b67318e002332b6aa8b3d0308f11d2aba99/Systems/Weather/WeatherSimulationSnowAccum.cs

## DIST-11 — BlockWater
Les conditions natives de gel filtrent notamment eau immobile à niveau plein puis attributs/exposition/température. Toute variante de courant n’est pas présumée gélifiable.

Source primaire : https://github.com/anegostudios/vssurvivalmod/blob/849fa8cad9e392368566efc7474e73db6404a145/Block/BlockWater.cs

## DIST-12 — BlockLakeIce
Le code consulté résout water-still-7 pour la restitution d’eau. La fonte/casse adaptée doit préserver eau salée et courant si ces milieux sont supportés.

Source primaire : https://github.com/anegostudios/vssurvivalmod/blob/849fa8cad9e392368566efc7474e73db6404a145/Block/BlockLakeIce.cs

## DIST-13 — Temperature
La latitude saisonnière est calculée via le calendrier depuis Z ; un équateur oblique uniquement dans l’atlas serait insuffisant.

Source primaire : https://github.com/anegostudios/vssurvivalmod/blob/849fa8cad9e392368566efc7474e73db6404a145/Systems/Temperature.cs

## Corrections de lecture importantes
La documentation IMapRegion indique BiomeMap comme inutilisée, mais les sources consultées de certains générateurs lisent ce champ. Ne pas en déduire une inutilité générale ni l’utiliser comme API universelle : inventorier les consommateurs de la cible réelle. Même prudence pour TerrainMap et RiversMap.

Les définitions effectives des assets du jeu ne sont pas livrées dans ce dossier. Les catalogues complets, valeurs de rareté et versions de l’installation ne sont pas présentés comme audités ici : leur extraction est un livrable des nouveaux lots.

## API à vérifier à la demande
Les points de génération demeurent InitWorldGenerator, MapRegionGeneration, MapChunkGeneration et ChunkColumnGeneration(handler, pass, forWorldType). Relire les signatures sur la cible, l’ordre effectif des handlers et leur thread. L’inscription de notre handler ne désactive pas les passes natives.

Documents API primaires complémentaires :
- https://apidocs.vintagestory.at/api/Vintagestory.API.Server.IServerEventAPI.html
- https://apidocs.vintagestory.at/api/Vintagestory.API.Common.IMapChunk.html
- https://apidocs.vintagestory.at/api/Vintagestory.API.Common.ClimateCondition.html

Les types C08 à C12 sont des propositions internes C# d’ISRWorldGen, pas des noms de types natifs. Les appels publics sont préférés. Une dépendance VSEssentials/VSSurvival ou 0Harmony n’est activée qu’après preuve du besoin et décision consignée ; aucune nouvelle bibliothèque n’est automatiquement requise par le plan.
