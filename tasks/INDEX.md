# Index des 61 tâches — ISRWorldGen 1.2

Point d’avancement déclaré : L05-C ; état réel dans le dépôt. L05-D adopte le plan ; les lots L14–L18 ne sont pas exécutés après L13.

| ID | Mission | Prérequis |
|---|---|---|
| [L00-A](L00/L00-A.md) | Auditer et verrouiller la cible locale | — |
| [L00-B](L00/L00-B.md) | Prouver le débogage C# par le MCP | L00-A |
| [L00-C](L00/L00-C.md) | Valider un remplacement de terrain minimal | L00-B |
| [L01-A](L01/L01-A.md) | Construire coordonnées, RNG et IDs | L00-A |
| [L01-B](L01/L01-B.md) | Geler contrats C# et snapshots | L01-A |
| [L01-C](L01/L01-C.md) | Établir banc analytique et harnais de tests | L01-B |
| [L02-A](L02/L02-A.md) | Construire la géométrie robuste de l’atlas | L01-C |
| [L02-B](L02/L02-B.md) | Indexer et borner les descriptions globales | L02-A |
| [L02-C](L02/L02-C.md) | Définir profils d’échelle et contrôle des motifs | L02-B |
| [L03-A](L03/L03-A.md) | Produire plaques et masses continentales | L02-C |
| [L03-B](L03/L03-B.md) | Composer reliefs et enveloppe verticale | L03-A |
| [L03-C](L03/L03-C.md) | Décrire strates et propriétés des roches | L03-B |
| [L04-A](L04/L04-A.md) | Calculer température et latitude cohérentes | L03-B |
| [L04-B](L04/L04-B.md) | Transporter humidité et précipitations | L04-A |
| [L04-C](L04/L04-C.md) | Établir budgets de ruissellement et recharge | L04-B, L03-C |
| [L05-A](L05/L05-A.md) | Résoudre dépressions et topologie de drainage | L03-B |
| [L05-B](L05/L05-B.md) | Accumuler débits et transferts | L05-A, L04-C |
| [L05-C](L05/L05-C.md) | Publier exutoires et budgets de raffinement | L05-B, L02-C |
| [L06-A](L06/L06-A.md) | Implémenter incision et uplift déterministes | L05-C, L03-C, L14-B, L18-A |
| [L06-B](L06/L06-B.md) | Ajouter transport, dépôts et boucle couplée | L06-A |
| [L06-C](L06/L06-C.md) | Raffiner sans détruire les contraintes | L06-B |
| [L07-A](L07/L07-A.md) | Construire trajectoires et sections fluviales | L06-C |
| [L07-B](L07/L07-B.md) | Construire lacs, côtes et deltas | L07-A |
| [L07-C](L07/L07-C.md) | Qualifier la matérialisation des fluides | L07-B, L11-A |
| [L08-A](L08/L08-A.md) | Planifier les réseaux de cavernes | L03-C, L05-C, L14-B, L18-A |
| [L08-B](L08/L08-B.md) | Composer les volumes souterrains | L08-A |
| [L08-C](L08/L08-C.md) | Valider navigation et eau souterraine | L08-B, L07-B |
| [L09-A](L09/L09-A.md) | Sélectionner des sites rares connectés | L08-C |
| [L09-B](L09/L09-B.md) | Composer les cinq familles fantastiques | L09-A |
| [L09-C](L09/L09-C.md) | Certifier les accès après toutes les passes | L09-B, L11-C, L12-A |
| [L10-A](L10/L10-A.md) | Écrire manifeste et stockage versionné | L01-B |
| [L10-B](L10/L10-B.md) | Construire scheduler et caches bornés | L10-A, L01-C |
| [L10-C](L10/L10-C.md) | Qualifier reprise après crash et arrêt | L10-B, L11-B |
| [L11-A](L11/L11-A.md) | Brancher les hooks et cartes natives | L00-C, L02-C, L01-B |
| [L11-B](L11/L11-B.md) | Assembler la colonne complète | L11-A, L07-C, L08-C, L09-B, L10-B, L14-C, L15-B, L16-C, L17-C, L18-B, L18-C |
| [L11-C](L11/L11-C.md) | Qualifier contenu vanilla, spawn et serveur | L11-B, L12-A, L10-C, L15-C, L16-D, L17-D, L18-D |
| [L12-A](L12/L12-A.md) | Réaliser assets et palettes fantastiques | L09-B |
| [L12-B](L12/L12-B.md) | Exposer profils et configuration validée | L02-C, L10-A, L05-D |
| [L12-C](L12/L12-C.md) | Finaliser aperçu, diagnostics et preuves | L01-C, L06-C, L08-B, L09-B, L11-B, L16-D, L17-D, L18-D |
| [L13-A](L13/L13-A.md) | Exécuter la campagne et la revue des paysages | L09-C, L11-C, L12-C, L10-C |
| [L13-B](L13/L13-B.md) | Qualifier performances et parties complètes | L13-A, L12-B |
| [L13-C](L13/L13-C.md) | Préparer et auditer la distribution V1 | L13-B, L12-A |
| [L05-D](L05/L05-D.md) | Adopter le plan et auditer les contrats publiés | L05-C |
| [L14-A](L14/L14-A.md) | Inventorier roches et associations natives | L05-D, L03-C |
| [L14-B](L14/L14-B.md) | Compléter la stratigraphie 3D commune | L14-A |
| [L14-C](L14/L14-C.md) | Raccorder strates et métadonnées natives | L14-B, L11-A |
| [L18-A](L18/L18-A.md) | Qualifier neige climatique et budgets saisonniers | L05-D, L04-C, L11-A |
| [L15-A](L15/L15-A.md) | Produire profils pédologiques et classes | L14-B, L06-C, L18-A |
| [L15-B](L15/L15-B.md) | Matérialiser les sols sans casser les couvertures | L15-A, L14-C, L07-C |
| [L15-C](L15/L15-C.md) | Qualifier agriculture et conservation en jeu | L15-B, L11-B, L18-D |
| [L16-A](L16/L16-A.md) | Extraire règles de dépôts et profil de potentiel | L14-B |
| [L16-B](L16/L16-B.md) | Planifier occurrences et matérialisation filtrée | L16-A, L06-C |
| [L16-C](L16/L16-C.md) | Raccorder gisements et modes de prospection | L16-B, L14-C, L07-C |
| [L16-D](L16/L16-D.md) | Qualifier minerais après assemblage complet | L16-C, L11-B, L12-A |
| [L17-A](L17/L17-A.md) | Inventorier flore et qualifier contexte écologique | L15-A, L18-A |
| [L17-B](L17/L17-B.md) | Composer peuplements et voisinages stables | L17-A, L06-C, L07-B |
| [L17-C](L17/L17-C.md) | Brancher arbres, patches et sous-bois natifs | L17-B, L15-B, L11-A |
| [L17-D](L17/L17-D.md) | Qualifier flore, récoltes et saisons en monde complet | L17-C, L18-D, L11-B |
| [L18-B](L18/L18-B.md) | Distribuer neige et glace initiales | L18-A, L07-C, L15-B |
| [L18-C](L18/L18-C.md) | Garantir gel-dégel des eaux et ownership | L18-A, L07-C |
| [L18-D](L18/L18-D.md) | Qualifier l’année et le rattrapage saisonnier | L18-B, L18-C, L11-B, L10-C |
