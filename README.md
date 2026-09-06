# Générateur de mondes Vintage Story — Cahier des charges modulaire
**Version documentaire 1.0 · 6 septembre 2026 · C# · Visual Studio Community 2026 · Codex + MCP Visual Studio**

Ce dossier est une spécification de développement, pas un mod déjà implémenté. Le nom technique provisoire est `WorldGen` et l’identifiant provisoire `worldgen` ; leur gel fait partie de L00-A. Aucun dépôt n’a été modifié et aucun test du jeu n’est déclaré réussi dans cette livraison.

## Commencer sans tout lire
Pour une première lecture humaine, ouvrir [la synthèse](docs/00-SYNTHESE.md), puis [le plan et les jalons](docs/01-PLAN.md). Pour un agent, lire [AGENTS.md](AGENTS.md), choisir **une seule tâche** dans [l’index](tasks/INDEX.md), puis les seuls fichiers indiqués dans sa rubrique « Lectures obligatoires ». L00-A est la première tâche.

[Le socle invariant](docs/02-SOCLE.md) définit les règles communes. Les fiches `specs/` portent le comportement attendu ; `contracts/` porte les échanges entre modules ; `tests/` porte les oracles et critères d’acceptation. Un lot est une unité de livraison ; une tâche A/B/C est un sous-lot délégable à un agent. Le mot « chunk » sans qualificatif désigne uniquement les blocs de terrain Vintage Story.

## Routes utiles
| Besoin | Entrée |
|---|---|
| Comprendre les choix et les limites | [Synthèse](docs/00-SYNTHESE.md) |
| Ordonner le développement et les équipes | [Plan](docs/01-PLAN.md) et [orchestration](docs/03-ORCHESTRATION.md) |
| Exécuter une tâche isolée | [Index des tâches](tasks/INDEX.md) |
| Vérifier l’intégration réelle au jeu | [Audit API](docs/04-AUDIT-API.md) |
| Déboguer avec le MCP | [Procédure Visual Studio/MCP](docs/05-VISUAL-STUDIO-MCP.md) |
| Vérifier la couverture des besoins | [Traçabilité](docs/06-TRACABILITE.md) |
| Préparer une version distribuable | [Recette et jalons](tests/00-RECETTE.md) |
| Reprendre après interruption | [État machine](registry/state.json) et [modèle de passation](templates/HANDOFF.md) |

## Capsule de contexte
Sous Windows, depuis ce dossier :
```powershell
powershell -NoProfile -File .\tools\Get-TaskContext.ps1 -TaskId L00-A
```
Le script écrit un paquet de lecture local dans `artifacts/contexts/`. Il agrège uniquement les entrées déclarées, donne leur empreinte et refuse une capsule dépassant son budget : il ne tronque aucune obligation. Il ne lance pas Codex, le jeu ou Visual Studio. Lecture manuelle équivalente possible. Le validateur Python facultatif est fourni pour vérifier liens, identifiants et dépendances ; Python n’est pas une dépendance du mod.

## Utilisation dans un dépôt existant
Copier `docs`, `specs`, `contracts`, `tasks`, `tests` documentaires, `registry`, `templates`, `prompts`, `tools` et `AGENTS.md` à la racine de travail choisie. Fusionner les instructions si un `AGENTS.md` existe : ne jamais l’écraser sans lecture. Les répertoires de code décrits sont à créer lors du développement ; l’arborescence Visual Studio existante reste la référence tant que L00-A ne l’a pas auditée.

Le fichier [SOURCES](docs/07-SOURCES.md) sépare les éléments vérifiés dans l’API des choix propres au projet. Le framework .NET, la version locale du jeu, les capacités du MCP et les seuils de performance ne sont pas inventés : leur qualification constitue une étape explicite du plan.

## Vérification de la livraison
Les [résultats des contrôles documentaires](docs/09-VERIFICATION-DU-DOSSIER.md) distinguent les vérifications effectuées ici des tests du mod restant à implémenter et exécuter.
