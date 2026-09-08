# Vérification de la livraison ISRWorldGen 1.2
**8 septembre 2026 · portée : documentation et outils de préparation uniquement.**

## Structure contrôlée
Le plan comprend **19 lots, 61 sous-lots, 132 exigences, 132 scénarios principaux, 13 contrats et 6 gates**. Les 42 anciens identifiants sont conservés ; les 19 ajouts couvrent L05-D et les cinq lots spécialisés. Les 48 scénarios nouveaux s’ajoutent aux 84 historiques. Le corpus hérité garde 256 seeds, séparées entre calibration et holdout.

Le validateur a contrôlé unicité/références des IDs, acyclicité du DAG, propriété de chaque exigence/test, rattachement aux gates, présence des documents, liens relatifs et budgets des capsules. Résultat : PASS, sans erreur. Voir [rapport JSON](../artifacts/spec-validation.json).

Les capsules calculées sont comprises entre 21503 et 45075 octets UTF-8, donc sous 48 Kio (49 152 octets). Ce budget concerne la documentation explicitement chargée, pas les fichiers C# et preuves qui restent à consulter de façon ciblée. Le script Windows a été étendu pour accepter les suffixes D ; son motif est testé statiquement contre les 61 IDs.

## Outils testés
Les auto-tests du validateur couvrent le dossier cohérent, la source manquante, les cycles, les tests inconnus, les exigences sans propriétaire, le dépassement de contexte, les liens cassés, le corpus dupliqué et l’ignorance des sorties générées. Voir les sources [validate_spec.py](../tools/validate_spec.py) et [test_documentation_tools.py](../tools/test_documentation_tools.py).

Les **14 auto-tests de préparation de mise à jour** couvrent le dépôt actif inchangé, la conservation de l’état, les conflits Markdown, la sortie hors dépôt, les collisions d’ID et la séparation des preuves précoces/finales. Leur source est [test_plan_update.py](../tools/test_plan_update.py) ; les fixtures d’adoption sont synthétiques et ne décrivent pas le dépôt de l’utilisateur. Le script ne possède pas de mode d’application automatique.

## Ce qui n’a pas été exécuté
Aucun C# du mod n’a été compilé ou exécuté pour cette livraison. Aucune session Visual Studio/MCP ni partie Vintage Story n’a été pilotée. Les **48 nouveaux scénarios du mod restent NOT_RUN**. Les résultats historiques et états réels des 42 anciennes tâches ne sont pas connus ici ; ils doivent être repris du dépôt et conservés.

L’indication L05-C vient de l’utilisateur, pas d’une inspection du commit de développement. Elle ne prouve ni la fin de cette tâche ni la clôture de toutes les branches précédentes. Aucun `registry/state.json` actif n’est fourni dans l’archive.

Les scripts Python utilisent la bibliothèque standard et ont été exécutés ici ; le script PowerShell a été contrôlé statiquement mais pas exécuté dans PowerShell sur Windows. Les sources API publiques ont été consultées ; leur conformité avec les DLL locales reste à vérifier sur le poste de développement.

Les budgets existants et spécifiques aux distributions restent à qualifier/geler. Les avertissements du validateur sur ces points sont volontaires et ne constituent pas une validation du jeu.
