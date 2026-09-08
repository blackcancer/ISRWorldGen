# T-AQ-L19-Qualification — Scénarios aquatiques


Les scénarios ci-dessous sont des spécifications, **NOT_RUN pour le mod dans cette livraison**. Utiliser des fixtures analytiques indépendantes, des témoins natifs et des budgets gelés ; ni round-trip de ses propres sorties ni simple capture ne suffisent. Rejouer les effets finaux après toutes les passes concernées.

Preuves communes : baseline du code/DLL/assets, seed et versions, dimensions et emprises, protocole/commandes, assertions positives et négatives, statut réel, mesures et journaux. Ajouter PNG/coupes + manifeste conformément à la cartographie de recette pour les sorties spatiales pertinentes. Performance en Release sans debugger ; sauvegardes jetables uniquement. Un essai non exécuté n’est jamais déclaré réussi.


## T-AQ-18 — Installation tardive et migrations

Exigence : R-AQ-18. Responsable de qualification : **L19-F**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Générer/sauvegarder avec L19 1.5 sans consommateur ; l’ajouter puis interroger zones déchargées après restart. Comparer à un monde ancien sans extension.

**Accepter seulement si.** Données 1.5 retrouvées sans chargement ; ancien monde migré seulement depuis métadonnées exactes ou signalé LegacyMissing ; aucun terrain rejoué.

**Témoin défectueux obligatoire.** Recalculer l’ancien monde avec les nouveaux paramètres pour remplir le catalogue : instrumentation/version détecte l’erreur.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-20 — État historique après modification et saison

Exigence : R-AQ-20. Responsable de qualification : **L19-F**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Sur monde jetable retirer une algue, boucher un chenal, construire près de la rive et traverser le gel/dégel ; interroger puis comparer aux blocs actuels.

**Accepter seulement si.** Historique et phase source conservés, contrôle courant exigé ; aucune affirmation de nage, nourriture ou glace actuelle non observée ; pas de restauration des plans.

**Témoin défectueux obligatoire.** Rafraîchir ObservedAt avec l’heure de requête sans lire le milieu, ou rouvrir le chenal : test échoue.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-21 — Consommateur intermods aquatique indépendant

Exigence : R-AQ-21. Responsable de qualification : **L19-F**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Avec seulement le contrat/guide L19-C, réaliser trois parcours de requêtes : eau douce, marine, terre/eau ; inclure profondeur et connexion connue.

**Accepter seulement si.** Compilation/chargement puis réponses correctes sur jeu/serveur ; aucun accès Core/Runtime ; fournisseur absent/incompatible géré ; aucune donnée serveur diffusée automatiquement.

**Témoin défectueux obligatoire.** Retirer le fournisseur, dupliquer un contrat incompatible ou ajouter une référence interne : les tests doivent révéler l’erreur.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-23 — Budget et non-influence du socle

Exigence : R-AQ-23. Responsable de qualification : **L19-F**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Release hors debugger, chaud/froid, requêtes concurrentes bornées et littoral/océan dense ; même générateur 1.5 avec/sans collecte L19 pour la comparaison.

**Accepter seulement si.** Budgets approuvés avant holdout et respectés ; données/plans L17 identiques hors métadonnées L19 ; pas d’exigence d’identités de populations natives figées.

**Témoin défectueux obligatoire.** Comparer au terrain 1.4 pour interdire tout changement L17, partager son RNG avec L19 ou assouplir les seuils après échec : protocole refusé.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.
