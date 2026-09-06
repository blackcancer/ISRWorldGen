# L09 — Sites fantastiques rares, connectés et explorables

## Livrable de lot
Catalogue rare, raccords et certificats d’accès. Le détail fonctionnel est dans [S09](../../specs/S09.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L09-A — Sélectionner des sites rares connectés](L09-A.md)
Prérequis : L08-C. Tests : T09-02, T09-03.

### [L09-B — Composer les cinq familles fantastiques](L09-B.md)
Prérequis : L09-A. Tests : T09-01, T09-06.

### [L09-C — Certifier les accès après toutes les passes](L09-C.md)
Prérequis : L09-B, L11-C, L12-A. Tests : T09-04, T09-05.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
