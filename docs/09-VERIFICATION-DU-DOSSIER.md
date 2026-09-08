# Vérification de la livraison ISRWorldGen 1.3
**8 septembre 2026 · portée : documentation et outils de préparation uniquement.**

## Structure contrôlée
Le plan comprend **21 lots, 69 sous-lots, 152 exigences, 152 scénarios principaux, 13 contrats et 7 gates**. Les identifiants historiques sont conservés ; L19 apporte une passation documentaire V1 et L20 un chantier TreeGen explicitement Post‑V1. Le corpus hérité garde 256 seeds, séparées entre calibration et holdout.

Le validateur contrôle unicité/références des IDs, acyclicité du DAG, propriété de chaque exigence/test, rattachement aux gates, présence des documents, liens relatifs, budgets des capsules et, en 1.3, séparation V1/Post‑V1 (`requires_gates`, exclusion L20/T20 de G5). Le résultat courant doit être régénéré après chaque adoption : voir [rapport JSON](../artifacts/spec-validation.json).

Les capsules doivent rester sous 48 Kio (49 152 octets). Ce budget concerne la documentation explicitement chargée, pas les fichiers C# et preuves qui restent à consulter de façon ciblée. Le script Windows accepte les suffixes D et refuse par défaut une capsule Post‑V1 sans déclaration explicite de gate qualifiée.

## Outils testés
Les auto-tests du validateur couvrent le dossier cohérent, la source manquante, les cycles, les tests inconnus, les exigences sans propriétaire, le dépassement de contexte, les liens cassés, le corpus dupliqué, l’ignorance des sorties générées et les violations de périmètre G5/Post‑V1. Voir les sources [validate_spec.py](../tools/validate_spec.py) et [test_documentation_tools.py](../tools/test_documentation_tools.py).

Les **14 auto-tests de préparation de mise à jour** couvrent le dépôt actif inchangé, la conservation de l’état, les conflits Markdown, la sortie hors dépôt, les collisions d’ID et la séparation des preuves précoces/finales. Leur source est [test_plan_update.py](../tools/test_plan_update.py) ; les fixtures d’adoption sont synthétiques et ne décrivent pas le dépôt de l’utilisateur. Le script ne possède pas de mode d’application automatique.

## Ce qui n’a pas été exécuté
Aucun C# du mod n’a été compilé ou exécuté pour cette livraison. Aucune session Visual Studio/MCP ni partie Vintage Story n’a été pilotée. Les **48 nouveaux scénarios du mod restent NOT_RUN**. Les résultats historiques et états réels des 42 anciennes tâches ne sont pas connus ici ; ils doivent être repris du dépôt et conservés.

L’indication L05-C vient de l’utilisateur, pas d’une inspection du commit de développement. Elle ne prouve ni la fin de cette tâche ni la clôture de toutes les branches précédentes. Aucun `registry/state.json` actif n’est fourni dans l’archive.

Les scripts Python utilisent la bibliothèque standard et ont été exécutés ici ; le script PowerShell a été contrôlé statiquement mais pas exécuté dans PowerShell sur Windows. Les sources API publiques ont été consultées ; leur conformité avec les DLL locales reste à vérifier sur le poste de développement.

Les budgets existants et spécifiques aux distributions restent à qualifier/geler. Les avertissements du validateur sur ces points sont volontaires et ne constituent pas une validation du jeu.
