# L05 — Bassins versants, lacs et réseau de drainage

## Livrable de lot
Dépressions, drainage, débits et ports partagés. Le détail fonctionnel est dans [S05](../../specs/S05.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L05-A — Résoudre dépressions et topologie de drainage](L05-A.md)
Prérequis : L03-B. Tests : T05-01, T05-02, T05-04.

### [L05-B — Accumuler débits et transferts](L05-B.md)
Prérequis : L05-A, L04-C. Tests : T05-03.

### [L05-C — Publier exutoires et budgets de raffinement](L05-C.md)
Prérequis : L05-B, L02-C. Tests : T05-05, T05-06.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
