# Matrice de compatibilité 1.1 → 1.2 — L05-D

**Portée.** Audit des sorties réellement présentes L03-C, L04-C et L05-C contre C08–C12 au commit de base L05-D. Les dispositions ne modifient ni contrat, ni snapshot, ni état d'exécution. `REUSE_WITH_EVIDENCE` réemploie uniquement les preuves citées; `EXTEND_AND_RETEST` exige un sidecar/modèle nouveau et les régressions du consommateur; `BREAKING_NEW_REVISION` interdit toute réécriture d'un monde publié; `BLOCKED` requiert une décision/version partagée.

| Source réel | Cible 1.2 / champ et unité | Version / consommateur | Disposition | Justification et preuve réutilisée |
|---|---|---|---|---|
| C01 — `MaterialSnapshot` L03-C | C08 `GeologyVolumeSnapshot`: codes/layers/fractures, coordonnées monde; coefficients normalisés [0,1] | C01 schema/checksum existant → GeologyRevision absent; L14-A/B, C09/C10/C11 | EXTEND_AND_RETEST | Les couches, fractures, ID et lecture immutable existent, mais ni `RockKey`, `CatalogId`, `ModelScaleId`, ni `GeologyRevision` publiés. L03-C Core Debug/Release 10/10, mais T14-01..08 sont nécessaires. |
| C01 — `MaterialSnapshot` L03-C | C09 `SoilProfile`: horizons L modèle, humidité/fertilité [0,1], drainage | Révisions geology/terrain/climate; L15-A/B, C11, C18 | EXTEND_AND_RETEST | Les propriétés matériaux sont une entrée valable, pas un profil de sol ni un mapping natif versionné. Aucun calcul de sol n'est réutilisable comme sortie. |
| C01 — `MaterialSnapshot` L03-C | C10 `MineralPotentialSnapshot`/occurrences: profondeur-altitude et potentiel par ressource | GeologyRevision/CatalogId; L16-A/B/C, prospection | EXTEND_AND_RETEST | Les hôtes/strates sont une base analytique, mais catalogue, ressources, occurrences et masque cavité manquent. |
| C01 — `MaterialSnapshot` L03-C | C11 `EcologicalContext`: géologie, support, pente/habitat | GeologyRevision requis; L17-A/B/C | EXTEND_AND_RETEST | Le matériau peut alimenter le contexte; climat, sol, terrain et ownership restent absents. |
| C02 — `WaterBudgetSnapshot` L04-C | C09 consommation hydrologique de référence, infiltration/recharge | Cycle/itération existants; L15-A, L18-A, L06-A | REUSE_WITH_EVIDENCE | Bilan pluie/ET/réserve/recharge/ruissellement, transferts et non-convergence sont déjà immuables et déterministes. L04-C Debug/Release 7/7 et régression amont 23/23. Toute pédologie qui change la recharge doit devenir une itération antérieure: T15/T18 requis. |
| C02 — `WaterBudgetSnapshot` L04-C | C12 `SeasonalWaterSidecar`: P liquide/solide, stockage/fonte et agrégation annuelle | `ClimateRevision`, périodes/poids et sidecar absents; L18-A | EXTEND_AND_RETEST | Le budget annuel est l'oracle de conservation, mais ne porte ni périodes ni neige. T18-01..03 et régression T05-05/06 nécessaires. |
| C03 — `BoundaryPortSnapshot` L05-C | C08 signature de snapshot liée à la géologie | `AlgorithmVersion=1`, itération, relief signature existent; L14-B/L06/L08 | EXTEND_AND_RETEST | Signature/itération et port immuable sont conservables, mais C08 exige `GeologyRevision` et `CatalogId` sans format ajouté au port C03. Sidecar lié par PortId/révision requis. |
| C03 — `BoundaryPortSnapshot` L05-C | C10 référence géologie pour dépôts proches eau/cavités | Version C03 1 existante; L16-C | EXTEND_AND_RETEST | Débit, profil et corridor protégé peuvent contraindre les consommateurs; la géologie commune/version de dépôt manque. |
| C03 — `BoundaryPortSnapshot` L05-C | C11 habitat rive/aquatique | PortId/corridor/débit disponibles; L17-C | EXTEND_AND_RETEST | Les données hydro servent de contexte, mais type d'eau, profondeur locale et `PlacementPolicy` ne sont pas publiés. |
| C03 — `BoundaryPortSnapshot` L05-C | C12 débit par période et stock neige | C03 v1 ne contient que débit annuel; L18-A/B/C | BLOCKED | Ajouter `seasonalDischarge` au port/snapshot existant serait une extension de format non versionnée. Demande limitée: conserver C03 v1 et publier `SeasonalWaterSidecar` versionné lié par `PortId`, vérifiant somme(w_i*q_i)=q_annuel. |
| C01/C02/C03 combinés | Révision commune C08–C12 et identité/snapshot C00 | L14-B, L18-A puis L06-A/L08-A | BREAKING_NEW_REVISION | Une modification de géologie, unités, seed ou bilan annuel doit publier une nouvelle révision et générer seulement un nouveau monde de test; jamais recalculer silencieusement un monde joué. |

## Mapping des chemins logiques vers ISRWorldGen réel

| Chemin logique du plan | Projet réel / chemin | Nature |
|---|---|---|
| `WorldGen.Core` | `src/WorldGen.Core/WorldGen.Core.csproj` | Cœur indépendant du jeu; L03-C `Geology/Materials`, L04-C `Climate/WaterBudget`, L05-C `Hydrology/Boundaries`. |
| `WorldGen.Runtime` | `src/WorldGen.Runtime/WorldGen.Runtime.csproj` | Orchestration/runtime sans renommage. |
| `WorldGen.VintageStory` | `src/WorldGen.VintageStory/WorldGen.VintageStory.csproj` | Adaptateur jeu; aucun appel API ajouté par L05-D. |
| `WorldGen.Tools` | `src/WorldGen.Tools/WorldGen.Tools.csproj` | Outils du produit; `tools/prepare_plan_update.py` est un outil documentaire séparé, non un build du mod. |
| `WorldGen.Tests` | `testsrc/WorldGen.Tests/WorldGen.Tests.csproj` | Tests MSTest; L05-D est `testsrc/WorldGen.Tests/L05D/`. |
| `WorldGen` (ancien plan) | Alias logique des projets `ISRWorldGen` ci-dessus | Aucun renommage de solution, projet, modid ou répertoire. |

## Handoff limité demandé à l'intégrateur

Décider et versionner, sans modifier C03 v1: un `SeasonalWaterSidecar` C12 immuable lié par `(ParentSnapshotHash, PortId, ClimateRevision)` avec unités explicites et conservation annuelle. L05-D n'implémente ni ne modifie cette extension. Une décision qui ajouterait des champs saisonniers dans `BoundaryPortSnapshot` v1 doit être refusée comme rupture et remplacée par une nouvelle révision/migration explicite.
