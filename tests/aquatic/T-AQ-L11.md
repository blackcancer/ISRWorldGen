# T-AQ-L11 — Scénarios aquatiques


Les scénarios ci-dessous sont des spécifications, **NOT_RUN pour le mod dans cette livraison**. Utiliser des fixtures analytiques indépendantes, des témoins natifs et des budgets gelés ; ni round-trip de ses propres sorties ni simple capture ne suffisent. Rejouer les effets finaux après toutes les passes concernées.

Preuves communes : baseline du code/DLL/assets, seed et versions, dimensions et emprises, protocole/commandes, assertions positives et négatives, statut réel, mesures et journaux. Ajouter PNG/coupes + manifeste conformément à la cartographie de recette pour les sorties spatiales pertinentes. Performance en Release sans debugger ; sauvegardes jetables uniquement. Un essai non exécuté n’est jamais déclaré réussi.


## T-AQ-02 — Catalogue de faune et voies de spawn

Exigence : R-AQ-02. Responsable de qualification : **L11-A**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Exporter entités chargées, règles de milieu/support, cartes, groupes, événements et conditions par voie de spawn ; relever les composants réellement actifs.

**Accepter seulement si.** Aucune catégorie présente perdue, aucune voie réputée couverte sans preuve ; l’absence réelle d’un contenu n’est pas compensée par un animal inventé.

**Témoin défectueux obligatoire.** Omettre la voie runtime ou une condition liée au support : la matrice doit détecter la lacune.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-10 — Apparition native au worldgen

Exigence : R-AQ-10. Responsable de qualification : **L11-B**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Pour les catégories présentes, fixture positive et négative des règles natives ; exécuter les vraies passes de spawn avec trace des tentatives, refus et créations.

**Accepter seulement si.** Entrées cohérentes, espèces admissibles dans leur milieu, refus expliqués, absence de duplication par L17/L19 ; aucun modèle de spawn supplémentaire.

**Témoin défectueux obligatoire.** Désactiver une carte nécessaire ou installer un second appel de spawn : le témoin doit détecter perte ou doublement.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.


## T-AQ-11 — Apparition et persistance natives pendant le jeu

Exigence : R-AQ-11. Responsable de qualification : **L11-C**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Reprendre les catégories présentes en solo/serveur, suivre leur voie runtime à paramètres natifs et après reload ; tester eau et support inadmissibles.

**Accepter seulement si.** Pas de duplication, disparition due au raccord ou spawn hors conditions ; budgets/fréquences évalués avec protocole statistique fixé, pas une rencontre fortuite.

**Témoin défectueux obligatoire.** Ne tester que worldgen, ou rejouer la création d’entités au reload : le contrôle runtime/persistance échoue.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.
