# Vérification de cette livraison documentaire
**Dossier v1.0 · 6 septembre 2026 · portée : documentation et outils de lecture uniquement.**

## Résultat vérifié
Le dossier contient **14 lots, 42 sous-lots délégables, 84 exigences, 84 scénarios principaux de test, 8 contrats de données et 6 portes de validation**. Le registre de tâches est relié aux exigences, scénarios et dépendances. Le corpus contient 256 seeds distinctes, avec une partition calibration/holdout sans recouvrement.

Le validateur documentaire a été exécuté avec résultat **PASS** : identifiants, liens internes, présence des définitions, couverture exigences/tâches/tests, dépendances sans cycle, budget de lecture et partition du corpus. Un contrôle supplémentaire confirme que chaque tâche référence sa propre fiche et que ses tests couvrent les exigences qui lui sont affectées.

Les **8 auto-tests** de l’outil de validation ont été exécutés et réussis. Ils contrôlent le cas conforme puis plusieurs défauts injectés : source manquante, cycle de dépendance, test inconnu, exigence sans tâche responsable, capsule trop volumineuse, lien cassé et seed dupliquée. Le rapport brut est conservé, pas remplacé par une appréciation de l’auteur.

## Lecture ciblée mesurée
Les capsules déclarées incluent **6 à 9 fichiers sources** selon la tâche. Leur taille UTF-8 mesurée est comprise entre **20.9 et 33.3 Kio**, avec une médiane de **23.8 Kio**, en dessous du plafond de 48 Kio. Ces nombres concernent la documentation obligatoire seulement : le code, les sources API ciblées et les preuves nécessaires à une mission peuvent ajouter du contexte. Ce n’est pas une mesure en tokens.

Trois capsules d’exemple sont fournies dans `artifacts/contexts/` : L00-A, L05-B et L09-C. Elles sont produites par l’outil Python équivalent ; le script PowerShell fourni n’a pas été exécuté dans l’environnement de préparation. Les générer à nouveau après toute modification documentaire, car les hashes inclus correspondent aux fichiers de cette livraison.

## Ce qui n’a pas été testé ici
Aucun mod n’a été implémenté, compilé, chargé ou débogué dans Vintage Story dans cette livraison. Le poste Visual Studio Community 2026 de l’utilisateur et son MCP n’ont pas été pilotés. **Les 84 scénarios du mod sont tous NOT_RUN et les 42 tâches sont toutes BACKLOG.** La seule tâche prête selon les dépendances initiales est L00-A.

Les chiffres de performance, dimensions et rareté restent des propositions à qualifier et à geler ; ils ne sont ni des mesures du mod ni des garanties obtenues. La première étape vérifie l’installation réelle du jeu et le template avant de choisir la cible .NET et les références.

## Preuves et reproduction
[Rapport du validateur](../artifacts/spec-validation.json) · [Sortie des auto-tests](../artifacts/documentation-tool-tests.txt) · [Utilisation des outils](../tools/README.md).

`SHA256SUMS.txt` inventorie les fichiers livrés, hors sa propre empreinte. L’archive est vérifiée pour son intégrité après création. Ne pas interpréter cette vérification d’archive comme une validation des fonctionnalités futures du jeu.
