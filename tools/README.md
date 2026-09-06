# Outils du dossier documentaire
## Capsule Windows sans dépendance Python
Depuis la racine :
```powershell
powershell -NoProfile -File ./tools/Get-TaskContext.ps1 -TaskId L05-B
```
Le script lit le manifeste, agrège uniquement les lectures explicites, calcule leurs empreintes et refuse de tronquer au-delà du budget. Il écrit dans `artifacts/contexts/`. Il ne lance ni agents, ni compilateur, ni jeu. Il est écrit pour les fonctions usuelles de Windows PowerShell 5.1/PowerShell 7 ; son exécution PowerShell n’a pas été vérifiée dans l’environnement de préparation de ce dossier. Une lecture manuelle des fichiers indiqués reste strictement équivalente.

## Validation facultative avec Python 3.10 ou ultérieur
Ces scripts servent à la cohérence du cahier, pas au développement runtime du mod :
```powershell
python ./tools/validate_spec.py
python ./tools/validate_spec.py --emit-context L05-B
python ./tools/test_documentation_tools.py
```
Le validateur contrôle liens relatifs, sources minimales, DAG, IDs, correspondance exigences/tâches/tests, partition du corpus et tailles des capsules. Les huit auto-tests injectent notamment cycle, lien cassé, source manquante et budget dépassé. Ils ne testent pas l’API de Vintage Story.

Le rapport `artifacts/spec-validation.json` distingue explicitement DOCUMENTATION_ONLY. Les scripts Python ont été exécutés lors de la livraison ; la preuve d’exécution est fournie dans les artefacts. Les 84 scénarios de test du mod restent NOT_RUN.

## Git
Les sorties `artifacts/contexts`, snapshots/PNGs de test, logs, bin/obj, données locales du MCP et sauvegardes de laboratoire ne sont pas des sources à versionner automatiquement. Conserver uniquement les preuves sélectionnées et expurgées. Fusionner consciemment cette politique à l’ignore existant du dépôt plutôt que remplacer sa configuration.
