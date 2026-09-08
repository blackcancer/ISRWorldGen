# Vérification des points d’intégration arbres — 8 septembre 2026

## Résultats de consultation publique

| Référence | Constat limité à la documentation consultée |
|---|---|
| API-20-01 | `ITreeGenerator`, namespace `Vintagestory.API.Server`, expose `GrowTree(IBlockAccessor, BlockPos, TreeGenParams, IRandom)`. |
| API-20-02 | `ICoreServerAPI` expose l’enregistrement par `RegisterTreeGenerator(AssetLocation, ITreeGenerator)` et une surcharge à délégué. |
| API-20-03 | `TreeGenParams` expose notamment `size`, `skipForestFloor`, `hemisphere`, les chances de mousse/lianes/autres blocs et `treesInChunkGenerated`. Aucun champ de pente ou d’obstacle n’y est documenté. |
| API-20-04 | Le wiki officiel décrit les assets `worldgen/treegen` et l’outil WorldEdit TreeGen. Cette page se dit vérifiée pour 1.19.8 ; elle ne garantit pas le comportement de la version installée. |
| API-20-05 | Le fichier public `Server/ITreeGenerator.cs` de vsapi expose le contrat et les paramètres correspondants. La branche master reste une référence mobile, pas un verrouillage de DLL locale. |

L20-A doit enregistrer la version du jeu, le runtime, les hashes d’assemblages et la révision des sources correspondantes. Aucune compilation ni session Vintage Story n’a été effectuée pour ce dossier. Une documentation publique actuelle ne prouve pas la compatibilité du poste local.

## Débogage existant et besoin complémentaire

Le wiki décrit `/we tool TreeGen`, puis `/we tv walnut`, un placement avec la baguette et `/we undo`. Vérifier ces commandes dans l’environnement de test. L’existence de cet outil ne prouve pas qu’il permet déjà de figer notre seed/contexte, d’exposer nos collisions ou de rejouer notre manifeste.

Ne pas affirmer qu’une commande vanilla `/gentree` existe ou n’existe pas sans contrôle ciblé. Le plan demande un diagnostic ISRWorldGen seulement là où les fonctions réellement disponibles ne suffisent pas. Aucun nom de nouvelle commande n’est présenté comme implémenté.

## Décisions de conception, non capacités natives

Berge/lisière/versant, gestion des obstacles, règles de réservation et manifeste de reproduction sont nos besoins internes. Il faut choisir leur raccordement après audit, pas ajouter des membres fictifs à l’API. L’interface d’un générateur ne garantit à elle seule ni sûreté multithread, ni rollback, ni atomicité multi-chunks, ni conservation des BlockEntities.

Aucune bibliothèque supplémentaire n’est nécessaire pour lire ou adopter ce plan. Pour le futur code, réutiliser les assemblages de la cible. 0Harmony et les autres dépendances facultatives ne sont activées que sur justification précise et décision d’architecture.

## Sources publiques consultées

API-20-01 : https://apidocs.vintagestory.at/api/Vintagestory.API.Server.ITreeGenerator.html

API-20-02 : https://apidocs.vintagestory.at/api/Vintagestory.API.Server.ICoreServerAPI.html

API-20-03 : https://apidocs.vintagestory.at/api/Vintagestory.API.Server.TreeGenParams.html

API-20-04 : https://wiki.vintagestory.at/Modding:Trees

API-20-05 : https://github.com/anegostudios/vsapi/blob/master/Server/ITreeGenerator.cs

## Sources projet consultées

Dépôt `blackcancer/ISRWorldGen`, snapshot de référence `348e971106e8c34206d75b612b231eb8b2d8bc68` : `docs/01-PLAN.md`, `tasks/L17/README.md`, `tasks/L17/L17-C.md`, `specs/S17.md`, `tasks/L13/README.md`, `registry/gates.json` ; structure des entrées de `registry/tasks.json`, `registry/requirements.json` et `registry/tests.json`.

La revue ne prétend pas avoir audité tout le code ni les preuves de la branche. L’outil de préparation doit encore comparer le dossier avec le dépôt local réel.
