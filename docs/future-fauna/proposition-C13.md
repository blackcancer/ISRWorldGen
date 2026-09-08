# Proposition de gel C13 / C13-AQ par L19-B

**Statut : à approuver par l'intégrateur — aucune interface n'est implémentée.** Baseline : `4754e2e` (plan r1.5). Références normatives : C13, C13-AQ, S19 §2-7 et S-AQ R-AQ-13.

## Frontière proposée

- Un seul assembly de contrat partagé, serveur, lecture seule, DTO immuables; aucune dépendance vers Core, Runtime, chunk, buffer mutable ou mod consommateur.
- Fournisseur facultatif et découvert une seule fois via le chargement à qualifier; absent/incompatible reste un résultat de découverte explicite, jamais « aucune eau ».
- Les opérations C13 restent `DescribeCapabilities`, contexte immédiat ou différé borné, candidats et coverage. L'extension AQ impose une dimension et un domaine vertical explicites pour toute eau; le rayon horizontal et le filtre vertical ne se confondent pas.
- Les résultats portent identité du monde, révision, provenance, validité temporelle et disponibilité de champ. `KnownAbsent`, `NotRecorded/LegacyMissing`, `Unsupported`, `Corrupt`, `Incompatible`, coverage partielle et dépassement de budget restent distincts.

## Minimum C13-AQ et cas de contrôle

Le gel couvre `WaterGeometry`, `WaterEnvironment`, `BottomDescription`, `FixedCommunity`, `HabitatCandidate`, `WaterConnection` et `Coverage`, avec les identités et unités/résolutions du dictionnaire. T-AQ-13 sera satisfait seulement après confrontation aux producteurs D : un exemple marin, rivière et interface terre/eau; le témoin négatif obligatoire est refus de verticalité facultative ou de température d'air présentée comme température d'eau.

Les cas à rejouer par D/E/F sont : volumes superposés, estuaire, chenal/chute, récif modifiant le support, patch rejeté, profondeur hors enveloppe, coverage verticale partielle, fournisseur absent/incompatible, monde ancien et monde modifié. Aucun ne autorise à déduire une navigabilité, une espèce, une population ou un stock.

## Budgets structurels proposés

Les bornes de S19 sont gelées comme point de départ, non comme mesure de performance : rayon 512 blocs, 16 régions, 8192 candidats examinés, 64 résultats, réponse 512 Kio, lot de 32 points, 4 lectures actives + 32 en attente, page décodée 4 Mio et cache 64 Mio. Un dépassement doit retourner une limite/couverture explicite; une géométrie trop longue est fragmentée à résolution connue ou rejetée. F seulement mesure latence, allocations, disque et surcoûts sur machine identifiée.

## Décision attendue

L'intégrateur doit approuver ou amender explicitement ces sémantiques, versions/ABI, packaging et budgets avant D/E. Toute modification ultérieure de C13/C13-AQ, solution, producteur ou registre reste hors L19-B et doit être réservée.
