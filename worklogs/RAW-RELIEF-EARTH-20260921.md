# Reprise brute — comparaison terrestre

Base : 5187715b10869ae3a815ac17a987c99782050e2d. Mandat : travail sur main, relief brut d'abord, vraie heightmap sous la mer, acceptation géographique par l'assistant et non par l'utilisateur.

Candidat : RawReliefModel, nouvelle sélection explicite. Enveloppes crustales et frontières de plaques réellement chargées ; déformation continue des coordonnées ; détail de crêtes limité aux ceintures ; plateforme et pente sous-marines. Aucun appel à l'érosion, au climat, à l'API du jeu ou aux sauvegardes dans cette campagne. Aucune nouvelle dépendance du mod.

Les seeds -437287116, 73 et 20260906 couvrent chacune 131072 x 131072 blocs, avec 1024 x 1024 vrais échantillons. Test de provenance, ordre, concurrence, domaine, conservation du fond marin, contrôle de fenêtres. Le float64 fait foi ; PNG 16 bits : Y=code*383/65535. Pas de normalisation image par image.

Références observées demandées à NOAA : trois emprises prédéfinies, réponses sources, coordonnées, unités, métriques et empreintes conservées. Des métriques réussies n'acceptent pas la géographie. Une erreur réseau est publiée comme échec de comparaison, jamais remplacée par du bruit synthétique.

Avant soumission : tests Python des encodeurs, orientation des grilles et pentes physiques passés sur fixtures ; C# à exécuter dans Actions. Aucun SDK local ni MCP natif utilisés. Revue indépendante et jeu NOT_RUN. Le statut géographique reste NOT_EVALUATED jusqu'à inspection réelle des sorties et des données terrestres. Aucune clôture de lot.
