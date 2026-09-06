# L10 — Persistance, streaming, concurrence et sûreté

## Livrable de lot
Manifeste, caches/scheduler et reprise après crash. Le détail fonctionnel est dans [S10](../../specs/S10.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L10-A — Écrire manifeste et stockage versionné](L10-A.md)
Prérequis : L01-B. Tests : T10-01, T10-02.

### [L10-B — Construire scheduler et caches bornés](L10-B.md)
Prérequis : L10-A, L01-C. Tests : T10-04, T10-05.

### [L10-C — Qualifier reprise après crash et arrêt](L10-C.md)
Prérequis : L10-B, L11-B. Tests : T10-03, T10-06.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
