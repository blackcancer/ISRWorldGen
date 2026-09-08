# T-AQ-L19-Requetes — Scénarios aquatiques


Les scénarios ci-dessous sont des spécifications, **NOT_RUN pour le mod dans cette livraison**. Utiliser des fixtures analytiques indépendantes, des témoins natifs et des budgets gelés ; ni round-trip de ses propres sorties ni simple capture ne suffisent. Rejouer les effets finaux après toutes les passes concernées.

Preuves communes : baseline du code/DLL/assets, seed et versions, dimensions et emprises, protocole/commandes, assertions positives et négatives, statut réel, mesures et journaux. Ajouter PNG/coupes + manifeste conformément à la cartographie de recette pour les sorties spatiales pertinentes. Performance en Release sans debugger ; sauvegardes jetables uniquement. Un essai non exécuté n’est jamais déclaré réussi.


## T-AQ-14 — Requêtes réellement tridimensionnelles

Exigence : R-AQ-14. Responsable de qualification : **L19-E**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Tester mer et cavité superposées, lac/berge, rive sinueuse dont le centroïde est éloigné, coordonnées négatives et limites numériques.

**Accepter seulement si.** Bon compartiment, bonne tranche, métrique publiée et ordre stable ; boîte de l’index non confondue avec l’emprise ; aucune fusion de dimensions.

**Témoin défectueux obligatoire.** Utiliser uniquement XZ ou le centroïde d’une rive : une fixture indépendante produit un résultat différent.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-15 — Connexions connues sans fausse navigabilité

Exigence : R-AQ-15. Responsable de qualification : **L19-E**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Graphe analytique lac/chenal/chute/zone temporaire, frontière inconnue et obstacle décrit ; comparer liens et quotas à un oracle indépendant.

**Accepter seulement si.** Sens, nature, résolution et caractère saisonnier préservés ; un lien hydraulique ne vaut pas passage, un voisinage incomplet n’est pas un bassin fermé.

**Témoin défectueux obligatoire.** Transformer toute arête en lien de nage bidirectionnel ou la lacune de coverage en impasse définitive : échec.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-19 — Couverture, quotas et absence de génération implicite

Exigence : R-AQ-19. Responsable de qualification : **L19-E**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Petite et grande emprise, océan étendu, trous verticaux, quotas faibles, annulation et cache froid ; compter pages/objets/octets et appels moteur causés.

**Accepter seulement si.** Limites respectées, réponses explicites, ordre stable sur portion examinée ; zéro chargement/génération de chunks, aucune exploration de tout l’océan.

**Témoin défectueux obligatoire.** Renvoyer Complete malgré quota ou charger des voisins pour combler un trou : le compteur/oracle échoue.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-22 — Cartographie, coupes et provenance

Exigence : R-AQ-22. Responsable de qualification : **L19-E**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Produire bathymétrie, type d’eau, substrat, peuplements, connexions, coverage et au moins une coupe de volumes superposés ; palettes figées.

**Accepter seulement si.** PNG, manifeste, unités, origine, résolution, seed/version/commit, hash et oracle cohérents ; zéro/inconnu/masqué distincts ; atlas serveur protégé.

**Témoin défectueux obligatoire.** Inverser Y, remplir un trou par zéro ou normaliser chaque carte séparément pour cacher une discontinuité : revue/contrôles détectent le défaut.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.
