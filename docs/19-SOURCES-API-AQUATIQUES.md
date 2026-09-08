# Sources et points d’intégration aquatiques

**Consultation documentaire : 8 septembre 2026. Compilation et exécution locales : non effectuées ici.**

## Base ISRWorldGen

Le dépôt observé est `blackcancer/ISRWorldGen` au commit `fef55915fe9fccdfdc1c144dc4e76884eb462b83`. Le dossier 1.4 embarqué en référence est une livraison de plan, pas une preuve d’adoption dans votre clone. Les chemins ci-dessous se lisent sur cette baseline, puis se confrontent aux changements locaux.

| Source | Observation limitée | Conséquence |
|---|---|---|
| [S17](https://github.com/blackcancer/ISRWorldGen/blob/fef55915fe9fccdfdc1c144dc4e76884eb462b83/specs/S17.md) et [T17](https://github.com/blackcancer/ISRWorldGen/blob/fef55915fe9fccdfdc1c144dc4e76884eb462b83/tests/T17.md) | Aquatiques, profondeur et types d’eau déjà mentionnés ; campagne explicitement marine insuffisamment détaillée. | Compléter L17-A…D, pas créer un second générateur végétal. |
| [S07](https://github.com/blackcancer/ISRWorldGen/blob/fef55915fe9fccdfdc1c144dc4e76884eb462b83/specs/S07.md) | Bathymétrie, littoraux, identité et types d’eau déjà prévus. | Réutiliser ces sorties et préciser leur géométrie/qualité pour L19. |
| [S11](https://github.com/blackcancer/ISRWorldGen/blob/fef55915fe9fccdfdc1c144dc4e76884eb462b83/specs/S11.md) | Distinction fond/surface pour métadonnées natives et ownership des passes. | Vérifier après coraux et lors de la faune, sans remplacer toutes les heightmaps par une hauteur unique. |
| [L07](https://github.com/blackcancer/ISRWorldGen/blob/fef55915fe9fccdfdc1c144dc4e76884eb462b83/tasks/L07/README.md) et [L11](https://github.com/blackcancer/ISRWorldGen/blob/fef55915fe9fccdfdc1c144dc4e76884eb462b83/tasks/L11/README.md) | L07-C dépend de L07-B/L11-A ; L11-B assemble après L17-C. | Ajout L07-C → L17-C sans dépendance retour L11-B → L07-C. |
| [Cartographie](https://github.com/blackcancer/ISRWorldGen/blob/fef55915fe9fccdfdc1c144dc4e76884eb462b83/docs/15-CARTOGRAPHIE-DE-RECETTE.md) | Cartes avec provenance et oracles complémentaires. | Ajouter couches aquatiques et coupes verticales sans faire de l’image un verdict. |

## API et code natif

**API-AQ-01 — Placement submergé.** La [documentation officielle de Block](https://apidocs.vintagestory.at/api/Vintagestory.API.Common.Block.html) expose `TryPlaceBlockForWorldGenUnderwater(IBlockAccessor, BlockPos, BlockFacing, IRandom, int, int, BlockPatchAttributes)`. La présence d’une signature ne prouve ni tous les prérequis ni l’absence d’effets secondaires d’une surcharge.

**API-AQ-02 — Algues/plantes en colonne.** [BlockSeaweed.cs](https://github.com/anegostudios/vssurvivalmod/blob/849fa8cad9e392368566efc7474e73db6404a145/Block/BlockSeaweed.cs), commit `849fa8cad9e392368566efc7474e73db6404a145`, comporte placement submergé, contrôles de support et assemblage de sections. La méthode auxiliaire `PlaceSeaweed` est interne dans cette source : ne pas la traiter comme une API publique de mod. Les limites, supports et couches doivent être qualifiés via les entrées accessibles sur la cible locale.

**API-AQ-03 — Récifs.** [BlockCoral.cs](https://github.com/anegostudios/vssurvivalmod/blob/849fa8cad9e392368566efc7474e73db6404a145/Block/BlockCoral.cs), même commit, utilise des attributs de patch, modifie du support et place des structures, plantes et décors associés. Le nom de classe de base ne suffit donc pas à décrire son emprise. Les écritures, réservations et reçus nécessitent une qualification spécifique ; ce constat n’est pas une autorisation de recopier ses méthodes internes.

**API-AQ-04 — Faune native.** [93.GenCreatures.cs](https://github.com/anegostudios/vsessentialsmod/blob/06673b67318e002332b6aa8b3d0308f11d2aba99/Systems/WorldGen/Standard/ChunkGen/93.GenCreatures.cs), commit `06673b67318e002332b6aa8b3d0308f11d2aba99`, enregistre une passe PreDone, produit/consomme des cartes animales et vérifie cartes climatiques, blocs/supports et conditions de spawn. Un callback intervient aussi lors de tentatives de spawn. Cela justifie la recette L11 distincte de L17. Ce fichier ne constitue pas à lui seul un audit complet du spawner runtime ni des espèces disponibles.

**API-AQ-05 — Cartes et persistance.** [IMapRegion](https://apidocs.vintagestory.at/api/Vintagestory.API.Common.IMapRegion.html) expose notamment `AnimalSpawnMaps`, `BlockPatchMaps`, `GetModdata` et `SetModdata`. La documentation décrit des données de mod persistées côté serveur. Cela ne démontre ni un accès arbitraire aux régions hors mémoire, ni l’atomicité multi-clé, ni la sûreté de tous les appels hors thread principal. C05/L10 et les essais de L19 doivent démontrer ces propriétés sans stocker un catalogue mondial dans une seule valeur.

**API-AQ-06 — Découverte du service.** [IModLoader](https://apidocs.vintagestory.at/api/Vintagestory.API.Common.IModLoader.html) expose `Systems`, `GetModSystem` et `IsModEnabled`. Ces primitives permettent d’étudier la découverte d’un provider. Elles ne garantissent pas à elles seules que deux mods partagent la même identité de type/ABI ; le packaging C13 et le chargement sont éprouvés par le consommateur externe.

## Vérification locale imposée avant code

Lire versions/empreintes des DLL et assets installés, TargetFramework du template et capacités MCP réelles. Auditer la chaîne complète de placement/spawn, son ordre, les couches de fluide et les flags requis pour sauver les métadonnées. Utiliser uniquement des signatures publiques disponibles, sans Harmony ni nouvelle bibliothèque par défaut. Une adaptation nécessaire est ciblée, documentée et testée avec sa désactivation.

Le catalogue réel de plantes, coraux, poissons ou autres entités n’est pas extrait par cette livraison : son extraction et sa couverture relèvent de L17-A/L11-A. Aucun support de liquide saumâtre, de salinité numérique, de température d’eau, de nutriments ou de comportement migratoire n’est affirmé sans producteur qualifié.
