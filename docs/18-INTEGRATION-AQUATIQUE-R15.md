# Intégration documentaire et technique de l’avenant aquatique 1.5

**Dossier à intégrer par revue ; aucune copie globale ni réinitialisation de l’état.**

## 1. Entrée et priorités

Comparer d’abord la définition active des lots au plan 1.4. Si la branche locale reste en 1.3, intégrer les définitions A…F et C13 de 1.4 avant l’extension aquatique, sans réappliquer des fichiers anciens à des sources plus récentes. Les anciennes clauses « L19 sans runtime/service/index » sont obsolètes. L’inventaire et les travaux réalisés restent des données à conserver.

L’archive de référence 1.4 est fournie uniquement pour que ce dossier ne dépende pas de la mémoire d’une autre conversation. Ce dossier ne suppose aucune tâche terminée, ne lève aucune pause et ne propose aucun remplacement du registre d’exécution. Un statut ancien reste historique ; les critères 1.5 ont leur propre preuve de requalification.

## 2. Cibles de fusion

| Cible dans le dépôt | Modification à effectuer |
|---|---|
| `docs/01-PLAN.md` | Intégrer le plan directeur 1.5 en conservant le catalogue/historique utile et les dépendances L00–L18 ; préciser L17/L11/L19 aquatiques et L20 post-V1. |
| `AGENTS.md`, `docs/02-SOCLE.md` | Ajouter la séparation peuplements fixes / faune native / service environnemental et l’obligation de coverage aquatique 3D ; retirer toute interdiction générale de runtime L19. |
| `specs/S07.md`, `contracts/C03.md` | Préciser niveau local, emprise verticale, type d’eau, fond de référence et liens hydrauliques connus ; réutiliser les volumes/ports existants. |
| `specs/S08.md`, `contracts/C04.md` | Raccorder les eaux souterraines déjà prévues avec identité verticale ; ne pas inventer de nouvelle galerie ou navigabilité animale. |
| `specs/S14.md`, `specs/S15.md`, `contracts/C08.md`, `contracts/C09.md` | Préciser la sortie de substrat/support aquatique, distincte des critères de fertilité agricole. Aucun cycle vers L19. |
| `specs/S17.md`, `contracts/C11.md` | Élargir explicitement le périmètre aux communautés fixes sous-marines ; type de support, milieu/domaine vertical, emprise récif, ownership des sous-placements et reçu effectif. |
| `specs/S11.md`, `contracts/C06.md`, `docs/11-PROPRIETE-DES-PASSES.md` | Affecter faune/spawn natifs à L11, identifier cartes et phases consommées, et qualification avant/après les peuplements. Clarifier entretien des métadonnées après récif. |
| `specs/S18.md`, `contracts/C12.md` | Distinguer surface gelée, volume sous-jacent, régime/projection et état réel ; conserver type d’eau au dégel. |
| `specs/S19.md`, `contracts/C13.md` | Appliquer C13-AQ et rendre obligatoire le service aquatique 3D, ses familles de données, persistance et requêtes, sans état de population. |
| `docs/future-fauna/` | Étendre inventaire, dictionnaire et exemples à marine/eau douce/terre-eau ; conserver et requalifier les informations déjà produites. |
| `tasks/L07`, `L08`, `L10`, `L11`, `L12`, `L13`, `L14`, `L15`, `L17`, `L18`, `L19` | Fusionner les compléments fournis ; ne pas créer des doublons de missions ou renuméroter les sous-lots. |
| `tests/T07.md`, `T11.md`, `T17.md`, `T18.md`, `T19.md`, `tests/00-RECETTE.md` | Conserver les scénarios existants et référencer les nouveaux T-AQ au bon propriétaire. Les anciennes attentes contradictoires de 1.3 sont retirées. |
| `docs/15-CARTOGRAPHIE-DE-RECETTE.md`, `templates/HANDOFF.md` | Ajouter bathymétrie/support, habitats, connexions, couverture verticale et coupes, avec manifeste et oracle. |
| Registries de tâches, exigences, tests, gates, scopes, budgets et traçabilité | Fusionner 1.4 puis le delta 1.5 en respectant le schéma effectif ; auditer les outils qui supposeraient des IDs de tests uniquement numériques. |

La liste de cibles désigne les documents/projets existants ou leurs équivalents locaux ; ne pas créer des contrats en double parce qu’un fichier a été déplacé. Les changements de C#, `.csproj`, solution, dépendances, contrat partagé et fichiers communs appartiennent à l’intégrateur sous mandat limité.

## 3. Clause C11 à corriger explicitement

Conserver les invariants hydrologiques et les réservations publiées. Remplacer une lecture trop absolue de « la végétation ne modifie pas le terrain » par : « L17 ne change pas les décisions hydrologiques/géographiques publiées. Les modifications locales de support par un générateur natif, notamment récifal, sont autorisées seulement dans une enveloppe réservée, avec propriétaire, limites et reçu ; elles ne détruisent ni volumes protégés ni accès obligatoires. »

L07/C03 décrit le fond de référence ; L17/C11 décrit le delta de peuplement/support ; L11/C06 maintient les métadonnées de la colonne finale. L19 conserve ces bases/versions sans les fusionner en une prétendue mesure actuelle. La classification écologique de génération ne doit pas changer parce qu’un consommateur interroge ou non la zone.

## 4. Clause C13 à corriger explicitement

La recherche horizontale peut rester disponible pour les usages terrestres. Pour les usages aquatiques, le point XYZ, le compartiment, la tranche verticale et la couverture volumique deviennent obligatoires ; ne pas conserver « filtre vertical si supporté » comme unique fonctionnalité marine. L’ABI peut rester rétrocompatible par extension négociée ou exiger une version majeure : décision selon ce qui a réellement été distribué, pas selon le numéro du plan.

L’exclusion de spawning de C13 reste valable pour **L19**. Elle n’interdit pas à **L11** de maintenir les mécanismes de faune native déjà concernés par la compatibilité du monde. Une correction d’entrée native n’est pas un nouveau système de population ; une nouvelle règle biologique doit être reportée au futur mod.

## 5. Dépendances et gates

Appliquer les dépendances 1.4 de L19-A…F et le prérequis L19-F de L13-A. Ajouter ensuite **L07-C aux prérequis de L17-C**. Préserver tous les prérequis locaux compatibles ; toute dépendance supplémentaire qui forme un cycle doit être arbitrée, pas supprimée sans revue.

Ne pas ajouter L17-D ni L11-B à L07-C : le probe précoce fluide est qualifié sur une fixture sans assemblage final ; ses interactions après peuplement sont rejouées en L17-D/L11-C. L19-D suit L11-B, mais aucun producteur ne suit L19-E/F. Les essais de géométrie souterraine de L07-B sont analytiques au stade précoce ; le raccord réel L08 est contrôlé après son intégration, pas exigé comme nouveau prérequis de L07-B.

G3 inclut les preuves initiales T-AQ-01…08 et T-AQ-10, dans leur niveau déclaré. G4 inclut les preuves fonctionnelles applicables T-AQ-01…23 et les obligations 1.4, notamment L19-F. G5 clôture les reprises sur candidat final via T-AQ-24/L13. Les critères de test ne sont pas assimilés à l’achèvement d’une tâche indépendante de ses anciens critères. Aucune gate V1 n’attend G6/L20.

## 6. Requalification et quotas

Créer un delta de requalification avec : tâche/exigence, baseline de la preuve antérieure, changement 1.5, oracle nouveau, niveau analytique/probe/jeu et état réel. Requalifier uniquement les consommateurs touchés, sans réécrire l’historique ni régénérer automatiquement des goldens.

Distinguer limites contractuelles (rayon, nombre de résultats/voisins/pages, mémoire, I/O, temps alloué, emprise maximale de patch) et cibles de performance mesurées. Les premières se gèlent lors des contrats ; les secondes sur la machine/profil de référence avant holdout. Ne pas inventer une latence atteinte ni installer une nouvelle bibliothèque géométrique par défaut.

Les capsules sont limitées aux fiches et contrats pertinents. La lecture des liens n’est pas récursive. Les quatre missions L19-B/D/E/F peuvent recevoir un budget documentaire maximal proposé de **80 Kio**, au lieu de 64 Kio, pour C13-AQ et leur recette ciblée ; l’intégrateur mesure le contenu réel et justifie tout dépassement. Les autres conservent leur budget ou une capsule séparée. Ne pas charger toutes les recettes T-AQ pour une mission qui n’en possède qu’une partie.

## 7. Sortie d’adoption

Fournir un diff documentaire, la table de propriété, le DAG contrôlé sur le manifeste réel, les capsules mesurées, la matrice exigences/tests et le plan de requalification. Vérifier spécifiquement que le scope V1 inclut le service aquatique et la faune native applicable, mais exclut les nouveaux comportements et L20.

L’adoption documentaire n’autorise ni déploiement, ni publication, ni reprise d’une pause explicite. Après intégration, le compte rendu distingue les définitions prêtes, le code restant à développer et les tests réellement exécutés.
