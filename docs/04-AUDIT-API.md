# Audit documentaire API et qualification locale
**Vérification des sources publiques : 6 septembre 2026. Compilation locale du mod : non effectuée dans cette livraison.**

## Référence observée
La page d’accueil de la documentation indique Vintage Story **1.22.7**. La branche master de `vsessentialsmod` consultée pointe vers le commit `06673b67318e002332b6aa8b3d0308f11d2aba99`, message « Updated to Version 1.22.7 », daté du 17 août 2026. Cela identifie la source observée ; ce n’est pas une lecture de l’installation de l’utilisateur. Sources API-00, API-04.

Le projet `VintagestoryAPI.csproj` consulté déclare son framework via `$(FrameworkVersion)` plutôt qu’une valeur directement exploitable. Il faut lire le template local et le runtime du jeu : Visual Studio Community 2026 n’impose pas la version de .NET du mod. Source API-08.

## Symboles confirmés et précautions
| Élément | Observation | Conséquence pour le développement |
|---|---|---|
| IServerEventAPI | InitWorldGenerator, MapRegionGeneration, MapChunkGeneration, ChunkColumnGeneration avec argument de type de monde | Utiliser les signatures compilées localement ; trois arguments pour le hook colonne |
| EnumWorldGenPass | Terrain, TerrainFeatures, Vegetation, NeighbourSunLightFlood, PreDone, Done | Ne pas recopier TerrainNoise depuis un ancien commentaire XML |
| IWorldGenHandler | Listes de handlers et WipeAllHandlers | La possibilité d’effacer tout ne justifie pas de le faire ; retrait ciblé prouvé |
| GenTerra | S’inscrit sur Terrain pour standard ; initialise aussi le niveau marin en AssetsFinalize | La stratégie de remplacement doit traiter cycle de vie et niveau marin, pas uniquement un événement tardif |
| IMapRegion | ClimateMap, ForestMap, OceanMap, GeologicProvinceMap, TerrainMap, RiversMap ; BiomeMap signalée inutilisée | Auditer formats/consommateurs ; pas de raccourci générique « set biome » |
| IMapChunk | Plusieurs cartes de hauteur/roche et YMax, sémantiques distinctes | Comparer les cartes aux blocs à la bonne passe |
| ISaveGame | StoreData/GetData, seed native int et version de sauvegarde | Stocker le manifeste ; tester taille, sauvegarde effective et reprise, sans supposer transaction multi-clé |

Les détails et URLs sont dans [SOURCES](07-SOURCES.md), API-01 à API-08. Les propriétés modernes trouvées ne constituent pas une preuve que tous leurs formats ou usages sont déjà adaptés à notre mod.

## Relevé local obligatoire à L00-A
Version client/serveur exacte ; version du template ; TargetFramework effectif ; SDK ; architectures ; chemin des DLL ; versions/empreintes API/essentials/survival ; configuration Debug/Release ; chemins de déploiement ; fichier de symboles ; capacités et version du MCP ; existence d’un profil de données jetable. Le rapport doit éviter secrets et chemins personnels dans les versions destinées à diffusion.

Le probe de compilation n’utilise que des symboles réellement nécessaires. Les appels doivent être recoupés avec le code source correspondant aux assemblages, et les différences consignées. Une signature présente dans une doc plus récente ne suffit pas pour une installation plus ancienne.

## Matrice des passes à produire
Pour chaque handler : nom/type observé, événement, passe, ordre, données lues, données écrites, contexte de thread, dépendances et action choisie. Couvrir au minimum terrain, strata, rivières/rivulets, cavernes, lacs, sols, minerais, sources thermales, végétation, structures/story et lumière. Il faut prouver qu’un handler remplacé ne réécrit plus nos données et qu’un handler conservé obtient encore ses entrées attendues.

La voie standard avec activation explicite est prioritaire mais conditionnée au spike. Un worldtype dédié reste une alternative à qualifier entièrement. Un patch Harmony n’est acceptable qu’avec symbole ciblé, raison, gestion de version et test de désactivation ; aucune dépendance 0Harmony n’est présélectionnée.

## Ce qui reste à démontrer sur le jeu
Encodage/consommateurs de TerrainMap et RiversMap, gestion des niveaux fluides, correction climatique native, mécanisme exact de réservation des structures, accès autorisé aux voisins par passe, moment sûr de publication des données persistantes, possibilité de certifier les accès après les décorations sans retoucher des chunks joués. Ces points ont des tâches et des tests ; ils ne sont pas présentés comme résolus par simple lecture de l’API.
