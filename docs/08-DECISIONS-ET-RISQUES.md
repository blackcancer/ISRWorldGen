# Décisions, hypothèses et risques
## Décisions de référence
ADR-001 : atlas global borné, raffinement local à contraintes, pas de super-régions toroïdales indépendantes.

ADR-002 : cœur C# CPU indépendant du jeu et du MCP ; aucune exigence de GPU compute.

ADR-003 : couplage préparatoire relief/climat/drainage avant publication ; pas de recomposition géographique après exploration.

ADR-004 : cavernes par réseau puis volumes ; merveilles rares intégrées aux galeries avec certificat d’accès final.

ADR-005 : nouvelle sauvegarde activée seulement en V1 ; données canoniques versionnées et caches reconstruisibles.

ADR-006 : contrats partagés gouvernés par l’intégrateur, sous-lots bornés et un seul détenteur de session MCP.

Ces décisions structurent la présente version documentaire. Leur évolution utilise [le modèle ADR](../templates/ADR.md) et met à jour les exigences et tests impactés.

## Choix proposés à geler, pas préférences utilisateur déjà connues
Nom WorldGen/worldgen ; profils d’étendue et hauteur ; nombres de sites ; seuils de rareté ; budgets de RAM/temps ; profil d’équipement des accès verticaux ; bibliothèque de géométrie ; runner de test ; format de snapshot ; placement exact des phases dans l’API. Chaque choix a un lot responsable et doit être consigné avant sa gate d’acceptation.

L’accès surface→galerie→merveille est la règle standard proposée, plus forte que la demande minimale « toujours reliée à une galerie ». Elle sert à garantir la découvrabilité. Une adaptation ultérieure peut changer le profil d’accès sans jamais autoriser une merveille isolée ou murée.

## Registre de risques
| Risque | Prévention et preuve | Porte de décision |
|---|---|---|
| Remplacement worldgen incompatible | Spike de handlers, fluides, métadonnées et cycle de vie | G0 |
| Framework/runtime supposé | Audit des DLL/template locaux et probe compilé | G0 |
| Atlas géant trop coûteux | Quota sites, estimation mémoire, profil vaste sans voxels globaux | G1 |
| Dépendances longues de fleuves | Réseau et ports globaux, génération depuis l’embouchure | G2 |
| Convergence ou bilans instables | Analytique, résidus et refus borné | G2 |
| Motifs Voronoï visibles | Témoins négatifs, mesures et revue multi-échelle | G2 puis G5 |
| Fluides natifs limités | Quantification contrôlée et endurance après ticks | G0 puis G2 |
| Grottes bouchées par décor/story | Réservations et certificat final sur blocs | G4 |
| Rare = jamais généré | Comptage de candidats/rejets et corpus non filtré | G4 |
| Sauvegarde fracturée après mise à jour | Manifeste, lecteur versionné, refus sûr et injection de crash | G4 |
| Saturation des workers/deadlock | DAG, single-flight, worker unique et stress | G4 |
| Conflits multi-agents | Worktrees, chemins disjoints et propriété des contrats | Chaque fusion |
| Coût du fantastique côté client | Assets sans shaders obligatoires et mesure rendu/collision | G3/G5 |
| Progression vanilla brisée | Inventaire de ressources et parcours réels | G4/G5 |

## Arbitrages interdits
Ne pas réduire la portée d’un besoin sans le dire : supprimer toutes les merveilles pour gagner de la RAM, remplir toutes les dépressions pour simplifier l’eau, retirer les structures story pour éviter une collision, remplacer les galeries par poches isolées ou revenir à vanilla en cas d’erreur ne sont pas des solutions conformes.

Une nouvelle version du jeu déclenche un audit de l’adaptateur et une campagne de compatibilité. Le cœur peut conserver son identité algorithmique seulement si ses sorties et formats restent identiques.
