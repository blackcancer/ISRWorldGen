# T-AQ-L17 — Scénarios aquatiques


Les scénarios ci-dessous sont des spécifications, **NOT_RUN pour le mod dans cette livraison**. Utiliser des fixtures analytiques indépendantes, des témoins natifs et des budgets gelés ; ni round-trip de ses propres sorties ni simple capture ne suffisent. Rejouer les effets finaux après toutes les passes concernées.

Preuves communes : baseline du code/DLL/assets, seed et versions, dimensions et emprises, protocole/commandes, assertions positives et négatives, statut réel, mesures et journaux. Ajouter PNG/coupes + manifeste conformément à la cartographie de recette pour les sorties spatiales pertinentes. Performance en Release sans debugger ; sauvegardes jetables uniquement. Un essai non exécuté n’est jamais déclaré réussi.


## T-AQ-01 — Catalogue des peuplements submergés

Exigence : R-AQ-01. Responsable de qualification : **L17-A**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Exporter les codes et références des blocs/plantes, coraux, décors et patches sur la cible auditée ; inventorier eau douce et marine.

**Accepter seulement si.** Chaque catégorie présente possède support, type d’eau, profondeur, mode de placement, comportement et sortie récoltable documentés ; catégories absentes justifiées.

**Témoin défectueux obligatoire.** Retirer une catégorie native présente du routage : l’audit de couverture doit échouer.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-04 — Support sous-marin distinct de la fertilité agricole

Exigence : R-AQ-04. Responsable de qualification : **L17-B**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Contraster roche, sable/dépôt et support fertile réellement présents ; vérifier candidats et appels natifs sur des profondeurs identiques.

**Accepter seulement si.** Compatibilité du support démontrée par catégorie ; absence de chiffre de fertilité détourné pour forcer tous les fonds à accepter toute plante.

**Témoin défectueux obligatoire.** Appliquer la même fertilité positive à tous les supports : une fixture contrastée révèle le contournement.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-05 — Distribution par milieux et gradients

Exigence : R-AQ-05. Responsable de qualification : **L17-B**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Matrice marine/eau douce/transition, faible/grande profondeur, fond plat/pente et climats contrastés ; budgets de distribution gelés avant holdout.

**Accepter seulement si.** Contraintes du profil et natives respectées, transitions et densités conformes ; proxies nommés, pas de température d’eau fictive ; revue de cartes et coupes.

**Témoin défectueux obligatoire.** Forcer la même couverture à toutes profondeurs ou déclarer le climat atmosphérique température mesurée de l’eau : échec.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-06 — Emprise et déterminisme des patches

Exigence : R-AQ-06. Responsable de qualification : **L17-C**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Algues et récifs natifs aux coins de quatre chunks, pentes et voisins concurrents ; inverser ordres/threads ; instrumenter RNG et écritures.

**Accepter seulement si.** Identités et plans/placements ISR déterministes à versions égales ; pas de coupure, doublon, débordement ni halo non borné. Les identités d’animaux vivants ne servent pas d’oracle.

**Témoin défectueux obligatoire.** Réinitialiser le RNG par ordre d’arrivée ou écrêter à la frontière sans rejet : comparaison et mesure d’emprise échouent.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-07 — Placement natif et reçu effectif

Exigence : R-AQ-07. Responsable de qualification : **L17-C**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Tester placement autorisé, profondeur aux limites, support refusé, absence de place et eau douce/salée ; inspecter deltas et état après ticks.

**Accepter seulement si.** Pas de conversion d’eau injustifiée, plante flottante ou fluide détruit ; reçu cohérent avec blocs/états effectivement modifiés, pas seulement le booléen de retour.

**Témoin défectueux obligatoire.** Déclarer GeneratedBaseline sur un retour true sans aucune écriture, ou perdre la couche liquide : contrôle échoue.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-08 — Récifs, fond final et zones protégées

Exigence : R-AQ-08. Responsable de qualification : **L17-C**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Récif près d’un chenal, d’une entrée protégée et d’un autre patch ; comparer fond C03, deltas L17 et métadonnées natives attendues.

**Accepter seulement si.** Transformations dans une enveloppe autorisée, aucun volume protégé détruit ; fond de référence et occupation finale distingués ; incident natif traité par rejet/adaptation bornée.

**Témoin défectueux obligatoire.** Permettre le remplacement d’un bloc réservé ou publier l’ancien fond comme fond final : assertion de protection/provenance échoue.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-09 — Récolte et permanence des modifications

Exigence : R-AQ-09. Responsable de qualification : **L17-D**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Récolter, enlever le support et effectuer les interactions natives disponibles ; décharger/recharger et traverser le cycle applicable sur monde jetable.

**Accepter seulement si.** Drops/interactions conformes aux assets, modifications conservées et absence de double génération ; toute repousse réellement native est distinguée d’un reseed du mod.

**Témoin défectueux obligatoire.** Relancer les patches à chaque chargement : la réapparition non autorisée est détectée.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.
