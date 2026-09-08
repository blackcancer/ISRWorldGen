# Adopter le plan 1.2 depuis L05-C
**Date : 8 septembre 2026. Information utilisateur : développement à L05-C. Pas d’audit du dépôt actif dans cette livraison.**

## Ce qui est conservé
Les 42 identifiants L00-A à L13-C restent en place, avec leurs exigences historiques. L05-C garde ses dépendances L05-B/L02-C, son mandat « exutoires et budgets », sa fiche et ses tests initiaux. Aucune branche parallèle n’est supposée terminée. Le mod conserve C#, Visual Studio Community 2026, le template installé et le débogage MCP.

Les noms des projets/répertoires réellement présents sont conservés. WorldGen dans les chemins techniques de l’ancien plan reste un alias à mapper vers ISRWorldGen, pas une demande de renommage. Le produit est ISRWorldGen ; l’identifiant natif déjà choisi doit être relevé, pas changé sans migration.

## Transition immédiate
Laisser L05-C terminer sa mission actuelle si elle est bien en cours ; conserver un checkpoint/commit et les résultats T05-05/06. Le simple message « au lot L05-C » n’autorise pas à le passer DONE. Préparer en parallèle uniquement la lecture et l’inventaire documentaire, sans changer son contrat pendant l’exécution.

Après cette clôture, L05-D fusionne le plan et classe les impacts. L14-A (catalogue rocheux) et L18-A (climat/neige/eau) deviennent les premières branches nouvelles lorsque leurs prérequis réels sont conformes. L14-B et L18-A débloquent l’érosion L06-A et les cavernes L08-A. Les tâches indépendantes déjà prêtes sur stockage/outillage peuvent continuer. Aucun retour automatique à L00-A.

## Fusion sans perte
Extraire l’archive à côté du dépôt. Le script `tools/prepare_plan_update.py --existing CHEMIN_DU_DEPOT --output DOSSIER_DE_REVUE` effectue une comparaison et crée des propositions uniquement dans le dossier de revue. Ce dernier doit être hors du dépôt actif et hors de cette livraison. Il ne fusionne, ne commite et ne remplace aucun fichier actif.

Les fichiers inchangés depuis la référence 1.1 peuvent être proposés comme mise à jour simple. Toute divergence locale nécessite revue. Les registries de définitions sont fusionnés par ID ; les dépendances nouvelles sont ajoutées, pas substituées aux dépendances locales. Les chemins/contrats modifiés localement ne sont pas écrasés. Les conflits de champs restent explicitement ouverts dans le rapport.

Le script préserve chaque entrée et champ du `registry/state.json` local dans un candidat, puis ajoute seulement les nouvelles tâches en BACKLOG. Des anciens IDs absents de l’état restent UNVERIFIED ; une tâche locale inconnue est conservée. Aucun état ni résultat historique ne devient PASS/DONE par effet du plan. Sans registre local, les résultats de préparation sont non certifiants et l’intégrateur reconstruit l’historique depuis les preuves.

L’intégrateur adopte ensuite le diff relu et valide la cohérence des fichiers effectivement fusionnés. Ne pas copier aveuglément l’arbre `proposal/` lorsque le rapport contient des conflits. L’archive ne contient volontairement pas `registry/state.json` ni de worklogs historiques. `registry/state.template.json` est uniquement un modèle documentaire.

## Compatibilité des contrats
Comparer C01/C02/C03 et snapshots actuels à C08-C12. Classer chaque changement :
- **Conforme** : mêmes unités/sorties, preuve ancienne pertinente et régression réussie ; réutiliser.
- **Extension compatible** : sidecar/champ optionnel versionné sans modifier l’ancien résultat ; nouvelle preuve ciblée.
- **Rupture** : changement d’unités, formats, géologie, bilan annuel ou seed ; nouvelle révision, régénération seulement de nouveaux mondes jetables, tests consommateurs à refaire.

Le résultat L05-C est le budget annuel de référence. L18-A doit y raccorder un régime de stockage/fonte conservatif. Si la nouvelle géologie change infiltration ou l’érosion, la publication commune climat/hydrologie/relief doit être recalculée avant nouveaux blocs ; pas de maintien artificiel d’un résultat incompatible. Un ancien monde déjà joué n’est jamais modifié pour satisfaire les nouveaux réglages.

## Requalification sans effacer l’historique
`registry/requalification-r12.json` décrit les zones d’impact à remplir localement. Les décisions possibles sont REUSE_WITH_EVIDENCE, EXTEND_AND_RETEST, BREAKING_NEW_REVISION et BLOCKED. TO_ASSESS n’est pas une validation. Les gates 1.0/1.1 restent archivées avec leur version ; une gate 1.2 exige ses clauses supplémentaires.

Les nouveaux probes sont délibérément séparés des campagnes intégrées : L15-B prouve les sols sur fixture native via T15-08 ; L17-C prouve les placements via T17-10. Les contrôles finaux sol forestier, minerai après cavernes, récoltes et saisons attendent les lots d’intégration correspondants. Ne pas accepter une tâche avec un test obligatoire encore NOT_RUN sous prétexte qu’il est « prévu plus tard ».

## Première décision de l’orchestrateur
Lire le commit, l’état et le verrou MCP, puis identifier L05-C en cours ou réellement accepté. Ne demander un arbitrage utilisateur que pour une rupture de périmètre ou décision non réversible non autorisée ; les compléments et requalifications du plan sont déjà mandatés. Toute limite d’outil reste explicite. Le script de préparation ne prouve pas l’exécution du jeu.
