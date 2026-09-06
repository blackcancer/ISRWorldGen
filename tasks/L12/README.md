# L12 — Assets, configuration et outils de diagnostic

## Livrable de lot
Assets, profils, aperçu et outils de diagnostic. Le détail fonctionnel est dans [S12](../../specs/S12.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L12-A — Réaliser assets et palettes fantastiques](L12-A.md)
Prérequis : L09-B. Tests : T12-01.

### [L12-B — Exposer profils et configuration validée](L12-B.md)
Prérequis : L02-C, L10-A. Tests : T12-02, T12-03.

### [L12-C — Finaliser aperçu, diagnostics et preuves](L12-C.md)
Prérequis : L01-C, L06-C, L08-B, L09-B, L11-B. Tests : T12-04, T12-05, T12-06.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
