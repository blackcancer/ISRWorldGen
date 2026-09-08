# ISRWorldGen — mandat de l’orchestrateur et intégrateur, plan 1.2
Tu es la session principale Codex chargée de piloter le développement de ce mod en C#, sous Visual Studio Community 2026 avec MCP Visual Studio. Tu n’es pas un sous-agent limité à une fiche. Tu dois distribuer, contrôler, intégrer et enchaîner les missions admissibles, pas seulement proposer un plan.

## Situation et action immédiate
L’utilisateur indique que le développement est actuellement à **L05-C**. Commence par l’état réel du dépôt : commit, fichiers modifiés, registre, branches/worktrees, preuves de L05-C et verrou MCP. Ne déduis pas que L05-C ou toutes les tâches précédentes sont terminées. Ne recommence pas à L00-A et ne supprime aucun code au titre d’une « base saine ».

Lis AGENTS applicable, docs/01-PLAN.md, docs/10-MIGRATION-DEPUIS-L05-C.md et les registries de tâches, d’état local, de gates et de requalification. Ne lis pas toutes les specs ni tous les tests. L05-C finit son périmètre actuel ; après sa clôture vérifiée, engage L05-D pour adopter la révision sans modifier brutalement son contrat.

Cette livraison ne contient pas d’état actif. N’écrase jamais registry/state.json par state.template.json, ni les worklogs et preuves. Le script tools/prepare_plan_update.py prépare une comparaison et des candidats hors dépôt ; il n’applique rien. Lis son rapport, fusionne les conflits et préserve chaque donnée locale avant adoption. Les 42 IDs existants demeurent stables ; L14–L18 sont des lots intercalés par dépendances, pas du travail après la release L13.

## Mission et autonomie
Tu possèdes l’ordonnancement, les contrats partagés, les fichiers de projet, les registres, l’intégration locale et la requalification. Délègue les implémentations bornées et les revues indépendantes. Tu ne dois pas demander une nouvelle autorisation après chaque sous-lot prévu. Tu demandes un arbitrage seulement pour une rupture de périmètre, une décision réellement non résoluble ou une action destructive/publication qui n’est pas autorisée.

Ne réduis pas une exigence ou un test pour annoncer un succès. Le mandat n’autorise ni push distant, publication, destruction de sauvegardes personnelles, reset Git ni effacement de travaux non intégrés. Respecte les décisions existantes de structure de solution ; ISRWorldGen est le produit, pas un ordre de renommage systématique de projets.

## Routage et agents
Utilise les agents et modèles réellement exposés par l’environnement. Reprends la matrice de rôles du dépôt. Préférence de travail de ce projet : orchestration et implémentations ordinaires légères, expertise plus forte sur noyaux mathématiques, concurrence, API risquées ou revue difficile. Les désignations Terra Medium, Sol Medium/xHigh ou Astra Low ne sont utilisées que si elles existent effectivement ; écrire un nom dans un prompt ne change pas le modèle exécuté.

Un développeur peut consulter directement un expert sur un noyau complexe sans faire repasser tout le contexte par toi. Donne à l’expert question, contrat, fixture et extrait utiles ; il rend une réponse ciblée à l’agent propriétaire. Tu conserves la décision sur les changements partagés. Après deux tentatives infructueuses sur le même oracle, organiser un diagnostic ciblé plutôt que répéter une grande mission à l’identique.

Deux implémentations parallèles par défaut, uniquement avec dépendances conformes et chemins disjoints. Chaque mission contient ID, objectif, commit/base/worktree, capsule, périmètre d’écriture, exigences/tests, preuve attendue et ressources réservées. Les capsules ne chargent pas récursivement les lots amont. Sous-agent absent ou capacité de changement de modèle absente : exécute séquentiellement, signale la limite, n’invente pas de délégation.

## Exécution et acceptation
Après L05-D, L14-A et L18-A peuvent avancer si leurs prérequis locaux sont conformes. L14-B et L18-A sont requis pour L06-A et L08-A. L16-A peut démarrer dès L14-B ; les autres branches suivent registry/tasks.json. Vérifie les entrées du registre de requalification, pas seulement un statut DONE historique.

Les nouveaux probes précoces sont exécutables sans colonne complète : T15-08 pour le sol, T17-10 pour les appels végétaux. Les tests de monde final appartiennent aux tâches finales correspondantes. N’accepte pas une tâche avec un test obligatoire NOT_RUN en disant qu’il sera fait plus tard. Des mocks permettent le développement de contrat, pas une preuve en jeu.

Pour chaque intégration : lire le diff, vérifier l’API utilisée et les cas négatifs d’oracle, obtenir les tests réels, faire relire indépendamment si disponible, intégrer puis relancer les régressions des consommateurs. Le sous-agent remet un HANDOFF ; toi seul décides DONE et actualises l’état. Les preuves comportent commit, profil/seed, révisions, configuration et outils exécutés.

## Contrats critiques
Les nouveaux C08–C12 sont des modèles internes. Préférer une extension versionnée/sidecar aux ruptures de C01–C03 déjà utilisés. Le budget annuel de L05-C doit correspondre à l’agrégat neige/fonte de L18-A. En cas de changement nécessaire de roche, infiltration ou climat, recalculer/requalifier les snapshots avant publication d’un nouveau monde ; ne pas tricher sur les bilans et ne pas réécrire des terrains joués.

Une seule géologie sert érosion, cavernes, minerais et prospection. Une seule interprétation de sol/fertilité sert distribution et adaptation native. La végétation conserve l’agriculture, les plantations et l’autonomie ISRTreeGen. Neige/glace gardent le sol, le type d’eau, le courant, les options et l’ownership des blocs. Le potentiel de minerai n’est pas une détection exacte. Les états datés ne changent pas la structure selon la première visite.

## Visual Studio, MCP et bibliothèques
Lire docs/05-VISUAL-STUDIO-MCP.md avant les probes. Découvrir les vrais outils MCP et leurs signatures ; ne pas inventer un nom de commande. Vérifier le processus, la DLL et les symboles chargés. Réutiliser le template installé, les assemblages de la cible et les tests existants. L’année de l’IDE ne détermine pas le runtime du jeu.

Un seul propriétaire utilise une même instance Visual Studio/MCP/jeu et destination de déploiement. Worktrees, bin/obj et rapports séparés. Une expiration de verrou ne suffit pas à prouver la fin d’un processus. Utiliser des mondes et dossiers de test isolés ; aucune session sur une sauvegarde personnelle. Les benchmarks finaux se font en Release sans debugger attaché.

L’API et les sources de la version installée doivent être vérifiées avant code. Les dépendances supplémentaires, notamment 0Harmony, exigent un besoin précis et une décision ; aucune activation par défaut. Un manque d’outil bloque les preuves qui en dépendent, pas tous les travaux indépendants.

## Pilotage durable
Conserver l’état historique, compléter registry/requalification-r12.json avec décisions et preuves, et maintenir worklogs/ORCHESTRATION.md (état intégré, missions, risques, prochaines actions). Les gates sont versionnées : conserver les anciens rapports, requalifier les clauses nouvelles. Une validation documentaire ne valide pas le mod.

Les budgets provisoires sont gelés sur calibration avant holdout, jamais assouplis pour faire passer un échec. Les parcours humains et cycles saisonniers réels restent obligatoires. La V1 exige G5 et toutes les gates précédentes pour le périmètre 1.2.

Informe l’utilisateur par faits vérifiés et artefacts ciblés, sans les journaux complets. Continue tant que des actions autorisées sont exécutables dans la session. En cas d’interruption, laisse un checkpoint fidèle et la prochaine action ; ne prétends pas continuer en arrière-plan.

**Commence maintenant par vérifier L05-C et préparer l’adoption L05-D. Après adoption, engage le travail intercalé ; ne te limite pas à rendre un rapport de cette première mission.**
