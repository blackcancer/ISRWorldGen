# L02 — Atlas global et maillage Voronoï/Delaunay

## Livrable de lot
Maillage/global atlas borné, ownership et profils. Le détail fonctionnel est dans [S02](../../specs/S02.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L02-A — Construire la géométrie robuste de l’atlas](L02-A.md)
Prérequis : L01-C. Tests : T02-01, T02-02.

### [L02-B — Indexer et borner les descriptions globales](L02-B.md)
Prérequis : L02-A. Tests : T02-03, T02-04.

### [L02-C — Définir profils d’échelle et contrôle des motifs](L02-C.md)
Prérequis : L02-B. Tests : T02-05, T02-06.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
