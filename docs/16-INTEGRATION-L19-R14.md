# Adoption contrôlée du plan 1.4

**Ce dossier prépare des définitions. Il ne modifie pas le dépôt ni son état d’exécution.**

## Baseline et autorité

Base publique observée le 8 septembre 2026 : `fef55915fe9fccdfdc1c144dc4e76884eb462b83`. Elle contient déjà des ajouts 1.3, un inventaire A et une pause B. L’état local peut être plus récent : relever d’abord HEAD, modifications non commitées, worktrees, réservations, schémas et pauses ; ne pas afficher de secrets. Ne pas réappliquer la révision 1.2 comme si le projet était encore à L05-C.

L’autorité de cette correction est : support technique de données dans L19, aucune simulation animale, L20 après 1.0.0. S19/C13/T19 et les fiches 1.4 remplacent les clauses 1.3 incompatibles. Les exigences R19/T19-01, 06 et 08 changent substantiellement ; les autres ont des précisions et de nouveaux liens de preuve. Tous les IDs historiques restent traçables par version.

Ne pas effacer l’ancien inventaire ni réattribuer un test passé à une nouvelle sémantique. Conserver les preuves historiques et ajouter un registre de requalification selon le schéma existant. Une clause inchangée peut réutiliser une preuve identifiée après revue ; une clause modifiée exige sa nouvelle preuve.

## Documents candidats

Les fichiers de `payload/` sont des textes de référence à fusionner. `docs/01-PLAN.md` donne un plan directeur complet mais ne remplace pas automatiquement les ajouts locaux. Les fichiers L20/S20/T20 et audit arbres sont reproduits sans changement de périmètre depuis la livraison 1.3 ; comparer avec la version locale avant tout remplacement.

| Emplacement cible | Action d’intégration |
|---|---|
| `docs/01-PLAN.md`, `docs/13-INTEGRATION-L19-L20.md` | Remplacer les règles actives erronées de L19, conserver les obligations L00–L18 et les ajouts locaux. |
| `specs/S19.md`, `tests/T19.md`, `tasks/L19/` | Fusionner A/B/C révisés et ajouter D/E/F. |
| `contracts/C13.md`, `contracts/README.md` | Ajouter la frontière publique proposée et son entrée ; si C13 existe localement, arbitrer la collision au lieu d’écraser. |
| `docs/16-INTEGRATION-L19-R14.md`, `docs/17-API-ENVIRONNEMENT.md` | Ajouter procédure et contrôle API ciblé ; ne pas réutiliser le numéro 15 réservé à la cartographie. |
| `docs/future-fauna/` | Conserver l’inventaire réel. Revoir seulement ses déclarations de périmètre et préparer les compléments B/C. Le gabarit ne le remplace pas. |
| `AGENTS.md`, `docs/00-SYNTHESE.md`, `README.md` | Corriger la présentation de L19 et les lectures/règles actives, sans perdre l’orchestration ni la cartographie. |
| `specs/S17.md`, `contracts/C11.md` | Préciser que L19 projette les données sans posséder le peuplement ; L20 reste post-V1. Pas de dépendance producteur vers L19-F. |
| `tasks/L13/README.md`, `tasks/L13/L13-A.md`, `tests/00-RECETTE.md` | Remplacer la seule passation documentaire par le prérequis L19-F et les contrôles techniques ; conserver les autres obligations et les cartes. |
| `docs/06-TRACABILITE.md`, index de tests/contrats et sources | Ajouter les nouveaux IDs, propriétaires, C13 et les liens des preuves futures. |
| `registry/tasks.json`, `requirements.json`, `tests.json`, `gates.json` | Traduire le delta déclaratif dans les schémas locaux, conserver champs inconnus et dépendances supplémentaires. Ne pas recopier un registre neuf à la place de l’existant. |
| `registry/plan-scopes-r13.json` et consommateurs | Mettre à jour la politique effectivement lue pour inclure D/E/F en V1 et garder L20 post-V1. Soit mettre à jour le chemin actuel, soit migrer tous les consommateurs vers un chemin 1.4 ; aucune seconde politique active contradictoire. |
| `registry/state.json`, worklogs, preuves, verrous | Ne pas remplacer ni remettre à BACKLOG ; préparer seulement les ajouts/écarts d’état pour l’intégrateur, et préserver les pauses. |

## Clauses actives à utiliser dans les anciens documents

**AGENTS / synthèse.** « L19 fournit le socle environnemental intermods : inventaire et contrat, collecte et conservation de données de génération, API publique en lecture seule et indexation bornée, puis qualification par un consommateur externe. Il n’implémente pas les animaux. A/B/C restent documentaires ; D/E/F sont techniques. Toute déclaration de disponibilité actuelle nécessite une vraie observation. »

**L17 / C11.** « L17 conserve la responsabilité des habitats, peuplements et du placement. L19 consomme ses sorties validées et ses reçus disponibles pour le catalogue environnemental ; il ne replante pas et ne constitue pas une seconde distribution. Les extensions de producteur sont réservées et testées par leur propriétaire. L20 reste après G5 et aucune fonctionnalité avancée de L20 n’est un prérequis V1. »

**L13-A / recette.** « Conserver les prérequis existants et ajouter L19-F. La passation C seule ne valide pas le socle : D/E/F doivent prouver collecte/persistance/consultation et un vrai consommateur externe. Les scénarios T19 concernés et régressions sont repris sur la baseline finale. T20 reste exclu de G5. »

**G4/G5.** « G4 inclut L19-A à F et leurs preuves sur le socle technique. G5 confirme ces preuves et les régressions affectées dans la qualification globale ; aucun comportement animalier n’est demandé. »

Ne pas se contenter d’ajouter ces clauses à la fin en laissant ailleurs « L19 exclusivement documentaire » ou « aucun service/index/API ». Rechercher les anciennes formulations dans plan, tests, prompts, scripts et politiques. Les archives peuvent les conserver seulement avec un statut historique explicite.

## DAG et définitions

Conserver A→L05-D et B→A. Ajouter E aux prérequis existants de C. Ajouter D→B/L10-B/L11-B ; E→D/L02-B ; F→C/E/L10-C/L09-C ; L13-A→F. Ne pas ajouter C comme prérequis de D : C attend E, ce qui créerait une boucle. Ne pas ajouter L19-F comme prérequis de L11-B/C ou L12-C.

Les 6 missions L19 appartiennent à V1. Les 5 missions L20 restent post-V1 et requièrent G5 qualifiée. Le contrôle de cycle porte sur le graphe mixte tâches et gates, pas seulement la liste des tâches. Les gates ne remplacent pas leurs preuves et statuts effectifs.

Le delta JSON contient des définitions proposées et n’est pas directement le schéma de l’ordonnanceur. Vérifier sa traduction, le filtrage release_scope, requires_gates, les lectures obligatoires, les critères modifiés, les nouvelles tâches et les suspensions. Le validateur fourni effectue une vérification en mémoire ; il ne met à jour aucun fichier du dépôt.

## Pauses et autorisations

Le dépôt observé porte une pause utilisateur de L19-B. Préparer/adopter le plan ne la lève pas. Les D/E/F restent des tâches proposées tant que le mandat de reprise n’est pas donné et leurs prérequis validés. Ne pas confondre une correction documentaire demandée avec une autorisation de coder ou de publier.

Le prompt fourni est un mandat d’adoption, pas une autorisation implicite d’exécution. Une reprise ultérieure doit identifier la baseline du plan adoptée, maintenir l’historique et attribuer explicitement les réservations de chemins, contrats, projets et instance MCP/jeu.

## Vérification avant adoption

Depuis le paquet extrait, exécuter `python tools/validate_r14.py`, puis `python -m unittest discover -s tools -p "test_*.py"`. Pour contrôler le graphe fusionné en mémoire sur le dépôt réel : `python tools/validate_r14.py --baseline-root "CHEMIN_DU_DEPOT"`. Ce dernier contrôle lit les registres de définition, jamais `registry/state.json` ; il ne les modifie pas.

Relancer ensuite les validateurs documentaires/ordonnanceur propres au dépôt et mesurer les capsules sur les fichiers locaux. Pour T19, chaque agent lit le préambule commun et uniquement ses scénarios ; ne pas inclure les 24 cas entiers dans chaque capsule. Les tests techniques réels sont NOT_RUN dans cette livraison.

Rapport d’adoption attendu : baseline/diff, fichiers fusionnés, collisions, versions, DAG et politiques contrôlés, clauses historiques retirées de l’autorité active, requalifications requises, état des pauses inchangé et prochaine mission admissible sans la lancer automatiquement.
