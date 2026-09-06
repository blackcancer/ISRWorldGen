# L04 — Climat, précipitations et bilan de l’eau

## Livrable de lot
Température, précipitations, recharge et budgets. Le détail fonctionnel est dans [S04](../../specs/S04.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L04-A — Calculer température et latitude cohérentes](L04-A.md)
Prérequis : L03-B. Tests : T04-01, T04-02.

### [L04-B — Transporter humidité et précipitations](L04-B.md)
Prérequis : L04-A. Tests : T04-03, T04-04.

### [L04-C — Établir budgets de ruissellement et recharge](L04-C.md)
Prérequis : L04-B, L03-C. Tests : T04-05, T04-06.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
