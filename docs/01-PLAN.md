# Plan de développement ISRWorldGen — révision 1.2

**Insertion depuis L05-C · 8 septembre 2026 · conserver historique et identifiants.**

## Point d’entrée actuel
Le développement est déclaré à L05-C. Vérifier sa clôture réelle, ne pas reprendre à L00. [La procédure d’adoption](10-MIGRATION-DEPUIS-L05-C.md) et L05-D organisent les compléments. Les anciennes tâches ne sont pas présumées DONE ; le registre local et leurs preuves font foi.

## Séquence d’insertion
| Vague | Travail | Ce qui doit être prouvé |
|---|---|---|
| 0 | Fin ou confirmation de L05-C, puis **L05-D** | Ports/budgets existants, état préservé, delta et requalifications identifiés. |
| 1 | **L14-A/B** et **L18-A** ; L14-C dès L11-A disponible | Géologie 3D native et bilan neige/fonte raccordé à l’année hydrologique. |
| 2 | Reprise de **L06** et **L08**, puis **L07** selon DAG | Érosion et cavernes consomment les mêmes roches/apports ; rivière jusqu’à la mer. |
| 3 | **L15-A/B**, **L16-A/B/C**, **L17-A/B/C**, **L18-B/C** | Sols, occurrences/prospection, habitats/placement et neige/glace prouvés sur fixtures natives. |
| 4 | **L11-B**, puis **L18-D**, **L15-C**, **L16-D**, **L17-D** | Colonne complète et cycles en jeu : agriculture, ressources, végétation, neige/glace. |
| 5 | **L11-C**, **L09-C**, diagnostics et **L13** | Progression/serveur, accès après toutes passes/saisons, corpus et distribution V1. |

Ces vagues ne remplacent pas le DAG : stockage, profils, assets fantastiques et autres tâches indépendantes avancent quand leurs dépendances le permettent. En particulier L16-A peut démarrer dès L14-B, sans attendre la fin des sols. L18-C peut avancer dès L07-C sans attendre le modèle de neige initiale. Une session MCP commune reste exclusive.

## Lots existants conservés


| Lot | Objet | Livrable de sortie |
|---|---|---|
| [L00](../tasks/L00/README.md) | Environnement, API et preuve d’intégration | Cible locale, MCP débogable et spike terrain sûr |
| [L01](../tasks/L01/README.md) | Cœur déterministe et contrats de base | Contrats C#, RNG, coordonnées et banc analytique |
| [L02](../tasks/L02/README.md) | Atlas global et maillage Voronoï/Delaunay | Maillage/global atlas borné, ownership et profils |
| [L03](../tasks/L03/README.md) | Géologie, continents, relief initial et matériaux | Continents, relief initial, couches et propriétés rocheuses |
| [L04](../tasks/L04/README.md) | Climat, précipitations et bilan de l’eau | Température, précipitations, recharge et budgets |
| [L05](../tasks/L05/README.md) | Bassins versants, lacs et réseau de drainage | Dépressions, drainage, débits et ports partagés |
| [L06](../tasks/L06/README.md) | Évolution du relief et raffinement multi-échelle | Érosion, dépôts et raffinement conservatif |
| [L07](../tasks/L07/README.md) | Cours d’eau, lacs, littoraux et océans détaillés | Lits, eau, lacs, côtes et deltas stables |
| [L08](../tasks/L08/README.md) | Cavernes naturelles et hydrologie souterraine | Réseaux naturels, volumes et navigation |
| [L09](../tasks/L09/README.md) | Sites fantastiques rares, connectés et explorables | Catalogue rare, raccords et certificats d’accès |
| [L10](../tasks/L10/README.md) | Persistance, streaming, concurrence et sûreté | Manifeste, caches/scheduler et reprise après crash |
| [L11](../tasks/L11/README.md) | Adaptateur Vintage Story et compatibilité du monde | Colonnes natives, maps, contenu vanilla et serveur |
| [L12](../tasks/L12/README.md) | Assets, configuration et outils de diagnostic | Assets, profils, aperçu et outils de diagnostic |
| [L13](../tasks/L13/README.md) | Qualification complète et distribution | Campagnes, performances, parcours et distribution |



## Lots de distribution ajoutés
| Lot | Objet | Sous-lots |
|---|---|---|
| [L14](../tasks/L14/README.md) | Strates et roches | A catalogue ; B géologie 3D ; C matérialisation/métadonnées. |
| [L15](../tasks/L15/README.md) | Sols et fertilité | A profils ; B probe de placement ; C agriculture et conservation finales. |
| [L16](../tasks/L16/README.md) | Minerais/gisements | A règles/potentiel ; B occurrences ; C placement/prospection ; D progression et persistance. |
| [L17](../tasks/L17/README.md) | Végétation | A habitats/catalogue ; B peuplements ; C appels natifs ; D récoltes/plantations/saisons. |
| [L18](../tasks/L18/README.md) | Neige et glace | A climat/bilan annuel ; B distribution initiale ; C gel-dégel ; D année complète et coûts. |

L05-D est un sous-lot de transition dans L05, pas un nouveau lot de terrain. Les 61 tâches conservent des lectures limitées et des périmètres propres. Les cinq nouveaux lots n’ont volontairement pas une division uniforme.

## Dépendances modifiées sur les anciens lots
| Tâche inchangée en identité | Prérequis ajoutés | Motif |
|---|---|---|
| L06-A | L14-B, L18-A | Érosion fondée sur roche et eau communes. |
| L08-A | L14-B, L18-A | Cavernes dans les bons hôtes, transferts d’eau cohérents. |
| L11-B | L14-C, L15-B, L16-C, L17-C, L18-B, L18-C | Aucun assemblage complet sans les cinq distributions. |
| L11-C | L15-C, L16-D, L17-D, L18-D | Recette native globale après les campagnes spécialisées. |
| L12-B | L05-D | Profils exposant les nouveaux paramètres validés. |
| L12-C | L16-D, L17-D, L18-D | Diagnostics fondés sur états réellement intégrés. |

La fiche et les prérequis de L05-C ne changent pas. Si une tâche aval a déjà commencé localement, conserver sa branche, classifier son delta et ne certifier sa version 1.2 qu’après les compléments.

## Portes de validation révisées


### G0 — Environnement et intégration prouvés
Version locale verrouillée, breakpoint réel MCP, preuve de remplacement sélectif et fluides/heightmaps. Sans cela, aucune prétention de compatibilité au jeu.

Tâches exigées : L00-A, L00-B, L00-C. Les gates précédentes doivent être qualifiées pour la même version ; une archive de preuve 1.1 reste historique, pas automatiquement valable pour les nouvelles clauses.


### G1 — Socle reproductible et atlas borné
Contrats gelés, atlas échantillonné, quotas, stockage abstrait et premiers codecs natifs éprouvés.

Tâches exigées : L01-A, L01-B, L01-C, L02-A, L02-B, L02-C, L10-A, L11-A. Les gates précédentes doivent être qualifiées pour la même version ; une archive de preuve 1.1 reste historique, pas automatiquement valable pour les nouvelles clauses.


### G2 — Bassin complet des sources à la mer
Bilan et raffinement conservés, profils fluviaux corrects et eau stable sur le moteur réel. Prototype interne de surface, pas V1. Strates 3D et agrégation annuelle neige-fonte qualifiées avant certification du bassin.

Tâches exigées : L03-A, L03-B, L03-C, L04-A, L04-B, L04-C, L05-A, L05-B, L05-C, L06-A, L06-B, L06-C, L07-A, L07-B, L07-C, L10-B, L05-D, L14-A, L14-B, L14-C, L18-A. Les gates précédentes doivent être qualifiées pour la même version ; une archive de preuve 1.1 reste historique, pas automatiquement valable pour les nouvelles clauses.


### G3 — Sous-sol et catalogue intégrés
Réseaux naturels, cinq familles fantastiques matérialisées et colonne complète ; certification finale reste à G4. La colonne complète intègre désormais sols, gisements/prospection, végétation et neige/glace initiales ; leurs cycles complets sont qualifiés à G4.

Tâches exigées : L08-A, L08-B, L08-C, L09-A, L09-B, L12-A, L11-B, L15-A, L15-B, L16-A, L16-B, L16-C, L17-A, L17-B, L17-C, L18-B, L18-C. Les gates précédentes doivent être qualifiées pour la même version ; une archive de preuve 1.1 reste historique, pas automatiquement valable pour les nouvelles clauses.


### G4 — Toutes les fonctions prévues raccordées
Accès finaux certifiés, reprise après crash, contenu vanilla, configuration et diagnostics prêts. Candidat de recette, non encore version validée. Agriculture, minerais/prospection, récoltes/plantations et cycles neige/gel/dégel ont leurs preuves réelles.

Tâches exigées : L09-C, L10-C, L11-C, L12-B, L12-C, L15-C, L16-D, L17-D, L18-D. Les gates précédentes doivent être qualifiées pour la même version ; une archive de preuve 1.1 reste historique, pas automatiquement valable pour les nouvelles clauses.


### G5 — V1 qualifiée et distribuable
Corpus, revue, endurance, jeu/serveur et installation propre validés. Tous les tests bloquants effectivement exécutés. Tous les cas supplémentaires T14 à T18 et T05U sont inclus ; un ancien rapport 1.1 ne certifie pas la recette 1.2.

Tâches exigées : L13-A, L13-B, L13-C. Les gates précédentes doivent être qualifiées pour la même version ; une archive de preuve 1.1 reste historique, pas automatiquement valable pour les nouvelles clauses.


## Règles d’exécution
Un seul détenteur modifie chaque contrat ou fichier de projet. Les tests exigés d’une tâche sont réellement exécutables à son stade : les probes natifs précoces ne prétendent pas valider le monde complet. Les campagnes finales reprennent les interactions après cavernes, sols forestiers, météo, structures et modifications du joueur.

Le modèle physique n’est pas recalculé à l’apparition de chaque chunk. Une évolution de bilan/sol/roche impose une itération préparatoire cohérente ; les ports publiés ne changent pas pour faire entrer un détail local. Les réglages qui changent le monde sont versionnés et gelés pour une sauvegarde.

Voir [propriété des passes](11-PROPRIETE-DES-PASSES.md), [recette](../tests/00-RECETTE.md), [registre exact du DAG](../registry/tasks.json) et [traçabilité](06-TRACABILITE.md). Aucune durée de développement ni performance atteinte n’est présumée.

## Révision 1.3 — L19 et L20

L19 est une passation documentaire V1 pour un futur mod animalier, sans implémenter d’animaux, de POI runtime, de service, de simulation ou de contrat C# animalier. L19-A peut démarrer après L05-D ; L19-B dépend de L19-A ; L19-C dépend de L19-B, L11-C et L12-C. L13-A reçoit L19-C comme dépendance documentaire.

L20 est un chantier TreeGen **postérieur à ISRWorldGen 1.0.0**. Il dépend d’une G5 qualifiée et relève de G6 : aucun lot V1 ni aucun scénario de recette V1 ne dépend de L20/T20. Voir [l’intégration détaillée](13-INTEGRATION-L19-L20.md), [la vérification API arbres](14-VERIFICATION-API-ARBRES.md) et [la politique de périmètre](../registry/plan-scopes-r13.json).
