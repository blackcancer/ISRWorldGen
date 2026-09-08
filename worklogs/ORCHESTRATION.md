# Orchestration ISRWorldGen

## Checkpoint 2026-09-08 — adoption 1.2

- `main` publié : `c8faebcb9ba5155adacf9600101df2b8ca8c1840`.
- Plan 1.2 intégré sans réinitialiser les 42 IDs existants ni les preuves : 19 lots, 61 tâches, 132 exigences et 132 scénarios.
- Validation documentaire : `validate_spec.py` PASS ; 9 tests du validateur et 14 tests de préparation PASS.
- L05-D est en cours : matrice de compatibilité C01–C03 vers C08–C12 et requalification des sorties hydrologie/relief.
- Risques ouverts : L04-A, L00-C et L10-A conservent leurs limites runtime historiques ; aucun statut `DONE` n’est inféré par le plan 1.2.
- Action suivante : revue indépendante puis intégration de L05-D ; L14-A et L18-A ne démarrent qu’après cette décision.
