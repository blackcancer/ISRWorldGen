# L11 — Adaptateur Vintage Story et compatibilité du monde

## Livrable de lot
Colonnes natives, maps, contenu vanilla et serveur. Le détail fonctionnel est dans [S11](../../specs/S11.md). Le lot est validé quand ses trois sous-lots sont fusionnés et leurs tests de module relancés ensemble.

## Sous-lots

### [L11-A — Brancher les hooks et cartes natives](L11-A.md)
Prérequis : L00-C, L02-C, L01-B. Tests : T11-01, T11-02.

### [L11-B — Assembler la colonne complète](L11-B.md)
Prérequis : L11-A, L07-C, L08-C, L09-B, L10-B. Tests : T11-03.

### [L11-C — Qualifier contenu vanilla, spawn et serveur](L11-C.md)
Prérequis : L11-B, L12-A, L10-C. Tests : T11-04, T11-05, T11-06.

## Intégration et revue
Une branche n’autorise pas à changer les contrats partagés. Le relecteur examine les cas limites propres au lot et compare les preuves aux oracles, pas uniquement aux assertions écrites par l’auteur. En cas d’échec d’un consommateur, créer un défaut de régression reproductible et conserver son ID dans la passation.
