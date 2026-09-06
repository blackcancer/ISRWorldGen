# L07 — Cours d’eau, lacs, littoraux et océans détaillés

## Livrable de lot
Lits, eau, lacs, côtes et deltas stables. Le détail fonctionnel est dans [S07](../../specs/S07.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L07-A — Construire trajectoires et sections fluviales](L07-A.md)
Prérequis : L06-C. Tests : T07-01, T07-02.

### [L07-B — Construire lacs, côtes et deltas](L07-B.md)
Prérequis : L07-A. Tests : T07-03, T07-04, T07-05.

### [L07-C — Qualifier la matérialisation des fluides](L07-C.md)
Prérequis : L07-B, L11-A. Tests : T07-06.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
