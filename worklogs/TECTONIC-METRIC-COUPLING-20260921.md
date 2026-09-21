# Couplage métrique des vitesses transportées

Base : ec6eb3596788c978ee3cbe2e03175b47575cb243. Le run 35611675685 a compilé le Core et réussi 43 contrôles dans le job Linux material 106372081676, puis refusé l'histoire entière 512² : croûte continentale >150 km équivalents. Cet essai n'est PAS accepté. La limite et la conversion verticale restent inchangées.

La fermeture locale vitesse=fraction transportée peut concentrer la convergence comme un choc d'advection. Le candidat de correction réintroduit une portée spatiale de couplage : deux fois DeformationWidth, appliquée uniquement aux vitesses prescrites mélangées, jamais aux hauteurs, épaisseurs ou quantités de matière. Trois moyennes métriques séparables approchent un noyau gaussien ; les poids positifs préservent la vitesse uniforme et l'enveloppe convexe. Le support traverse les deux côtés du contact matériel, sans remettre le centre Voronoï mobile en autorité.

Il s'agit d'une fermeture cinématique réduite déclarée, pas d'une viscosité lithosphérique identifiée ni d'un équilibre de forces. Sa portée n'est pas calibrée aux données terrestres. Son ID entre dans les paramètres et l'identité de l'histoire. Les sources/recyclages, le donneur conservatif et les seuils de comparaison interplateforme restent inchangés.

Quatre tests supplémentaires vérifient constantes/zéro, atténuation du motif de Nyquist, translation périodique/enveloppe et entrées invalides. Une référence Python du noyau a été exécutée localement avec succès ; elle ne vaut pas exécution C#. La CI doit à nouveau exécuter les 47 contrôles et les trois seeds entières dans les deux modes et sur les deux plateformes. La réussite éventuelle ne sera pas un PASS géographique.

Aucun profil natif, registre, sauvegarde, API du jeu ou dépendance ajouté. Érosion, jeu, MCP Visual Studio et revue d'un second agent : NOT_RUN. Pas de repli qui repeint un relief ou masque un dépassement ; une nouvelle erreur reste à diagnostiquer et publier avec sa provenance.
