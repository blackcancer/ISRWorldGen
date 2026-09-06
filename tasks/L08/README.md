# L08 — Cavernes naturelles et hydrologie souterraine

## Livrable de lot
Réseaux naturels, volumes et navigation. Le détail fonctionnel est dans [S08](../../specs/S08.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L08-A — Planifier les réseaux de cavernes](L08-A.md)
Prérequis : L03-C, L05-C. Tests : T08-01, T08-03.

### [L08-B — Composer les volumes souterrains](L08-B.md)
Prérequis : L08-A. Tests : T08-02, T08-06.

### [L08-C — Valider navigation et eau souterraine](L08-C.md)
Prérequis : L08-B, L07-B. Tests : T08-04, T08-05.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
