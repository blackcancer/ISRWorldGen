# Adoption de la révision 1.3 — L19 et L20

## Nature de la livraison

Ce paquet est un delta pour le dossier 1.2 existant. Il contient le plan directeur révisé, les deux specs, huit tickets, vingt scénarios, un mandat d’orchestration et des définitions de registres à fusionner. Il ne contient ni le dépôt complet, ni un état actif, ni du code C# du mod. Les contenus L00–L18 restent dans votre dépôt.

La version 1.3 désigne le dossier, pas une version jouable. Le snapshot GitHub consulté est identifié dans l’audit API ; il ne remplace pas la lecture du poste de développement. Arrêter seulement les écritures concurrentes sur les fichiers partagés pendant l’intégration, pas les travaux indépendants.

## Préparation non destructive

Depuis le dossier extrait du paquet, lancer Python 3.10 ou ultérieur :

```powershell
python .\tools\prepare_r13.py --source "D:\Chemin\ISRWorldGen" --output "D:\Revues\ISRWorldGen-r13"
```

Les deux chemins sont à adapter. Le répertoire de sortie doit ne pas exister et être extérieur au dépôt et au paquet. Le script lit les quatre registres de définition réels et prépare un dossier `candidate/` ; il n’applique rien et n’appelle jamais Git.

Il conserve les tâches, dépendances et champs locaux, y compris les états déjà présents dans les définitions. Les fichiers existants qu’il propose de changer sont signalés avec leur hash et un diff. Une collision d’identifiant de définition incompatible est refusée ; aucun remplacement silencieux. `registry/state.json` n’est ni généré ni copié, même si absent dans le dépôt. Ses décisions restent exclusivement locales.

Une sortie candidate sans erreur ne signifie pas adoption : les textes modifiés doivent être revus, les schémas/consommateurs locaux vérifiés et les contrôles du dossier relancés. Les liens et capsules sont vérifiés contre la vue candidat + dépôt, sans recopier tout le dépôt. Un lien ancien cassé dans un document de base non modifié reste du ressort du validateur global local.

## Changements attendus

`registry/tasks.json` reçoit les huit nouvelles définitions et L13-A reçoit L19-C comme prérequis supplémentaire. `registry/requirements.json` et `registry/tests.json` reçoivent R19/T19 et R20/T20. Les nouvelles définitions restent BACKLOG/NOT_RUN, sauf si une entrée identique déjà connue possède un état local à préserver.

`registry/gates.json` reçoit L19-A/B/C dans G4, la précision documentaire dans G4/G5 et la nouvelle gate G6 hors V1. G0–G3 ne changent pas de critères. `registry/plan-scopes-r13.json` explicite les tâches et gates V1/post-V1. Aucune preuve de gate n’est écrasée. La préparation émet un plan de requalification, pas une décision de réussite.

Le candidat ajoute une courte section de délimitation à S17 et L17-C ainsi qu’une note au README de L13, à L13-A et à la recette générale. Le reste de leur contenu local est conservé. Le plan directeur complet est proposé comme fichier à fusionner, jamais imposé au dépôt. Le prompt est livré sous un nouveau nom, `DEMARRAGE-CODEX-R13.md`, pour ne pas écraser un mandat local modifié.

Les références de synthèse `docs/06-TRACABILITE.md`, `docs/09-VERIFICATION-DU-DOSSIER.md`, `README.md` et le registre d’orchestration actif doivent être actualisées depuis les résultats locaux après intégration. Ne pas remplacer une preuve de validation 1.2 par le rapport de cet outil. Les compteurs 21 lots/69 tâches/152 exigences/152 scénarios supposent exactement la base 1.2 sans ajouts locaux.

## Périmètres de qualification et ordonnanceur

Les champs `release_scope`, `requires_gates` et la nouvelle gate G6 décrivent la politique 1.3. Ils ne sont pas la preuve qu’un script 1.2 les interprète. Vérifier et adapter le sélecteur de mission, la recette, la clôture des gates et le validateur local avant de déclarer le plan adopté.

Pour V1, sélectionner les anciennes tâches V1 et L19 seulement. Les tests T20 ne deviennent pas bloquants de G5 parce qu’ils figurent dans le même registre. Pour lancer L20, vérifier une décision G5 réellement qualifiée et liée à une baseline 1.0.0, pas seulement l’appartenance de L13-C aux tâches terminées. G6 ne peut être évaluée qu’après cette preuve.

Contrôler ce comportement avec un cas négatif : G5 absent/refusé alors que L13-C est marqué DONE doit empêcher L20-A ; T20 NOT_RUN doit en revanche être compatible avec G5 lorsque tous les contrôles V1 sont satisfaits. Si le sélecteur ne peut exprimer ces règles, les faire appliquer explicitement par l’orchestrateur en attendant son adaptation. Ne pas masquer la limite dans un rapport de conformité.

## Requalification limitée au delta

Conserver les preuves L05-C, L05-D et L14–L18. Revoir les clauses documentaires de G4/G5 et la dépendance de L13-A. Vérifier l’absence de dépendance inverse vers L20, la traçabilité R19/T19 et R20/T20, les chemins d’écriture sans runtime de L19 et l’autonomie du pack.

Une fois le plan adopté, L19-A/B peuvent avancer avec les travaux indépendants, puis L19-C utilise les sorties réelles L11-C/L12-C. Le manque d’information est une conclusion documentaire recevable, jamais un motif de créer un service animalier. Après G5 seulement, poursuivre les cinq missions L20, avec revue séparée G6.

## Limites des outils livrés

Les tests du script utilisent des dépôts synthétiques temporaires ; ils ne prouvent pas l’état de votre dépôt. La préparation valide les schémas connus, les références, l’acyclicité, la séparation de release et les documents nouveaux/modifiés. Elle ne compile pas le mod, ne valide pas les DLL locales, ne lance pas le jeu et ne remplace pas les validations métier du dossier 1.2.

Aucune fusion automatique dans le dépôt, aucun commit, push, déploiement ou état DONE n’est effectué. En cas de conflit de contenu, conserver le fichier local et faire fusionner le candidat par l’intégrateur avant toute adoption.
