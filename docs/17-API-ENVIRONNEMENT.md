# API environnementale — points vérifiés et preuves locales nécessaires

**Consultation documentaire du 8 septembre 2026. Aucune compilation ni exécution locale du mod dans cette livraison.**

## Mécanismes natifs observés

Les noms ci-dessous sont natifs ; les types/opérations C13 sont ceux d’ISRWorldGen, encore à implémenter.

| Point | Observation documentaire | Conséquence limitée |
|---|---|---|
| `IModLoader.Systems` et `GetModSystem(string)` | Énumération des ModSystems et récupération par nom complet ; l’overload générique est contraint à ModSystem. [API-1] | Découverte d’un provider possible. Ne pas appeler le générique avec une interface arbitraire. Le partage de l’assembly de contrat et le cas fournisseur absent doivent être testés. |
| `IMapRegion.GetModdata/SetModdata` | Méthodes de données mod persistantes côté serveur. [API-2] | Support possible d’un catalogue régional ; cela ne démontre ni accès aux régions déchargées ni atomicité multi-clés. |
| `IMapChunk.GetModdata/SetModdata`, `MarkDirty()` | Stockage de métadonnées de colonne et signalement de sauvegarde. [API-3] | Qualifier réellement écriture/relecture/redémarrage et le protocole dirty du backend retenu. Ne pas recopier les anciens noms obsolètes SetData/GetData. |
| `ISaveGame.GetData/StoreData` | Données persistantes associées à la sauvegarde ; `SavegameIdentifier` identifie la sauvegarde. [API-4] | Possibilité pour manifeste/références, pas justification de stocker un catalogue mondial non borné dans une unique clé. |
| `IServerEventAPI` | Hooks mapregion/mapchunk/colonne et initialisation worldgen. [API-5] | Vérifier l’ordre réel et le thread ; enregistrer les reçus au moment sûr des producteurs, pas au premier événement simplement disponible. |

La doc IMapChunk distingue notamment la heightmap terrain de génération d’autres cartes de hauteur ; ne pas interpréter toutes les cartes comme le sol courant. [API-3] L19 exporte la signification exacte retenue avec sa résolution et sa phase.

Les descriptions XML anciennes peuvent différer des symboles d’une version installée. Verrouiller version client/serveur, framework du template, DLL/empreintes, API source correspondante et signatures avant code. Aucune version de .NET ni nouvelle bibliothèque n’est imposée par ce plan ; la création du contrat partagé ne justifie pas Harmony.

## Probes locaux requis

E prouve le chargement unique du contrat, la découverte typée et le démarrage sans fournisseur. D prouve le stockage/dirty/publication choisi et les lectures de métadonnées sans chargement/génération de chunks. F rejoue ces points sur des binaires distincts dans le jeu/serveur réel, après déchargement et redémarrage.

La voie standard est de réutiliser le stockage C05/L10. Une API qui retourne une région déjà chargée ne suffit pas à prouver le besoin hors-chunks. Ne pas inventer une méthode native de chargement sans génération ni invoquer arbitrairement un moteur depuis des workers : qualifier l’accès réel, l’adapter dans L10 au besoin et conserver les limites explicites.

Le catalogue C13 n’est pas un `IPointOfInterest` natif et ne s’enregistre pas automatiquement dans le registre natif. La compatibilité de l’API publique de notre mod ne garantit pas les comportements d’un animal vanilla ; ce n’est pas la mission.

## Sources primaires

[API-1] Documentation officielle : https://apidocs.vintagestory.at/api/Vintagestory.API.Common.IModLoader.html

[API-2] Documentation officielle : https://apidocs.vintagestory.at/api/Vintagestory.API.Common.IMapRegion.html

[API-3] Documentation officielle : https://apidocs.vintagestory.at/api/Vintagestory.API.Common.IMapChunk.html

[API-4] Documentation officielle : https://apidocs.vintagestory.at/api/Vintagestory.API.Server.ISaveGame.html

[API-5] Documentation officielle : https://apidocs.vintagestory.at/api/Vintagestory.API.Server.IServerEventAPI.html

Références projet examinées via GitHub connecté : `docs/01-PLAN.md`, `registry/tasks.json`, `registry/plan-scopes-r13.json`, `tasks/L13/L13-A.md`, `docs/future-fauna/inventaire.md`, arborescence `src/` et diff du commit `fef55915fe9fccdfdc1c144dc4e76884eb462b83` (cartographie de recette et pause B). Les assertions de tests historiques présentes dans l’inventaire sont ses déclarations, pas des essais reproduits ici.

Repository de référence : https://github.com/blackcancer/ISRWorldGen/tree/fef55915fe9fccdfdc1c144dc4e76884eb462b83

Les sources sont consultées pour préparer le plan, pas pour certifier une installation locale. Les documents arbres L20 sont conservés depuis 1.3 ; leur audit local reste un travail L20-A avant toute implémentation post-V1.
