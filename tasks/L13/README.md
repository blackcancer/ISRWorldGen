# L13 — Qualification complète et distribution

## Livrable de lot
Campagnes, performances, parcours et distribution. Le détail fonctionnel est dans [S13](../../specs/S13.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L13-A — Exécuter la campagne et la revue des paysages](L13-A.md)
Prérequis : L09-C, L11-C, L12-C, L10-C. Tests : T13-01, T13-02.

### [L13-B — Qualifier performances et parties complètes](L13-B.md)
Prérequis : L13-A, L12-B. Tests : T13-03, T13-04.

### [L13-C — Préparer et auditer la distribution V1](L13-C.md)
Prérequis : L13-B, L12-A. Tests : T13-05, T13-06.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
