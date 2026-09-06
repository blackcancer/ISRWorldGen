# Plan de développement et jalons
## Ordre de travail
Le chemin de lancement est **L00-A → L00-B → L00-C** pour prouver la cible et l’intégration ; le cœur indépendant peut avancer en parallèle dès que sa cible de build est connue. Ensuite, les sous-lots suivent leurs dépendances, pas nécessairement leur numéro. Une gate est une porte de validation, pas une interdiction de préparer une tâche indépendante d’une branche ultérieure.

Ne pas attendre la fin de toute la géologie pour découvrir que l’eau ou les cartes ne fonctionnent pas dans le moteur. L11-A utilise des snapshots synthétiques dès le socle ; L07-C vérifie tôt les fluides réels. Le premier démonstrateur géographique significatif doit couvrir un **bassin entier, des sources jusqu’à l’océan**, et non une belle tuile isolée.

## Lots
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

## Portes de validation

### G0 — Environnement et intégration prouvés
Version locale verrouillée, breakpoint réel MCP, preuve de remplacement sélectif et fluides/heightmaps. Sans cela, aucune prétention de compatibilité au jeu.

Sous-lots exigés : L00-A, L00-B, L00-C. Les gates antérieures doivent également être closes avant validation de cette gate.

### G1 — Socle reproductible et atlas borné
Contrats gelés, atlas échantillonné, quotas, stockage abstrait et premiers codecs natifs éprouvés.

Sous-lots exigés : L01-A, L01-B, L01-C, L02-A, L02-B, L02-C, L10-A, L11-A. Les gates antérieures doivent également être closes avant validation de cette gate.

### G2 — Bassin complet des sources à la mer
Bilan et raffinement conservés, profils fluviaux corrects et eau stable sur le moteur réel. Prototype interne de surface, pas V1.

Sous-lots exigés : L03-A, L03-B, L03-C, L04-A, L04-B, L04-C, L05-A, L05-B, L05-C, L06-A, L06-B, L06-C, L07-A, L07-B, L07-C, L10-B. Les gates antérieures doivent également être closes avant validation de cette gate.

### G3 — Sous-sol et catalogue intégrés
Réseaux naturels, cinq familles fantastiques matérialisées et colonne complète ; certification finale reste à G4.

Sous-lots exigés : L08-A, L08-B, L08-C, L09-A, L09-B, L12-A, L11-B. Les gates antérieures doivent également être closes avant validation de cette gate.

### G4 — Toutes les fonctions prévues raccordées
Accès finaux certifiés, reprise après crash, contenu vanilla, configuration et diagnostics prêts. Candidat de recette, non encore version validée.

Sous-lots exigés : L09-C, L10-C, L11-C, L12-B, L12-C. Les gates antérieures doivent également être closes avant validation de cette gate.

### G5 — V1 qualifiée et distribuable
Corpus, revue, endurance, jeu/serveur et installation propre validés. Tous les tests bloquants effectivement exécutés.

Sous-lots exigés : L13-A, L13-B, L13-C. Les gates antérieures doivent également être closes avant validation de cette gate.

## Parallélisation conseillée
Après L00-A, L00-B et L01-A peuvent avancer en parallèle sans partager une session de debug. Après atlas/géologie initiale, le climat et l’analyse des dépressions peuvent avancer séparément ; l’accumulation des débits attend leur jonction. Après les ports hydrologiques, réseaux de cavernes et érosion de surface sont en partie indépendants. Le stockage abstrait peut avancer dès les contrats, sans attendre toutes les formes de terrain.

Après la description des merveilles, l’agent assets et l’agent assemblage des colonnes peuvent travailler séparément. La validation finale d’accès attend les assets et les passes de structures réelles. Les performances finales attendent l’intégration complète, mais les compteurs et limites sont en place dès les premiers modules.

Le registre encode le DAG exact, vérifié par le validateur. Ne pas lancer deux tâches prêtes si leurs chemins d’écriture, fichiers de projet, déploiement ou session MCP se chevauchent. Les tests dépendant d’un composant encore absent restent NOT_RUN, même si le sous-lot a commencé par un mock.

## Contrôle de portée
La V1 inclut tous les lots. Les premières gates ne sont que des prototypes internes. Des améliorations futures pourront ajouter hydraulique dynamique des barrages, modèles d’érosion plus coûteux, thèmes fantastiques supplémentaires ou génération sur d’autres plateformes ; elles ne servent pas à reporter les garanties de connexion, continuité, persistance ou survie déjà dans V1.

Chaque modification du plan indique exigences et tests concernés, impact de schéma/seed et besoin de refaire une campagne. Aucune estimation calendaire n’est imposée avant les probes et premiers benchmarks.
