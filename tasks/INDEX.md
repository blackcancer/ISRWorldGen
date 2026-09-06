# Index des 42 sous-lots

Ne charger que la fiche sélectionnée et ses lectures déclarées. Le statut courant se trouve dans [state.json](../registry/state.json), pas dans les fichiers de spécification.

| Tâche | Objet | Prérequis | Tests principaux |
|---|---|---|---|
| [L00-A](L00/L00-A.md) | Auditer et verrouiller la cible locale | Aucun | T00-01, T00-02 |
| [L00-B](L00/L00-B.md) | Prouver le débogage C# par le MCP | L00-A | T00-03 |
| [L00-C](L00/L00-C.md) | Valider un remplacement de terrain minimal | L00-B | T00-04, T00-05, T00-06 |
| [L01-A](L01/L01-A.md) | Construire coordonnées, RNG et IDs | L00-A | T01-01, T01-02 |
| [L01-B](L01/L01-B.md) | Geler contrats C# et snapshots | L01-A | T01-03, T01-04 |
| [L01-C](L01/L01-C.md) | Établir banc analytique et harnais de tests | L01-B | T01-05, T01-06 |
| [L02-A](L02/L02-A.md) | Construire la géométrie robuste de l’atlas | L01-C | T02-01, T02-02 |
| [L02-B](L02/L02-B.md) | Indexer et borner les descriptions globales | L02-A | T02-03, T02-04 |
| [L02-C](L02/L02-C.md) | Définir profils d’échelle et contrôle des motifs | L02-B | T02-05, T02-06 |
| [L03-A](L03/L03-A.md) | Produire plaques et masses continentales | L02-C | T03-01, T03-02 |
| [L03-B](L03/L03-B.md) | Composer reliefs et enveloppe verticale | L03-A | T03-05, T03-06 |
| [L03-C](L03/L03-C.md) | Décrire strates et propriétés des roches | L03-B | T03-03, T03-04 |
| [L04-A](L04/L04-A.md) | Calculer température et latitude cohérentes | L03-B | T04-01, T04-02 |
| [L04-B](L04/L04-B.md) | Transporter humidité et précipitations | L04-A | T04-03, T04-04 |
| [L04-C](L04/L04-C.md) | Établir budgets de ruissellement et recharge | L04-B, L03-C | T04-05, T04-06 |
| [L05-A](L05/L05-A.md) | Résoudre dépressions et topologie de drainage | L03-B | T05-01, T05-02, T05-04 |
| [L05-B](L05/L05-B.md) | Accumuler débits et transferts | L05-A, L04-C | T05-03 |
| [L05-C](L05/L05-C.md) | Publier exutoires et budgets de raffinement | L05-B, L02-C | T05-05, T05-06 |
| [L06-A](L06/L06-A.md) | Implémenter incision et uplift déterministes | L05-C, L03-C | T06-01, T06-03 |
| [L06-B](L06/L06-B.md) | Ajouter transport, dépôts et boucle couplée | L06-A | T06-02, T06-04 |
| [L06-C](L06/L06-C.md) | Raffiner sans détruire les contraintes | L06-B | T06-05, T06-06 |
| [L07-A](L07/L07-A.md) | Construire trajectoires et sections fluviales | L06-C | T07-01, T07-02 |
| [L07-B](L07/L07-B.md) | Construire lacs, côtes et deltas | L07-A | T07-03, T07-04, T07-05 |
| [L07-C](L07/L07-C.md) | Qualifier la matérialisation des fluides | L07-B, L11-A | T07-06 |
| [L08-A](L08/L08-A.md) | Planifier les réseaux de cavernes | L03-C, L05-C | T08-01, T08-03 |
| [L08-B](L08/L08-B.md) | Composer les volumes souterrains | L08-A | T08-02, T08-06 |
| [L08-C](L08/L08-C.md) | Valider navigation et eau souterraine | L08-B, L07-B | T08-04, T08-05 |
| [L09-A](L09/L09-A.md) | Sélectionner des sites rares connectés | L08-C | T09-02, T09-03 |
| [L09-B](L09/L09-B.md) | Composer les cinq familles fantastiques | L09-A | T09-01, T09-06 |
| [L09-C](L09/L09-C.md) | Certifier les accès après toutes les passes | L09-B, L11-C, L12-A | T09-04, T09-05 |
| [L10-A](L10/L10-A.md) | Écrire manifeste et stockage versionné | L01-B | T10-01, T10-02 |
| [L10-B](L10/L10-B.md) | Construire scheduler et caches bornés | L10-A, L01-C | T10-04, T10-05 |
| [L10-C](L10/L10-C.md) | Qualifier reprise après crash et arrêt | L10-B, L11-B | T10-03, T10-06 |
| [L11-A](L11/L11-A.md) | Brancher les hooks et cartes natives | L00-C, L02-C, L01-B | T11-01, T11-02 |
| [L11-B](L11/L11-B.md) | Assembler la colonne complète | L11-A, L07-C, L08-C, L09-B, L10-B | T11-03 |
| [L11-C](L11/L11-C.md) | Qualifier contenu vanilla, spawn et serveur | L11-B, L12-A, L10-C | T11-04, T11-05, T11-06 |
| [L12-A](L12/L12-A.md) | Réaliser assets et palettes fantastiques | L09-B | T12-01 |
| [L12-B](L12/L12-B.md) | Exposer profils et configuration validée | L02-C, L10-A | T12-02, T12-03 |
| [L12-C](L12/L12-C.md) | Finaliser aperçu, diagnostics et preuves | L01-C, L06-C, L08-B, L09-B, L11-B | T12-04, T12-05, T12-06 |
| [L13-A](L13/L13-A.md) | Exécuter la campagne et la revue des paysages | L09-C, L11-C, L12-C, L10-C | T13-01, T13-02 |
| [L13-B](L13/L13-B.md) | Qualifier performances et parties complètes | L13-A, L12-B | T13-03, T13-04 |
| [L13-C](L13/L13-C.md) | Préparer et auditer la distribution V1 | L13-B, L12-A | T13-05, T13-06 |
