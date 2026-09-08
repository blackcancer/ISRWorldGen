# ISRWorldGen — instructions de développement, plan 1.2
## Mission et lecture minimale
Développer en C# un générateur Vintage Story fondé sur un atlas géologique et hydrologique cohérent, avec de rares sites souterrains fantastiques **toujours reliés à une galerie praticable**. Visual Studio Community 2026 est l’IDE ; un MCP Visual Studio sera accessible à Codex.

L’état local et ses preuves font foi : ne réinitialiser aucun lot ni `registry/state.json` lors d’une adoption documentaire. Après L05-C validé, L05-D adopte et requalifie le plan 1.2. Les contrats C08–C12 sont des modèles internes versionnés, jamais des API Vintage Story supposées ; conserver les projets existants et ne renommer aucun chemin sans migration explicite.

Ne pas lire tout le cahier des charges. Lire `docs/02-SOCLE.md`, la fiche de la tâche demandée et ses seules lectures obligatoires. Consulter les sources pertinentes seulement si un symbole/API ou un algorithme doit être implémenté/vérifié. `registry/tasks.json` est le manifeste de routage ; `registry/state.json` est l’état d’exécution. Les fichiers `specs/`, `contracts/` et `tests/` cités sont normatifs ; leurs liens de référence ne constituent pas une obligation de lecture récursive.

## Exécution
Une tâche = un objectif, un propriétaire, un périmètre d’écriture, des dépendances et des preuves. Ne commencer l’implémentation qu’après validation des dépendances. Ne pas deviner un contrat absent : déclarer un blocage précis et proposer un changement de contrat limité. Les tâches d’audit peuvent produire un résultat « capacité absente » documenté ; cela ne valide pas la capacité elle-même.

Respecter le template de mod installé. Ne pas substituer arbitrairement son framework ni réutiliser celui d’un ancien mod. Avant tout appel Vintage Story, vérifier signature et cycle de vie dans les assemblages ciblés et les sources correspondantes. Pas de `IChunkColumnGenerator` ou de surcharge inventée. Pas de `WipeAllHandlers()` global. Aucun Harmony par défaut.

Pas de génération globale dans chaque chunk, de `Random` partagé, de `GetHashCode()` persistant, d’horloge influençant le monde, de dépendance à l’ordre des threads ou d’itération non triée utilisée comme oracle. Le cœur reste indépendant du jeu, du GPU et du MCP. Les décisions géographiques publiées sont immuables.

Les distributions de roches/strates, sols/fertilité, minerais/prospection, végétation et neige/glace sont désormais normatives. Une géologie unique alimente érosion, cavernes, minerais et prospection ; un seul modèle de sol/fertilité alimente distribution et adaptation native ; les états saisonniers ne régénèrent jamais les décisions structurelles selon la première visite. Le budget annuel L05-C est conservé lorsque L18 introduit stockage et fonte.

## Collaboration et sécurité
La politique détaillée de profils, d’escalade et de communication est dans [docs/03-ORCHESTRATION.md](docs/03-ORCHESTRATION.md). Par défaut, l’orchestrateur et le développeur de lot utilisent GPT-5.6 Terra Medium. Une difficulté algorithmique, numérique, concurrente ou de performance bornée peut recevoir directement une expertise GPT-5.6 Sol Medium ou xHigh ; Astra ne peut être choisi que s’il est réellement exposé par l’installation. Un relecteur distinct de l’auteur est obligatoire et son profil est adapté au risque.

Deux agents d’implémentation simultanés par défaut, seulement sur tâches indépendantes et chemins disjoints. Un worktree et un dossier de sortie par agent. Chaque lot indique objectif/exclusions, base, dépendances, périmètre, critères de recette, risques et condition d’escalade. Le développeur peut consulter un expert ciblé, ou lui déléguer un sous-périmètre exclusif, avec au plus un niveau de sous-délégation. Après deux corrections sans progrès mesurable, réévaluer la cause et le profil plutôt que répéter la même tentative.

Aucun agent ne modifie les contrats partagés, le plan, les versions, le registre d’état ou les dépendances NuGet sans passage par l’intégrateur. Les branches d’agents ne sont jamais fusionnées automatiquement. Les notifications de décision sont compactes : `Tâche | Événement | Référence du code | Résultat ou blocage | Preuves | Décision attendue`.

Un seul détenteur du verrou de session Visual Studio/MCP et de l’instance de jeu. L’autorisation de débogage ne vaut pas autorisation de publier, de supprimer des sauvegardes ou de modifier une partie personnelle. Utiliser des sauvegardes jetables séparées. Ne pas lire/afficher les secrets de connexion. Aucune commande MCP n’est présumée : découvrir les outils réels et leur schéma.

### Revue aveugle locale sans élévation
Lorsqu’une recette exige l’isolement de `sealed/`, appliquer le protocole complet de [l’orchestration](docs/03-ORCHESTRATION.md#revue-aveugle-locale-sans-élévation) : jeton restreint Low Integrity, aucun handle hérité, ACL/mandatory label vérifiés avant revue et reçu horodaté avant révélation. Exécuter les scripts JSON avec PowerShell 7 (`pwsh`). Cette isolation ne remplace jamais une revue métier indépendante.

## Finition et passation
Exécuter les tests de la tâche, ajouter un cas de régression pour tout défaut corrigé et relancer les tests du sous-système après fusion. Un test non exécuté est `NOT_RUN`, jamais `PASS`. Les captures ne remplacent ni les assertions ni les parcours en jeu. Remettre un rapport conforme à `templates/HANDOFF.md`, avec commit, fichiers, changements d’interface, commandes, résultats, liens vers preuves et risques. Le contrôleur valide avant `DONE`.

Les scripts documentaires ne construisent pas le mod. Ne pas annoncer une compatibilité ou des performances à partir de leur réussite.
