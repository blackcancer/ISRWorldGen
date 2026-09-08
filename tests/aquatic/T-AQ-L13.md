# T-AQ-L13 — Scénarios aquatiques


Les scénarios ci-dessous sont des spécifications, **NOT_RUN pour le mod dans cette livraison**. Utiliser des fixtures analytiques indépendantes, des témoins natifs et des budgets gelés ; ni round-trip de ses propres sorties ni simple capture ne suffisent. Rejouer les effets finaux après toutes les passes concernées.

Preuves communes : baseline du code/DLL/assets, seed et versions, dimensions et emprises, protocole/commandes, assertions positives et négatives, statut réel, mesures et journaux. Ajouter PNG/coupes + manifeste conformément à la cartographie de recette pour les sorties spatiales pertinentes. Performance en Release sans debugger ; sauvegardes jetables uniquement. Un essai non exécuté n’est jamais déclaré réussi.


## T-AQ-24 — Recette V1 des milieux aquatiques

Exigence : R-AQ-24. Responsable de qualification : **L13-A**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Corpus figé, holdout et itinéraire surface/submergé : rivière, embouchure, côte, fond et peuplement ; couvrir serveur, reprise et API externe.

**Accepter seulement si.** Exigences applicables couvertes, régressions/protections satisfaites et NOT_RUN visibles ; exemptions d’assets absents limitées et motivées, jamais exemption des minima de données.

**Témoin défectueux obligatoire.** Clore G5 avec seul audit L19-C, omettre T-AQ ou attendre L20 : le contrôle de gate rejette la qualification.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.
