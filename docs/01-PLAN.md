# ISRWorldGen — Plan directeur révisé 1.4

**8 septembre 2026 · correction de L19 et maintien de L20 · version du plan distincte de la version du mod.**

## 1. Décision

L19 ne se limite plus à un dossier documentaire : **ISRWorldGen collecte, conserve et expose en lecture seule ses données de génération afin qu’un mod indépendant puisse les exploiter.** Le lot ne développe toujours aucun besoin, troupeau, chasse, navigation, alimentation ou système de population animale.

Les trois identifiants documentaires L19-A/B/C sont conservés. L19-D implémente le catalogue et sa persistance ; L19-E l’interface publique, la recherche spatiale et le diagnostic ; L19-F qualifie le tout avec un mod consommateur indépendant sur le jeu réel. Le contrat C13 décrit cette frontière technique.

L20 reste le chantier d’amélioration contextuelle des arbres, interne à ISRWorldGen et réalisé après la baseline 1.0.0 qualifiée. Le pack ISRTreeGen reste autonome et utilisable avec le jeu sans ISRWorldGen. L17 continue de posséder végétation/peuplements/placement natifs de la V1 ; les formes nouvelles de L20 ne deviennent pas un prérequis caché de L19 ou de la V1.

## 2. Reprise depuis l’état réel

Le dépôt public observé est `blackcancer/ISRWorldGen`, commit `fef55915fe9fccdfdc1c144dc4e76884eb462b83`. La révision 1.3 y a été ajoutée, un inventaire L19-A existe et L19-B porte une pause explicite dans l’état observé. Ce n’est pas un audit de l’installation locale ni une affirmation de clôture de tous les lots antérieurs. Références : `docs/future-fauna/inventaire.md`, `docs/01-PLAN.md` et commit d’ajout de `docs/15-CARTOGRAPHIE-DE-RECETTE.md`.

La présente livraison prépare le plan ; elle ne l’applique pas au dépôt et ne lève aucune pause. Conserver branches, worktrees, inventaire, états, worklogs et preuves. Ne pas revenir à L05-C : ce point n’était qu’un historique d’insertion de la révision 1.2. Une mission déjà DONE peut nécessiter une requalification ciblée 1.4, sans effacement de son ancien résultat.

Le travail déjà commencé dans L19-B doit être conservé et confronté au nouveau périmètre lors de sa reprise autorisée. Un changement d’exigence ne justifie ni reset global, ni remplacement de l’inventaire par un gabarit vide.

## 3. Catalogue complet

| Lot | Objet | Livrable de sortie |
|---|---|---|
| L00 | Environnement, API et preuve d’intégration | Cible locale, MCP débogable et spike terrain sûr |
| L01 | Cœur déterministe et contrats de base | Contrats C#, RNG, coordonnées et banc analytique |
| L02 | Atlas global et maillage Voronoï/Delaunay | Atlas borné, ownership et profils |
| L03 | Géologie, continents, relief initial et matériaux | Continents, couches et propriétés rocheuses |
| L04 | Climat, précipitations et bilan de l’eau | Température, précipitations, recharge et budgets |
| L05 | Bassins versants, lacs et réseau de drainage | Dépressions, drainage, débits et ports partagés |
| L06 | Évolution du relief et raffinement multi-échelle | Érosion, dépôts et raffinement conservatif |
| L07 | Cours d’eau, lacs, littoraux et océans détaillés | Lits, eau, lacs, côtes et deltas stables |
| L08 | Cavernes naturelles et hydrologie souterraine | Réseaux naturels, volumes et navigation |
| L09 | Sites fantastiques rares, connectés et explorables | Catalogue rare, raccords et certificats d’accès |
| L10 | Persistance, streaming, concurrence et sûreté | Manifeste, caches, ordonnanceur et reprise après crash |
| L11 | Adaptateur Vintage Story et compatibilité du monde | Colonnes natives, maps, contenu vanilla et serveur |
| L12 | Assets, configuration et outils de diagnostic | Assets, profils, aperçu et diagnostics |
| L13 | Qualification complète et distribution | Campagnes, performances, parcours et distribution V1 |
| L14 | Strates et roches | A catalogue ; B géologie 3D ; C matérialisation et métadonnées |
| L15 | Sols et fertilité | A profils ; B probe de placement ; C agriculture et conservation finales |
| L16 | Minerais et gisements | A potentiel ; B occurrences ; C placement et prospection ; D progression et persistance |
| L17 | Végétation native et peuplements | A habitats ; B peuplements ; C appels natifs ; D récoltes, plantations et saisons |
| L18 | Neige et glace | A bilan annuel ; B distribution initiale ; C gel-dégel ; D année complète et coûts |
| L19 | Socle de données environnementales pour les futurs mods | A inventaire ; B contrat/dictionnaire ; C passation ; D collecte/persistance ; E API/index ; F qualification externe — V1 |
| L20 | ISRTreeGen : amélioration contextuelle dans ISRWorldGen | A audit ; B adaptation ; C intégration ; D débogage ; E qualification — après V1.0.0 |

Sur la base exacte de 61 sous-lots en 1.2 : 72 sous-lots après adoption, dont 67 V1 et 5 post-V1. Par rapport à l’extension 1.3 de 69 sous-lots, seuls D/E/F sont ajoutés. Les exigences/scénarios de L19 passent de 8 à 24 ; les 12 de L20 sont conservés, soit 168 exigences et 168 scénarios théoriques avec la base 1.2 inchangée. Le registre local prévaut si d’autres travaux ont été ajoutés.

## 4. Contenu de L19

Le socle minimal fournit relief et pente de référence, plans d’eau/tronçons et secteurs de berge, habitats/couvert/peuplements, sols/fertilité de référence, climat/régime neige-glace et entrées/volumes naturels publiables. Il réutilise les producteurs existants et ne crée pas une seconde géographie.

Les formes exportées sont des candidats environnementaux, pas des garanties de ressource. Une zone herbacée n’est pas une quantité de nourriture ; une entrée de cave n’est pas un abri sûr ; une berge n’est pas un chemin d’abreuvement. Le futur mod détermine besoins, régime alimentaire, navigation et présence physique actuelle.

Le contrat distingue les décisions planifiées, l’état initial réellement généré et une observation datée lorsqu’un provider approprié existe. Le niveau « observation courante » n’est pas livré par simple lecture du catalogue. La couverture est indépendante de cette validité temporelle : une donnée peut être complète pour la génération tout en exigeant une vérification dans le monde courant.

Le catalogue est constitué même sans consommateur installé. Les métadonnées durables restent accessibles après déchargement et redémarrage, sans charger/générer de chunks pour répondre. Sur anciennes sauvegardes, reconstruire uniquement depuis des sources historiques exactes, ou signaler la lacune sans réécrire le terrain.

L’API publique décrit capacités/versions, contexte à une position, candidats dans un rayon et coverage. Les réponses sont immuables et bornées. Aucun scan global, zéro silencieux, tableau vide trompeur ou dépendance au chargement du monde n’est accepté comme substitut à cette interface.

## 5. Missions et dépendances

| Mission | Résultat attendu | Prérequis |
|---|---|---|
| L19-A | Inventorier les données et qualifier leur couverture | L05-D |
| L19-B | Définir le dictionnaire, le contrat public et les responsabilités | L19-A |
| L19-C | Revoir et transmettre le dossier sur le service réellement livré | L19-B, L11-C, L12-C, L19-E |
| L19-D | Implémenter la collecte, les reçus et la persistance environnementale | L19-B, L10-B, L11-B |
| L19-E | Implémenter l’API publique, l’index et le diagnostic bornés | L19-D, L02-B |
| L19-F | Qualifier le socle avec un consommateur externe et le monde réel | L19-C, L19-E, L10-C, L09-C |
| L13-A | Qualification globale V1, complétée par les preuves du socle | Préserver les prérequis existants et ajouter L19-F. |
| L20-A | Audit des arbres après la V1 | L13-C, L17-D et décision G5 qualifiée. |
| L20-B | Adaptation des formes en blocs | L20-A. |
| L20-C | Raccordement au placement natif | L20-B. |
| L20-D | Diagnostic reproductible des arbres | L20-C. |
| L20-E | Qualification après V1 | L20-C, L20-D. |

**Ordre de L19 : A → B → D → E → C → F.** L19-C conserve son rôle de passation documentaire finale ; sa nouvelle dépendance à E évite de décrire une API seulement projetée. D n’attend pas C, et aucun producteur amont n’attend la clôture de L19. Ces relations sont à fusionner dans le DAG réel, sans retirer des dépendances locales supplémentaires.

Les amendements documentaires C13 sont approuvés en B par l’intégrateur. Les changements C# du contrat et les projets sont implémentés en E sous mandat réservé. D peut d’abord travailler sur sa projection interne et le stockage, sans dépendre d’une interface E déjà compilée. Les éventuelles références publiques nécessaires à D sont introduites par l’intégrateur à partir du contrat gelé ; cela ne change pas l’ordre des qualifications.

## 6. Insertion dans la feuille de route

| Phase | Travail | Condition de sortie |
|---|---|---|
| Adoption documentaire | Diff 1.3→1.4, retrait des exclusions erronées, définition des nouvelles tâches et plan de requalification. | Aucun état écrasé, pause préservée, DAG/schémas/capsules vérifiés. |
| Transverse précoce, après reprise autorisée | Requalification ciblée de A puis B, en parallèle des lots indépendants existants. | Inventaire utile, C13 et bornes approuvés. |
| Production des distributions | Poursuivre L06–L18 selon le DAG déjà adopté. | L11-B et L10-B livrent les entrées de D. |
| Support technique | D puis E ; commencer les tranches indépendantes dès leurs prérequis réels, sans mocks définitifs. | Données persistantes et API de consultation opérationnelles. |
| Fin de fonctions V1 | L11-C/L12-C/L10-C/L09-C et C, puis F. | Consommateur externe et essais monde/serveur validés. |
| Recette/distribution | L13-A/B/C puis G5. | Socle environnemental et fonctions existantes qualifiés sur la même baseline. |
| Après 1.0.0 | L20-A/B/C/D/E puis G6. | Arbres contextuels qualifiés sans dépendance du pack vers ISRWorldGen. |

Ne pas traiter les numéros comme une séquence aveugle. L14/L18 restent prérequis des bilans/érosion concernés ; minerais, cavernes, sols et plantations conservent leurs dépendances de la révision 1.2. Les tâches de L19 n’autorisent pas une réécriture globale des producteurs. Un hook manquant donne lieu à une modification ciblée, réservée et testée chez son propriétaire.

## 7. Portes de validation

Les critères G0–G3 sont conservés. Les listes ci-dessous sont la base normative historique à fusionner avec les ajouts locaux, non un moyen d’effacer des obligations déjà adoptées.

### G0 — Environnement et intégration prouvés

Version locale verrouillée, breakpoint réel, remplacement sélectif, fluides et heightmaps prouvés.

Tâches : L00-A, L00-B, L00-C.


### G1 — Socle reproductible et atlas borné

Contrats gelés, atlas borné, stockage abstrait et premiers codecs natifs éprouvés.

Tâches : L01-A, L01-B, L01-C, L02-A, L02-B, L02-C, L10-A, L11-A.


### G2 — Bassin complet des sources à la mer

Bassin des sources à la mer, bilans et raffinement conservés, roches 3D et agrégat neige-fonte cohérents. Ce stade reste un prototype interne.

Tâches : L03-A, L03-B, L03-C, L04-A, L04-B, L04-C, L05-A, L05-B, L05-C, L06-A, L06-B, L06-C, L07-A, L07-B, L07-C, L10-B, L05-D, L14-A, L14-B, L14-C, L18-A.


### G3 — Sous-sol et catalogue intégrés

Sous-sol, cinq familles fantastiques et colonne complète raccordés, avec les cinq distributions. Les cycles complets restent à qualifier à G4.

Tâches : L08-A, L08-B, L08-C, L09-A, L09-B, L12-A, L11-B, L15-A, L15-B, L16-A, L16-B, L16-C, L17-A, L17-B, L17-C, L18-B, L18-C.


### G4 — Fonctions V1 et socle environnemental réellement raccordés

Les accès, sauvegardes, contenu vanilla, configuration, distributions et cycles ont leurs preuves. Ajouter L19-A à F : collecte persistante, catalogue, API publique et validation par consommateur indépendant. Aucun besoin animalier n’est requis.

Tâches : L09-C, L10-C, L11-C, L12-B, L12-C, L15-C, L16-D, L17-D, L18-D, L19-A, L19-B, L19-C, L19-D, L19-E, L19-F.

### G5 — ISRWorldGen 1.0.0 qualifié et distribuable

L13-A/B/C reprend la baseline finale, le corpus historique, les contrôles bloquants et les régressions L19 pertinentes. Un dossier descriptif, une API compilée non consommée ou un UNKNOWN systématique ne suffisent pas. Les scénarios T20 sont exclus ; la V1 n’attend pas le pack ni l’algorithme avancé de L20.

Tâches : L13-A, L13-B, L13-C. L13-A conserve ses dépendances et exige L19-F ; son ancienne dépendance directe L19-C peut rester, désormais redondante.

### G6 — Arbres contextuels qualifiés après 1.0.0

Après G5 qualifiée sur une baseline 1.0.0 identifiée : adaptation berge/lisière/versant et obstacles, formes en blocs, protections, diagnostic reproductible, compatibilité avec/sans le pack et campagne visuelle/performance. L20 ne crée ni racines détaillées ni maillages de branches. L20-A audite les outils natifs avant de compléter les capacités absentes, sans supposer l’existence de `/gentree` ni l’absence de tout outil arbre.

Tâches : L20-A à E. G6 exige G5. Aucune tâche V1 ne dépend de L20 ou de G6.

## 8. Recette et preuve finale

Les 24 scénarios T19 comprennent 8 contrôles documentaires révisés, 5 tests collecte/stockage D, 6 API/index/diagnostic E et 5 qualifications externes F. Les cas techniques incluent toujours un témoin défectueux : identité instable, faux état actuel, mauvais classement spatial, corruption, mauvais packaging ou génération de chunks cachée.

La preuve décisive est un petit mod de recette externe, installé après génération du monde et utilisant seulement le contrat public. Il doit accéder aux familles minimales sur des fixtures réelles, après déchargement et après redémarrage, sans reconstruire le monde ni s’appuyer sur les classes internes. Ce mod n’est pas distribué comme fonctionnalité animale.

Conserver les nouvelles règles de cartes de diagnostic : sorties spatiales pertinentes avec PNG, manifeste de provenance, palette gelée, emprise/unités, hash et oracle numérique. Une image n’est jamais seule un PASS. Les tests scalaires/API sans résultat spatial n’ont pas à produire d’image artificielle.

Les budgets structurels ont des valeurs initiales dans S19, à qualifier et geler avant les essais d’acceptation. Les mesures CPU/mémoire/disque et la latence ne sont pas annoncées comme acquises. Les tests natifs doivent utiliser les assemblages et le runtime installés ; les sources publiques ne sont pas une preuve de compatibilité locale.

## 9. Orchestration et adoption

Terra Medium conserve l’orchestration et le développement courant lorsqu’il est disponible. Expertise ciblée pour les algorithmes spatiaux, l’ABI, la concurrence et la reprise ; relecteur distinct de l’auteur ; deux implémentations simultanées au plus sur périmètres disjoints, un seul détenteur de la session MCP/jeu. Les noms de modèles sont des profils souhaités, pas une garantie de capacité exposée par l’installation.

Le package ne contient pas d’état actif ni de code C# du mod. `extension-r14.json` est un delta déclaratif de plan à traduire dans les schémas réellement utilisés. Le validateur fourni lit les définitions mais ne les fusionne pas dans le dépôt. Les anciens outils `prepare_r13.py` ne doivent plus servir à cette correction : leur règle « L19 documentaire seulement » est obsolète.

La procédure détaillée figure dans `docs/16-INTEGRATION-L19-R14.md`. Les clauses 1.3 incompatibles sont remplacées, pas conservées côte à côte comme deux autorités. Les contenus L20 inchangés gardent leur révision de contenu 1.3 ; la politique globale de cette livraison est 1.4. Une adoption documentaire ne lève pas la pause L19-B et n’autorise pas l’exécution de D/E/F sans mandat de reprise.
