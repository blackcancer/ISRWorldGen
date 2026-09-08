# ISRWorldGen — Cahier des charges et plan intercalé 1.2
**8 septembre 2026 · C# · Visual Studio Community 2026 · Codex + MCP Visual Studio**

Cette révision intègre les cinq distributions demandées : strates/roches, sols/fertilité, minerais/gisements/prospection, végétation, neige/glace. Le développement ayant été signalé à **L05-C**, les identifiants et travaux existants sont conservés. Ce dossier est un plan documentaire, pas une nouvelle implémentation ni un registre d’avancement reconstruit.

## Entrée immédiate
Lire [la migration depuis L05-C](docs/10-MIGRATION-DEPUIS-L05-C.md), puis [le plan consolidé](docs/01-PLAN.md). La session principale reçoit [le prompt d’adoption](prompts/ADOPTER-PLAN-1.2.md) et le [mandat d’orchestrateur](prompts/DEMARRAGE-CODEX.md). Ce dernier pilote le projet entier ; les fiches de sous-lots restent des missions d’agents.

**Extraire à côté du dépôt, pas par-dessus.** Aucun `registry/state.json`, code ni worklog de développement n’est livré. Le [modèle d’état](registry/state.template.json) n’est pas à copier sur l’état réel. Le script de préparation décrit dans [les outils](tools/README.md) produit un rapport et des candidats hors dépôt, sans les appliquer. Toute modification locale doit être fusionnée/revue ; ne pas effacer les preuves existantes.

## Organisation
Le dossier contient 19 lots et 61 tâches (42 existantes conservées, 19 ajouts dont L05-D), 132 exigences et 132 scénarios principaux (84 historiques, 48 nouveaux), ainsi que 13 contrats C00–C12. Les nouveaux numéros L14–L18 ne définissent pas un ordre après L13 : le DAG les intercale avant les consommateurs.

Le socle [02-SOCLE](docs/02-SOCLE.md) reste commun. Les nouveaux modules sont [S14 strates](specs/S14.md), [S15 sols](specs/S15.md), [S16 minerais](specs/S16.md), [S17 végétation](specs/S17.md), [S18 neige/glace](specs/S18.md). Les [tâches](tasks/INDEX.md) donnent objectif, lectures limitées, périmètre et tests ; les [contrats](contracts/README.md) et [recette](tests/00-RECETTE.md) assurent la traçabilité.

## Lecture ciblée pour Codex
Un agent reçoit une seule tâche et ses lectures déclarées, jamais le dossier entier. Exemple de préparation documentaire sous Windows :
```powershell
powershell -NoProfile -File ./tools/Get-TaskContext.ps1 -TaskId L14-B
```
Ce script ne lance aucun agent ni le jeu. Le contexte est refusé au-delà du budget, sans supprimer de contraintes. Les noms de projets WorldGen des anciens chemins sont des alias à mapper en L05-D, pas une demande de renommer une solution ISRWorldGen existante.

## État des vérifications
Les [contrôles documentaires](docs/09-VERIFICATION-DU-DOSSIER.md) couvrent IDs, dépendances, liens, capsules, couverture exigences/tests et préparation de fusion sans perte. Les nouvelles preuves du jeu sont toutes NOT_RUN. L’état des tests anciens est inconnu ici et doit être conservé dans le dépôt.

Les [sources primaires](docs/12-VERIFICATION-DISTRIBUTIONS-API.md) documentent les points de vigilance natifs. Elles ne remplacent pas la compilation et les probes sur la version installée. Aucun dépôt distant n’a été modifié ; aucun mod n’a été compilé/exécuté dans cette livraison.
