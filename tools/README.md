# Outils documentaires — plan 1.2
## Capsules
```powershell
powershell -NoProfile -File ./tools/Get-TaskContext.ps1 -TaskId L14-B
```
Agrège uniquement les lectures de la tâche avec leurs empreintes ; refuse de tronquer une capsule hors budget. Ne lance aucun agent, compilateur ou jeu. Le script existant est conservé ; son exécution PowerShell n’a pas été testée sur le poste de l’utilisateur.

## Préparation d’adoption sans mutation du dépôt
Python 3.10+ facultatif, bibliothèque standard uniquement, sans dépendance runtime du mod :
```powershell
python ./tools/prepare_plan_update.py --existing "CHEMIN_DU_DEPOT_ACTUEL" --output "DOSSIER_DE_REVUE_NEUF_HORS_DEPOT"
```
Le dossier de revue doit être neuf et hors du dépôt et de cette livraison. Le script ne change aucun fichier actif. Il conserve les variations locales dans les fusions JSON et signale les conflits textuels pour revue. Les documents proposés sont dans proposal/, les conflits dans review-report.json. Le candidat d’état est séparé, jamais dans proposal/.

La comparaison utilise la référence documentaire 1.1 incorporée dans update_baseline_r11.json. Une adaptation locale divergente n’est pas écrasée. Les champs supplémentaires, tâches locales et preuves sont conservés dans le candidat d’état. Une absence d’état historique reste UNVERIFIED. Le script ne fournit ni --apply ni commande Git.

## Contrôles
```powershell
python ./tools/validate_spec.py
python ./tools/validate_spec.py --emit-context L18-C
python ./tools/test_documentation_tools.py
python ./tools/test_plan_update.py
```
Le validateur vérifie IDs, DAG, liens, tâches/exigences/tests et tailles des capsules. En l’absence du vrai état local, il utilise le modèle uniquement pour la cohérence documentaire et ne propose aucune tâche comme réellement prête.

Les auto-tests portent sur les outils du plan, pas sur le mod. Les résultats exécutés figurent dans docs/09-VERIFICATION-DU-DOSSIER.md et artifacts/. La préparation et les auto-tests Python ont leur propre statut. C#, jeu, Visual Studio et MCP ne sont pas invoqués par ces scripts.

Ne pas versionner automatiquement tous les logs, bin/obj, capsules et sauvegardes. Conserver seulement les preuves nécessaires et expurgées selon la politique du dépôt existant.

## Contrôles exécutés dans cette livraison
8 tests de validateur et 14 tests de préparation passent. Le sélecteur PowerShell est contrôlé statiquement pour tous les IDs, sans exécution PowerShell. Voir les journaux du rapport de vérification.
