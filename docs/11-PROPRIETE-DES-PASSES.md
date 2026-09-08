# Propriété des passes et consommateurs natifs — 1.2
**Table d’architecture à confronter au journal réel des handlers de la cible. Ce n’est pas une liste de numéros de passes native inventée.**

| Donnée/opération | Décision autoritaire | Matérialisation / consommateur | Contrôle bloquant |
|---|---|---|---|
| Atlas, relief, eau initiale | L02–L07, C01–C03 | L11, codecs audités | IDs/ports, couches solides/fluides et niveaux distincts. |
| Roches/strates | L03 + L14, C08 | L14-C/L11-B | Ne pas laisser GenRockStrata écraser un second modèle. |
| Propriétés K/solubilité/recharge | C08 commun | L06/L08/L04 | Révisions identiques ; mélange interdit. |
| Minerais et sous-gisements | L16, C10 | L16-C/L11-B | Filtrage hôtes/vides et absence de doublon GenDeposits. |
| Prospection et guide | C08/C10 selon mode natif | L16-C | Pas de reconstruction vanilla indépendante ; comptage local ≠ potentiel. |
| Sols et classes de fertilité | L15, C09 | L15-B/L11-B | Mapping vers variantes natives ; ne pas traiter NPK comme un champ climatique. |
| Agriculture en partie | Systèmes natifs conservés | Adaptation climatique auditée | Pas de reset à l’épuisement, engrais, eau et reprise. |
| Arbres et arbustes naturels | L17, C11 | Générateurs natifs qualifiés | RNG/ownership/collision stables ; supports corrects. |
| Herbes et plantes/patches | L17, C11 | GenBlockLayers/patches réutilisés ou remplacés de façon ciblée | Inventorier les catégories, pré/post passes et exclusions story. |
| Sous-bois et sol forestier | L17 avec politique C09 | ForestFloorSystem si qualifié | Un propriétaire et conversion du sol explicite. |
| Plantations et arbres du joueur | Natifs conservés | Registre de générateurs | Pas de worldgen au reload ; pack ISRTreeGen indépendant. |
| Climat de référence | L04 + L18-A, C02/C12 | Climat natif selon codec/événements | Une seule correction altitude/latitude. |
| Température et météo datées | Natif, adaptation ciblée si nécessaire | Agriculture/flore/neige/glace | Une seule application de saison ; aucun axe oblique non raccordé. |
| Neige/glace de référence | L18-B, C12 | Colonnes initiales | Supports et métadonnées cohérents ; substrat préservé. |
| Accumulation neige datée | Natif ou chemin explicitement adapté, jamais deux | L18-D | Rattrapage borné, date et options respectées. |
| Gel/dégel/casse | Politique L18-C | Fluides/blocs natifs ou adaptateur dédié | Type d’eau, courant, niveau et ownership conservés. |
| Structures, grottes et merveilles | L08/L09 + réservations natives | L11/L09-C | Pas d’obstruction après toutes les passes et saisons autorisées. |

## Ordre logique de matérialisation
Décrire d’abord volumes rocheux, vides, réservations, occurrences minérales et eau. Évaluer ensuite les masques et matériaux finaux ; les gisements ne remplissent pas les vides. Poser les sols et couches superficielles, végétation et couvertures selon l’ordre natif audité, puis traiter l’état saisonnier. Les structures natives peuvent nécessiter une réservation ou plusieurs passages : leur ordre ne se devine pas depuis ce tableau. Recalculer les cartes au moment attendu par leurs consommateurs, pas une seule heightmap après tout.

L’ordre exact doit être tracé dans le jeu. Tout système retiré est remplacé pour ses autres consommateurs ou explicitement laissé initialisé sans sa passe de placement. Le fait qu’un ModSystem soit inutilisé pour générer ne prouve pas qu’on puisse le supprimer : prospection, patches et plantations peuvent encore le lire.

## Tests croisés après assemblage
L11-C rejoue T14-06/08, T15-03/04/05/07, T16-05/06/07/09, T17-04/05/07/08/09 et T18-05 à 10. L09-C vérifie les accès naturels avec les couvertures réellement présentes. Les actions volontaires du joueur ne déclenchent pas une réparation automatique du paysage.
