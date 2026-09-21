# Reprise L03/L04/L05 — candidats algorithmiques, pas acceptation géographique

Base : 7e8e70b374e74487e68e06ed767a20f966d15a12 (audit), puis 3963cc35 (provenance de reproduction).
Mandat : reprendre les trois lots sur main ; le résultat visé reste une cartographie réaliste et les cartes servent au contrôle amont.

## Périmètre de cette première intégration
- L03 : champs de crêtes courbes et éperons, déterminés par les frontières convergentes scellées du vrai atlas. Influence continue au-delà du propriétaire Voronoï ; enveloppe verticale conservée.
- L04 : transport conservatif en distance physique, évaporation océanique dépendante de température, condensation de relief/refroidissement, vents pondérés par fraction d'année. Aucune pluie de fond terrestre ni injection aux bords techniques.
- L05 : Priority-Flood utilisé pour les niveaux de seuil, puis vraie pente dénivelé/distance et potentiel géodésique sur plats, sans epsilon de hauteur.

Les méthodes historiques sont conservées pour les comparaisons et l'interprétation des anciennes preuves. Les trois nouvelles révisions ont des identifiants explicites : elles ne migrent pas silencieusement les sauvegardes et ne changent aucun appel à Vintage Story. Aucun nouveau package.

Tests ajoutés : contre-exemple du parent de flood, distances diagonales, grand décalage de coordonnées, vallée analytique, plats, bassins isolés, invariance de résolution sur transect, conservation atmosphérique, inversion des vents, dépendance thermique, poids annuels, provenance/quotas/déterminisme du relief.

Statut à l'écriture : exécution C# à vérifier dans Actions ; pas de PASS anticipé. La campagne native, la revue indépendante et l'acceptation des trois lots restent NOT_RUN. Les statuts historiques ne sont pas réinitialisés. Les instantanés et ports utilisant les nouveaux champs doivent être requalifiés avant publication native.

## Limites à contrôler par les cartes
Les crêtes initiales ne remplacent pas l'évolution relief-érosion L06. Le graphe D8 conserve une discrétisation angulaire. Les dépressions remplies virtuellement ne sont pas des lacs physiquement matérialisés. Le climat reste un modèle réduit annuel sur domaine complet, pas une circulation atmosphérique terrestre.
