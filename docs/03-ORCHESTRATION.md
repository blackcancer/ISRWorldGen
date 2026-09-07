# Orchestration Codex et agents
## Principe
Le cahier des charges est divisé en 14 lots et 42 sous-lots. Un sous-lot constitue une mission délégable, pas un chunk de terrain. Il doit produire un résultat testable avec un périmètre d’écriture limité. L’ordre numérique aide à lire ; les dépendances de `registry/tasks.json` déterminent l’ordre d’exécution réel.

L’orchestrateur lit le plan, le registre et la fiche active. Un agent d’implémentation ne lit que AGENTS, le socle, sa fiche, la spécification du module, les contrats indiqués et ses tests. Un relecteur reçoit le diff, les mêmes exigences et les preuves pertinentes, non tous les journaux du projet. Les dépendances amont ne sont pas chargées récursivement : leurs contrats et leur statut intégré suffisent, sauf bug à investiguer.

Les capsules de `tools/Get-TaskContext.ps1` comprennent les sources documentaires exactes et leurs hashes, avec un plafond de 48 Kio. Ce plafond est notre politique de tâche, pas la limite de contexte du modèle. Le plafond de lecture automatique AGENTS est un mécanisme distinct. Une capsule trop grande est refusée ; on scinde la mission ou justifie une extension explicite, sans tronquer un contrat. Les noms d’API incertains imposent une consultation ciblée des sources correspondantes.

## Rôles, profils et droits
L’**intégrateur** possède architecture, contrats, fichiers de projet, registre, versions, fusion, arbitrages et verrou de l’instance de jeu. L’**agent de module** implémente son sous-lot et ses tests dans les chemins autorisés. Le **relecteur** inspecte les cas limites et essaie de faire échouer la solution. Ces rôles peuvent tourner, mais une même personne/instance ne s’auto-certifie pas silencieusement.

Politique locale de profils :

- orchestrateur et développeur de lot : `gpt-5.6-terra`, effort `medium` ;
- expertise ciblée : `gpt-5.6-sol`, effort `medium` ;
- expertise algorithmique, numérique, concurrente ou performance difficile : `gpt-5.6-sol`, effort `xhigh` ;
- expertise transversale : Astra lorsqu’il est réellement disponible ; sur ce poste, Astra n’est pas exposé à la transition et le repli est Sol xHigh, consigné dans la passation ;
- relecture : agent distinct, Terra pour un lot courant ; Sol Medium ou xHigh pour un cœur critique.

Le choix est passé explicitement à l’outil de délégation lorsque celui-ci le permet. Le modèle demandé, le modèle effectivement exposé et le modèle observé sont distingués : une déclaration de l’agent ne vaut pas preuve. La session principale ne se reconfigure pas par un fichier du dépôt ; son modèle est `NON_VERIFIE` si les métadonnées d’exécution ne l’exposent pas.

Deux agents d’implémentation simultanés par défaut suffisent au démarrage. Une augmentation exige des tâches réellement indépendantes, sans chemins communs ni dépendance de debug. Les sous-agents consomment eux-mêmes du contexte et des outils ; ils ne sont donc pas multipliés pour relire tous le même cahier. La délégation utilise la capacité native de Codex si disponible ; sinon l’intégrateur relaie une question compacte sans prétendre à un échange direct. Sources DEV-01 et DEV-02.

## Séquence d’une mission
L’intégrateur vérifie que les dépendances sont DONE au commit intégré, renseigne propriétaire/base/branche et crée un espace de sortie unique. Le contrat de mission reprend objectif, exclusions, interfaces, risques, recours expert autorisé, condition d’escalade, tests, base exacte et emplacements de résultats. L’agent confirme son périmètre, obtient la capsule et examine les contrats utilisés. Il développe/teste, puis remet un HANDOFF avec preuves et éventuelles demandes de changement. Le relecteur contrôle le diff et au moins les risques identifiés. L’intégrateur fusionne, relance la régression concernée et seulement ensuite marque DONE.

Un développeur peut solliciter directement un expert dans ce contrat : une **consultation** restitue recommandation et vérifications ; une **délégation d’implémentation** donne à l’expert un périmètre d’écriture exclusif et traçable, pendant lequel le développeur n’y écrit plus. Un expert actif par lot et un seul niveau de sous-délégation par défaut. Après deux corrections sans progrès mesurable, identifier si l’obstacle vient du raisonnement, de la documentation, de l’outillage, du test ou d’une contradiction ; attribuer alors directement l’expertise adaptée ou remonter un dossier factuel.

Une branche d’agent n’emporte pas de modification opportuniste des contrats partagés ou du fichier de solution. Les demandes sont enregistrées avec le contrat actuel, le besoin, l’impact sur consommateurs et les tests à adapter. L’intégrateur peut alors confier une mission distincte de contrat et rebaser les agents concernés.

## Collision et ressources partagées
Chaque agent travaille dans son worktree avec ses bin/obj, rapports et fixtures générées. Aucun déploiement concurrent vers le même dossier de mod de test. Un seul agent utilise le MCP et le jeu à un instant donné ; le verrou contient propriétaire, PID, chemin de solution, sauvegarde jetable et expiration administrative. Libération obligatoire après la session, même en échec. Les outils réels déterminent si plusieurs instances Visual Studio séparées sont possibles ; cela n’est pas présumé.

Les écritures de `registry/state.json` appartiennent à l’intégrateur. Les agents écrivent uniquement leur rapport local `worklogs/Lxx-X.md`. Les rapports complets ne sont pas collés dans le fil principal : un résumé court renvoie aux artefacts et codes de test.

Les notifications qui modifient le suivi utilisent, après suppression des champs inutiles : `Tâche | Événement | Référence du code | Résultat ou blocage | Preuves | Décision attendue`. Les échanges locaux développeur–expert ou développeur–relecteur ne sont pas retransmis intégralement ; si la topologie des outils ne les permet pas, le relais de l’intégrateur reste compact et explicite.

## Reprise après interruption
Relire AGENTS, le statut de la tâche, sa capsule et son HANDOFF le plus récent. Vérifier le commit courant, les fichiers non committés et la validité du verrou MCP. Ne pas supposer qu’une action annoncée a été effectuée. Si le contexte doit être réduit, conserver faits vérifiés, contrats, travaux effectués, tests exécutés, blocages et prochaine action exacte ; les hypothèses non testées restent identifiées.

## Définition de terminé
Code borné et documenté ; exigences affectées couvertes ; tests ciblés réellement exécutés ; preuve structurée ; aucun contrat modifié sans décision ; régression consommateur relancée après fusion ; état final et limites explicites. Ni une build seule, ni un screenshot, ni un raisonnement de l’agent ne suffit à valider l’ensemble du mod.
