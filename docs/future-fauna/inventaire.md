# Inventaire ISRWorldGen pour un futur mod animalier

**Statut : inventaire documentaire L19-A, sans API ni service animalier.**

## Baseline et méthode

Baseline examinée : `1dc769bd5fb0e0c0d4c0518db7f066d58960d6df` (branche
`agent/l19-a`). Les statuts ci-dessous résultent de la lecture ciblée de
`registry/state.json`, des contrats C01--C05 disponibles, des sources Core
nommées et de leurs journaux de preuves. Ils ne décrivent ni un monde joué ni
un état courant du jeu.

Nomenclature :

- **implémentée et vérifiée** : composant présent, avec preuve de test locale
  identifiée dans le registre/journal ; cela ne vaut pas validation en jeu ;
- **documentée--planifiée** : contrat ou spécification normative présent,
  mais lot non réalisé sur cette baseline ;
- **absente** : aucun modèle, export ou preuve de la donnée dans le dépôt
  examiné.

`inconnu`, `non généré`, `aucun candidat` et la valeur numérique `0` restent
quatre états distincts. Aucun ne doit être transformé en ressource ou habitat
disponible.

## Matrice des informations

| Famille | Information effectivement repérée | Disponibilité | Source et preuve | Repère, valeur et temps | Limites pour un futur consommateur |
|---|---|---|---|---|---|
| Terrain et relief | Volume géologique échantillonnable, surfaces de strates inclinées, failles, intrusions, couverture alluviale et coordonnée d'incision. | implémentée et vérifiée | `src/WorldGen.Core/Geology/Stratigraphy/StratigraphySnapshot.cs` : `GeologyVolumeSnapshot`, `StratigraphicSurface`, `StratigraphySurfaceSampler`; `worklogs/L14-B.md`, T14-B Debug/Release 4/4 dans `registry/state.json`. | Monde Core : `x,z` en entiers larges, `y` entier ; épaisseur et pentes dans les unités du modèle/volume, pas une pente de navigation documentée ; décision structurelle immuable par révision/fingerprint. | Ni modèle de pente de surface continu, ni franchissabilité, ni trajectoire d'animal. Une coupe de roche n'est pas un relief praticable. |
| Hydrologie | Partition annuelle pluie/ET/ruissellement/recharge/réserve, transferts souterrains et classification de débit (`Dry`, `Rill`, `Stream`, `River`). | implémentée et vérifiée | `src/WorldGen.Core/Climate/WaterBudget/WaterBudgetModel.cs` : `WaterBudgetSnapshot`, `GroundwaterTransfer`; `src/WorldGen.Core/Hydrology/Discharge/DischargeAccumulator.cs` : `DischargeSnapshot`, `DischargeReachClass`; `worklogs/L04-C.md` (T04-05/T04-06 Debug 7/7, régression Release 23/23). | Cellules et réservoirs identifiés ; pluie/débits en `L/Ymod` et `L³/Ymod`, année modèle ; snapshot tagué par itération. | Aucun cours d'eau matérialisé, plan d'eau, rive, profondeur, courant voxel, salinité ou présence d'eau actuelle n'est fourni. Une résurgence/napppe n'est pas un point d'abreuvement. |
| Climat | Latitude, température de référence et de surface, correction altitudinale, continentalité ; précipitation annuelle et humidité atmosphérique. | implémentée et vérifiée | `src/WorldGen.Core/Climate/Temperature/TemperatureModel.cs` : `LatitudeAxis`, `TemperatureEvaluation`; `src/WorldGen.Core/Climate/Precipitation/PrecipitationModel.cs` : `PrecipitationSnapshot`; preuves L04-A/L04-B/L04-C du `registry/state.json`. | `WorldBlockPosition` et axe de latitude Core ; température en °C modèle, précipitation en `L/Ymod`; instant de référence annuel/modèle, non daté selon le calendrier de partie. | L'adaptateur climat et la preuve moteur L04-A restent bloqués dans le registre. Ni météo courante, ni saison courante, ni confort thermique d'espèce ne sont disponibles. |
| Sols et fertilité | Humidité de sol normalisée dans le budget hydrique ; dépôt alluvial distinct du substrat dans l'échantillon géologique. | implémentée et vérifiée pour ces seuls champs ; documentée--planifiée pour le profil de sol/fertilité C09. | `contracts/C02.md` § `ClimateSnapshot`/`WaterBudgetCell`; `StratigraphySnapshot.cs` : `AlluvialDeposit`, `SurfaceMaterialSample`; `specs/S15.md`; L15-A/B/C sont `BACKLOG` dans `registry/state.json`. | Humidité normalisée et rétention du budget à la cellule ; dépôt avec épaisseur/provenance ; aucune unité native de fertilité déclarée. | Pas de classe de sol native, de profondeur de sol finalisée, de fertilité exploitable ni de biomasse. `ClimateCondition.Fertility` et fertilité d'un bloc ne doivent pas être déduites de ces données. |
| Végétation et couvert | Politique et contexte écologique définis (climat, sol, drainage, eau, altitude, pente, couvert, berge/lisière/versant/fond). | documentée--planifiée | `specs/S17.md` §§ « Catalogue et habitats », « Composition » et « Placement » ; L17-A/B/C `BACKLOG` dans `registry/state.json`. | Résolution, catégories natives réellement disponibles et période de validité : inconnues sur cette baseline. | Aucun peuplement, herbe, baie, champignon, arbre, clairière, sous-bois ou stock récoltable n'est produit. Un couvert potentiel ne démontre ni nourriture ni innocuité. |
| Neige et glace | Fraction neige/glace possible dans le contrat climat, et modèle saisonnier/gel-dégel spécifié. | documentée--planifiée | `contracts/C02.md` § `ClimateSnapshot`; `specs/S18.md`; L18-A/B/C `BACKLOG` dans `registry/state.json`. | Le contrat indique une fraction éventuelle ; aucune valeur, couche native, type d'eau gelé ou date de snapshot n'est publiée. | Ni neige actuelle, ni glace praticable, ni dégel, ni épaisseur. La première visite ne doit jamais être utilisée comme vérité climatique. |
| Refuges et accès | Réseaux de cavernes, entrées, galeries, volumes et certificat d'accès sont décrits pour le joueur. | documentée--planifiée | `contracts/C04.md` : `CaveNetwork` et certificat d'accès ; `specs/S19.md` § « Relief et accès » ; L06--L09 `BACKLOG` dans `registry/state.json`. | Aucune liste d'entrées, volume abrité, ouverture ni résolution spatiale n'est publiée dans la baseline. | Un certificat de corridor joueur ne calcule pas la navigation d'une espèce. Pas de refuge, POI ou territoire existant. |
| Persistance et diagnostic | Identité/révision/fingerprint de géologie et snapshots hydriques immuables. | implémentée et vérifiée pour les objets Core ; documentée--planifiée pour persistance monde/diagnostic complet. | `StratigraphySnapshot.cs` : `GeologyRevisionIdentity`; `WaterBudgetModel.cs` : snapshots ; `contracts/C05.md`; L10-A est `IN_PROGRESS`, L11/L12 `BACKLOG`. | Identités attachées aux objets Core ; pas de format de sauvegarde animalier, ni index de requête. | Ne reflète pas récolte, abattage, construction, incendie, fonte, assèchement, chunks non chargés ou modification joueur. |

## Dictionnaire documentaire minimal

| Identifiant | Définition et source | Disponibilité | Données/repère | Valeurs manquantes et contrôle futur |
|---|---|---|---|---|
| `isr.geo.sample` | Échantillon de roche/formation immuable : `GeologySample` dans `StratigraphySnapshot.cs`. | implémentée et vérifiée | Monde Core `x,y,z`; clé roche, `FormationId`, propriétés normalisées, révision et fingerprint. | Ne donne ni altitude de surface ni pente praticable. Le futur mod doit demander une conversion locale et contrôler le terrain réellement chargé. |
| `isr.geo.alluvium` | Couverture sédimentaire : `AlluvialDeposit` et `SurfaceMaterialSample`. | implémentée et vérifiée | Monde Core, épaisseur positive et provenance textuelle. | Absence de dépôt n'est pas une fertilité zéro ; aucune catégorie de sol ni consommation animale. |
| `isr.climate.temperature` | Température Core : `TemperatureEvaluation`/`TemperatureSample`. | implémentée et vérifiée | Position monde, °C modèle, référence/surface/correction ; calcul annuel de référence. | Météo et saison de jeu inconnues. Le consommateur doit valider la météo locale et la tolérance de l'espèce. |
| `isr.climate.precipitation` | Champ annuel de précipitation : `PrecipitationField`. | implémentée et vérifiée | `CellId`, humidité et précipitation en `L/Ymod`. | Ni eau accessible ni fréquence météo ; le futur mod doit vérifier une source d'eau actuellement présente. |
| `isr.hydro.budget` | Bilan hydrique préparatoire : `WaterBudgetSnapshot`. | implémentée et vérifiée | Réservoir/cellule, année modèle, composantes de bilan et itération. | Pas de géométrie de berge, profondeur ou qualité de l'eau ; transfert souterrain non assimilable à abreuvement. |
| `isr.hydro.reach` | Classe de débit préparatoire : `DischargeReachClass`. | implémentée et vérifiée | Identifiant de cellule/routage et débit annuel ; catégories `Dry` à `River`. | Le tracé, la largeur et la matérialisation sont absents. `Dry` n'est pas une absence mesurée de toute eau courante. |
| `isr.soil.profile` | Profil sol/substrat/fertilité C09 visé par S15. | documentée--planifiée | Unité, projection native, résolution et durée : inconnues. | Ne pas substituer une fertilité ou biomasse. L15 devra fournir une source qualifiée avant tout usage. |
| `isr.vegetation.habitat` | Contexte et peuplements de S17. | documentée--planifiée | Catégories et résolution inconnues. | Pas de couverture ou ressource existante ; L17 devra qualifier les assets et l'état chargé. |
| `isr.season.snow-ice` | Neige/glace C12/S18. | documentée--planifiée | Valeur, date, type d'eau et support inconnus. | Ne pas conclure à une surface gelée ou un accès ; L18 et le moteur devront le vérifier. |
| `isr.shelter.cave` | Cavité/réseau et accès C04. | documentée--planifiée | Volumes, entrées, révision prévus ; aucune sortie publiée. | Aucun abri ni passage animal ; navigation, danger et état joueur relèvent du futur mod. |

## Catégories de POI candidates (sans service)

| Catégorie candidate | Source éventuelle | Ce qui manque | Validité et contrôle requis |
|---|---|---|---|
| Abreuvement | Futur tracé matérialisé depuis `isr.hydro.reach`/plans C03. | Eau, rive, profondeur, salinité, gel, accès et maintien actuels. | Local et daté ; le futur mod vérifie le bloc/eau réelle et la navigation de l'espèce. |
| Pâturage/cueillette | Futurs sols et peuplements S15/S17. | Biomasse, espèce végétale, innocuité, stock, repousse et concurrence. | Local et invalide dès modification du monde ; géré par le futur consommateur. |
| Repos/abri | Futurs réseaux C04 et couvert S17. | Volume sûr, entrées, exposition, occupation et navigation. | Local, dépendant de l'espèce et des changements joueur ; aucune garantie ISRWorldGen. |
| Passage | Pentes, vallées, berges et corridors de C03/C04. | Graphe navigable par espèce, largeur, obstacles et état de surface. | À revalider près de l'animal ; pas de scan global ou de parcours complet à chaque tick. |

Ces catégories sont des noms de documentation. Elles ne créent ni POI persistant,
ni provider, ni index spatial, ni callback d'invalidation.

## Responsabilités et besoins ouverts

ISRWorldGen reste propriétaire des plans de génération et de leurs identités.
Le futur mod animalier devra définir les espèces, besoins, préférences,
perception, navigation, réservations et territoires. Il devra aussi évaluer
localement l'eau, le gel, l'accès, les blocs support, les plantes effectivement
présentes, leur consommation, leur renouvellement, l'invalidation et les chunks
non chargés.

Besoins ouverts, sans demande de runtime L19 : export stable de relief/eau
matérialisés ; profil C09/L15 qualifié ; sortie écologique L17 qualifiée ; état
saisonnier C12/L18 qualifié ; et une stratégie bornée de consultation locale à
concevoir par le futur produit. ISRWorldGen V1 ne dépend d'aucun de ces
comportements animaux.
