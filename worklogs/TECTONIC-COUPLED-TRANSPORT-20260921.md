# Transport tectonique couplé — incrément borné

Base : be40b872584744f3289d040bd05d514f8289cb1d. Périmètre : transport numérique du modèle expérimental Evolution, contrôles dédiés et campagne ; aucun changement de l'API Vintage Story, de sauvegarde, de dépendance ni de statut de lot. Aucune érosion. Revue de code indépendante : NOT_RUN.

## Défaut précédent clos séparément
Le commit be40b872 corrige la décision de polarité qui amplifiait les arrondis d'âges presque identiques. Le run 35605225740 est réussi, y compris sa comparaison Windows/Linux. Ses tolérances n'ont pas été élargies. Ne pas confondre cette correction avec une validation géographique.

## Méthode optionnelle
Le schéma donneur d'ordre un reste la valeur par défaut. CoupledMusclV1 ajoute une reconstruction minmod commune aux quatre grandeurs extensives (croûte continentale, océanique, moment d'âge et cohorte héritée), avec SSPRK2. Le complément océan neuf participe au limiteur. Les flux, la positivité, les bilans et les relations entre les grandeurs sont contrôlés. Aucun volume ni aucune altitude n'est écrêté pour faire passer un contrôle. Le paramètre est inclus dans l'identité de l'histoire. La comparaison concerne le transport, pas un recalage du niveau marin.

## Test et campagne
Dix contrôles nommés complètent les contrôles existants : traduction analytique comparée au donneur, conservation de quatre inventaires, conservation des rapports, discontinuité, mouvement nul, tableaux non aliasés, permutation métrique des axes, entrées invalides, redimensionnement intégral et état initial identique entre schémas. Une référence Python locale de traduction donne une erreur L1 de 0.0544328577 contre 0.1638268172 pour le donneur ; ceci n'est pas une exécution C#.

Le workflow conserve les trois seeds et les 512² cellules du monde complet de référence de 1 000 000 unités. Matrice Windows/Linux × donneur/couplé, puis comparaison interplateforme séparée pour chaque schéma. L'égalité des données de l'atlas entier est vérifiée pour son application aux mondes de 131072, 262144 et 1000000 blocs ; ce n'est pas un recadrage. Les résultats C# réels et les cartes seront ceux des artefacts du run de ce commit, pas présumés PASS dans cette note de préparation.

## Garde et récupération
Publication demandée par l'utilisateur sur main : objets Git non destructifs, base vérifiée, déplacement non forcé uniquement. En cas de concurrence sur main, réconcilier les seuls fichiers concernés. Dossiers de preuve nouveaux, refus de réécriture de campagnes existantes. Runners hébergés sans secrets de jeu, permissions contents:read, artefacts séparés par méthode et plateforme. Ne pas lancer une autre évolution morphologique avant le verdict de cette campagne ; sur échec, corriger le contre-exemple ou retirer le schéma optionnel plutôt que réduire le contrôle.

## Limites de modèle inchangées
Cinématique imposée sur un atlas préparatoire périodique, pas résolution d'un équilibre des forces du manteau. La création et le recyclage de croûte sont comptabilisés ; la flexure des fosses, les arcs et la reconstruction lagrangienne rigide restent absents. Résolution réelle 512² (1953.125 blocs au pas monde 1M) : aucun détail inventé par agrandissement. Géographie NOT_ACCEPTED ; érosion suspendue jusqu'à une revue du relief et de la bathymétrie.