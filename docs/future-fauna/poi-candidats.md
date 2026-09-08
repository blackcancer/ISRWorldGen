# Catégories de POI candidates

**Statut : catalogue conceptuel L19-B, brouillon historique conservé et complété pour r1.5.** Les catégories sont des étiquettes de documentation. Elles ne créent ni point persistant, ni index spatial, ni provider, ni callback d'invalidation, ni comportement en jeu.

## Règle commune

Un candidat combine éventuellement un **habitat potentiel** et une information de génération. Il devient utile seulement après contrôle du **stock courant** et de l'**accès spécifique à l'espèce**. Ces contrôles restent à la charge du futur mod, près de l'animal et dans les chunks nécessaires ; ils ne justifient pas un scan mondial ni une recherche complète à chaque tick.

| Catégorie candidate | Sources éventuelles et signification limitée | Critères ou données manquants | Validité et contrôle à l'usage | Responsable futur |
|---|---|---|---|---|
| Abreuvement potentiel | `isr.hydro.reach`, `isr.hydro.budget` : débit/bilan annuels préparatoires. | Tracé, rive, eau libre, profondeur, courant, salinité, gel, obstruction, disponibilité et stock actuel. | Locale et datée ; une berge gelée est un cas négatif : eau potentielle ne signifie pas abreuvement possible. | Observation des blocs/eau, navigation, risque, réservation. |
| Pâturage potentiel | À terme `isr.soil.profile` et `isr.vegetation.habitat`; aujourd'hui planifiés. | Espèces végétales, biomasse, innocuité, quantité, repousse, concurrence et consommation précédente. | Locale, invalidée par récolte, construction, feu ou modification joueur. Une fertilité ne devient jamais une ration. | Inventaire courant, consommation, repousse, concurrence. |
| Cueillette potentielle | À terme catégories végétales L17. | Baies/champignons présents, toxicité, maturité, stock et renouvellement. | Locale et datée ; une ressource récoltée est le cas négatif. | Identification, règles d'espèce, réservation, invalidation. |
| Repos ou abri potentiel | À terme couvert L17 et réseaux C04/L06--L09. | Volume, entrée, exposition, température, danger, occupation, support et accès par espèce. | Locale ; un abri inaccessible est le cas négatif. Un corridor joueur ne suffit pas. | Diagnostic, navigation, occupation, politique de refuge. |
| Passage potentiel | Relief géologique ponctuel, futures vallées/berges et corridors. | Pente de surface, largeur, obstacles, support, hauteur libre, gel/eau, graphe navigable. | Réévaluation autour de l'agent ; aucune garantie issue d'une couche ou débit. | Recherche de chemin, invalidation locale. |
| Zone de chasse ou territoire | Notion propre au futur produit ; le couvert éventuel peut seulement guider une recherche. | Espèces, populations, proies, limites, compétition, signaux de présence, stock courant. | Temporel, non dérivé de la forêt ou d'un POI de génération. Une forêt n'atteste pas une proie. | Territoires, besoins, perception, règles de jeu. |

## Cas négatifs à conserver

- Une nappe, une résurgence ou un débit annuel ne garantissent pas une eau atteignable et non gelée.
- Une forêt abattue, une plante récoltée ou un champignon consommé invalide un candidat de nourriture sans modifier le plan ISRWorldGen.
- Une ouverture ou un corridor joueur ne garantit pas des dimensions ni risques acceptables pour une espèce.
- Un chunk non chargé n'est ni une observation courante, ni une absence de ressource.

Persistance, indexation, réservation et invalidation des candidats appartiennent à un produit futur ; elles ne sont pas spécifiées ni requises pour ISRWorldGen 1.0.0.

## Addendum r1.5 — catégories aquatiques candidates

Ces catégories utilisent uniquement les futurs champs C13-AQ proposés. Elles sont valables dans l'emprise et l'intervalle vertical annoncés, sur une révision et une couverture explicites; elles ne sont jamais des POI natifs ni des décisions biologiques.

| Catégorie | Sources minimales proposées | Vérifications au moment de l'usage | Cas négatif discriminant |
|---|---|---|---|
| Habitat aquatique potentiel | `isr.aq.water-geometry`, `isr.aq.water-environment`, `isr.aq.habitat-candidate`. | Espèce, volume réellement présent, profondeur, état de surface, accès et sécurité courants. | Deux volumes superposés au même XZ ne sont pas un seul habitat. |
| Ressource/couvert fixe potentiel | `isr.aq.fixed-community`, `isr.aq.bottom`, reçus et motifs de rejet. | Existence actuelle, type réellement consommable, quantité, récolte, concurrence et support accessible. | Un corail placé ne transforme pas le fond de référence en stock; un patch rejeté n'est pas une communauté vide. |
| Refuge ou reproduction potentiel | `isr.aq.habitat-candidate`, fond, colonne et interfaces sourcés. | Exposition, prédateurs, occupation, règles de l'espèce et navigation locale. | Une cavité noyée ou une rive ne délivre aucun certificat de refuge ou de reproduction. |
| Passage aquatique ou amphibie potentiel | `isr.aq.connection`, géométrie et coverage verticaux. | Continuité présente, profondeur, courant, obstacles, gel et capacités de l'espèce. | Une connexion hydraulique connue n'est pas un chemin de poisson; un trou vertical empêche toute conclusion complète. |
