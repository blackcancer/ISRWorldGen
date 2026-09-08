# T-AQ-L07 — Scénarios aquatiques


Les scénarios ci-dessous sont des spécifications, **NOT_RUN pour le mod dans cette livraison**. Utiliser des fixtures analytiques indépendantes, des témoins natifs et des budgets gelés ; ni round-trip de ses propres sorties ni simple capture ne suffisent. Rejouer les effets finaux après toutes les passes concernées.

Preuves communes : baseline du code/DLL/assets, seed et versions, dimensions et emprises, protocole/commandes, assertions positives et négatives, statut réel, mesures et journaux. Ajouter PNG/coupes + manifeste conformément à la cartographie de recette pour les sorties spatiales pertinentes. Performance en Release sans debugger ; sauvegardes jetables uniquement. Un essai non exécuté n’est jamais déclaré réussi.


## T-AQ-03 — Fond, niveau local et compartiments verticaux

Exigence : R-AQ-03. Responsable de qualification : **L07-B**. Portée : **V1**. Statut livré : **NOT_RUN**.

**Préparer et exécuter.** Fixtures : lac en altitude, océan, chenal, delta et cavité noyée superposée ; références de L08 pour les objets souterrains déjà produits.

**Accepter seulement si.** Identités et profondeurs cohérentes, sans confusion XZ ; connexions à leur résolution ; pas de substitution du niveau marin global au niveau du lac.

**Témoin défectueux obligatoire.** Fusionner deux eaux au même XZ ou calculer le lac depuis le niveau marin : les assertions échouent.

**Passation.** Conserver les preuves communes, la fixture, le résultat du témoin et les régressions affectées. Les contributeurs amont ne clôturent pas le scénario final à la place du responsable.
