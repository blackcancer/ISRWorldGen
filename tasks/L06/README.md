# L06 — Évolution du relief et raffinement multi-échelle

## Livrable de lot
Érosion, dépôts et raffinement conservatif. Le détail fonctionnel est dans [S06](../../specs/S06.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L06-A — Implémenter incision et uplift déterministes](L06-A.md)
Prérequis : L05-C, L03-C. Tests : T06-01, T06-03.

### [L06-B — Ajouter transport, dépôts et boucle couplée](L06-B.md)
Prérequis : L06-A. Tests : T06-02, T06-04.

### [L06-C — Raffiner sans détruire les contraintes](L06-C.md)
Prérequis : L06-B. Tests : T06-05, T06-06.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
