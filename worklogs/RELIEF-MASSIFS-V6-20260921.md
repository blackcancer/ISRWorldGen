# Relief brut v6 — volumes montagneux, pas seulement crêtes

Base réconciliée : b5719689624c648e70df63ad38f12bcf6f4276df. Le commit préparatoire 28188530 n'a PAS été intégré : sa mise à jour de main a été refusée car non fast-forward. La présente modification conserve intégralement le RawRidgeNetwork v5 arrivé sur main, AdvancedRidgeChecks et ses budgets. Aucune branche n'est forcée.

## Constat vérifié
Les PNG16/float64 réels du run 35590136121 sur le domaine complet 262144², seed -437287116, ont été lus. Les ramifications sont moins appariées, mais une arête dominante étroite sur un socle presque uniforme subsiste. L'acceptation géographique demeure NON_ACCEPTE ; aucune demande de validation utilisateur.

## Changement complémentaire
Seul RawReliefModel change. Les couloirs orogéniques passent à une largeur physique de base 6000..11000 avant modulation et les sutures héritées à 5500..10000. Un soulèvement plus large soutient les crêtes. Un détail multiscalaire lié aux massifs casse la régularité longitudinale ; les basses provinces utilisent un champ plus calme au lieu du même réseau de rides. La branche marine ne reçoit plus la copie affaiblie des contreforts terrestres : elle conserve sa propre dorsale, fosse et bathymétrie.

Il s'agit de corrections du champ C# solide, pas d'un traitement d'image. Algorithme raw-structural-relief-v6-broad-supported-orogens. Les mêmes trois seeds, mêmes deux mondes complets 131072/262144 et mêmes bornes PNG16 sont conservés. L'évolution n'est pas une simulation de tectonique historique.

## Preuves à lire après CI
Raw relief and Earth references doit recompiler le Core et exécuter la géométrie réelle, les tests de répétabilité/contamination/provenance et produire les 6 heightmaps complètes. Les références ETOPO2022 originales, les métadonnées et les sources restent conservées. Aucun PASS géographique ne découle de cette exécution. Comparer la largeur/continuité des massifs et les pentes relatives par rapport aux ensembles Alpes-Pô-Ligurien, Andes-marge chilienne et Norvège-plateforme-bassin ; unités Terre et blocs distinctes.

Erosion, exécution native, MCP et revue indépendante : NOT_RUN. Aucun changement de l'API du jeu, des sauvegardes, du catalogue de profils joueur ou du registre d'avancement. Le candidat brut reste explicitement sélectionné par la campagne hors jeu.
