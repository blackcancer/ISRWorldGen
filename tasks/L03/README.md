# L03 — Géologie, continents, relief initial et matériaux

## Livrable de lot
Continents, relief initial, couches et propriétés rocheuses. Le détail fonctionnel est dans [S03](../../specs/S03.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L03-A — Produire plaques et masses continentales](L03-A.md)
Prérequis : L02-C. Tests : T03-01, T03-02.

### [L03-B — Composer reliefs et enveloppe verticale](L03-B.md)
Prérequis : L03-A. Tests : T03-05, T03-06.

### [L03-C — Décrire strates et propriétés des roches](L03-C.md)
Prérequis : L03-B. Tests : T03-03, T03-04.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
