# L01 — Cœur déterministe et contrats de base

## Livrable de lot
Contrats C#, RNG, coordonnées et banc analytique. Le détail fonctionnel est dans [S01](../../specs/S01.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L01-A — Construire coordonnées, RNG et IDs](L01-A.md)
Prérequis : L00-A. Tests : T01-01, T01-02.

### [L01-B — Geler contrats C# et snapshots](L01-B.md)
Prérequis : L01-A. Tests : T01-03, T01-04.

### [L01-C — Établir banc analytique et harnais de tests](L01-C.md)
Prérequis : L01-B. Tests : T01-05, T01-06.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
