# L00 — Environnement, API et preuve d’intégration

## Livrable de lot
Cible locale, MCP débogable et spike terrain sûr. Le détail fonctionnel est dans [S00](../../specs/S00.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L00-A — Auditer et verrouiller la cible locale](L00-A.md)
Prérequis : aucun. Tests : T00-01, T00-02.

### [L00-B — Prouver le débogage C# par le MCP](L00-B.md)
Prérequis : L00-A. Tests : T00-03.

### [L00-C — Valider un remplacement de terrain minimal](L00-C.md)
Prérequis : L00-B. Tests : T00-04, T00-05, T00-06.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
