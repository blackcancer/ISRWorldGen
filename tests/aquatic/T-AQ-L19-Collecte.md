# T-AQ-L19-Collecte — Scénarios aquatiques


Les scénarios ci-dessous sont des spécifications, **NOT_RUN pour le mod dans cette livraison**. Utiliser des fixtures analytiques indépendantes, des témoins natifs et des budgets gelés ; ni round-trip de ses propres sorties ni simple capture ne suffisent. Rejouer les effets finaux après toutes les passes concernées.

Preuves communes : baseline du code/DLL/assets, seed et versions, dimensions et emprises, protocole/commandes, assertions positives et négatives, statut réel, mesures et journaux. Ajouter PNG/coupes + manifeste conformément à la cartographie de recette pour les sorties spatiales pertinentes. Performance en Release sans debugger ; sauvegardes jetables uniquement. Un essai non exécuté n’est jamais déclaré réussi.


## T-AQ-16 — Habitats et ressources candidates sans population fictive

Exigence : R-AQ-16. Responsable de qualification : **L19-D**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Peuplement submergé, fond nu, anfractuosité ou couvert géométrique, berge de transition et patch refusé ; comparer aux sources et reçus.

**Accepter seulement si.** Candidats traçables et portée historique explicite ; aucun stock comestible, nombre de poissons, ponte ou occupation déduit automatiquement.

**Témoin défectueux obligatoire.** Convertir une surface d’herbier en effectif de poissons ou déclarer un patch refusé présent : contrôle sémantique échoue.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-17 — Collecte durable sans consommateur

Exigence : R-AQ-17. Responsable de qualification : **L19-D**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Générer sites eau douce et marine sans consommateur ; vérifier fragments, publication, éviction puis lecture et injection de crash sur backend réel.

**Accepter seulement si.** Même identité/contenu publié après reprise ; aucune région partielle annoncée complète ; taille/checksum contrôlés ; aucun effet sur RNG/placements/faune.

**Témoin défectueux obligatoire.** Ne collecter que si le consommateur est chargé ou publier un manifeste avant ses données : cas positif/crash échoue.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.
