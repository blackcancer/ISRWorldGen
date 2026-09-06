# Instructions de travail — WorldGen
## Mission et lecture minimale
Développer en C# un générateur Vintage Story fondé sur un atlas géologique et hydrologique cohérent, avec de rares sites souterrains fantastiques **toujours reliés à une galerie praticable**. Visual Studio Community 2026 est l’IDE ; un MCP Visual Studio sera accessible à Codex.

Ne pas lire tout le cahier des charges. Lire `docs/02-SOCLE.md`, la fiche de la tâche demandée et ses seules lectures obligatoires. Consulter les sources pertinentes seulement si un symbole/API ou un algorithme doit être implémenté/vérifié. `registry/tasks.json` est le manifeste de routage ; `registry/state.json` est l’état d’exécution. Les fichiers `specs/`, `contracts/` et `tests/` cités sont normatifs ; leurs liens de référence ne constituent pas une obligation de lecture récursive.

## Exécution
Une tâche = un objectif, un propriétaire, un périmètre d’écriture, des dépendances et des preuves. Ne commencer l’implémentation qu’après validation des dépendances. Ne pas deviner un contrat absent : déclarer un blocage précis et proposer un changement de contrat limité. Les tâches d’audit peuvent produire un résultat « capacité absente » documenté ; cela ne valide pas la capacité elle-même.

Respecter le template de mod installé. Ne pas substituer arbitrairement son framework ni réutiliser celui d’un ancien mod. Avant tout appel Vintage Story, vérifier signature et cycle de vie dans les assemblages ciblés et les sources correspondantes. Pas de `IChunkColumnGenerator` ou de surcharge inventée. Pas de `WipeAllHandlers()` global. Aucun Harmony par défaut.

Pas de génération globale dans chaque chunk, de `Random` partagé, de `GetHashCode()` persistant, d’horloge influençant le monde, de dépendance à l’ordre des threads ou d’itération non triée utilisée comme oracle. Le cœur reste indépendant du jeu, du GPU et du MCP. Les décisions géographiques publiées sont immuables.

## Collaboration et sécurité
Deux agents d’implémentation simultanés par défaut, seulement sur tâches indépendantes et chemins disjoints. Un worktree et un dossier de sortie par agent. Aucun agent ne modifie les contrats partagés, le plan, les versions, le registre d’état ou les dépendances NuGet sans passage par l’intégrateur. Les branches d’agents ne sont jamais fusionnées automatiquement.

Un seul détenteur du verrou de session Visual Studio/MCP et de l’instance de jeu. L’autorisation de débogage ne vaut pas autorisation de publier, de supprimer des sauvegardes ou de modifier une partie personnelle. Utiliser des sauvegardes jetables séparées. Ne pas lire/afficher les secrets de connexion. Aucune commande MCP n’est présumée : découvrir les outils réels et leur schéma.

## Finition et passation
Exécuter les tests de la tâche, ajouter un cas de régression pour tout défaut corrigé et relancer les tests du sous-système après fusion. Un test non exécuté est `NOT_RUN`, jamais `PASS`. Les captures ne remplacent ni les assertions ni les parcours en jeu. Remettre un rapport conforme à `templates/HANDOFF.md`, avec commit, fichiers, changements d’interface, commandes, résultats, liens vers preuves et risques. Le contrôleur valide avant `DONE`.

Les scripts documentaires ne construisent pas le mod. Ne pas annoncer une compatibilité ou des performances à partir de leur réussite.
