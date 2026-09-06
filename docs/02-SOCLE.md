# Socle invariant — à lire pour chaque tâche
**Normatif · v1.0.** Les identifiants R-* sont définis dans les fiches de spécification ; les T-* dans les fiches de test. « Doit » signifie obligatoire pour la recette correspondante.

## Produit
C# ; Visual Studio Community 2026 ; template de mod déjà installé ; MCP Visual Studio pour diagnostiquer le processus réel. Cœur CPU sans référence au jeu, adaptateur Vintage Story séparé. Le framework et les dépendances sont verrouillés après audit local, pas déduits de l’année de l’IDE. Nom provisoire : WorldGen, modid provisoire : worldgen.

Le monde comprend un atlas global, un raffinement par bassins/régions et une matérialisation voxel. Géologie, relief, climat et eau sont couplés. Voronoï reste invisible comme grille. Le bruit ne commande pas les grandes structures. Une simulation hors-jeu simplifiée prépare des paysages ; elle ne tourne pas continuellement et ne régénère pas les constructions.

## Déterminisme et frontières
La génération dépend de la seed effective, des paramètres gelés, de la version algorithmique et des données d’actifs pertinentes. Aucun résultat ne dépend de l’ordre des chunks, des threads, des caches, des coordonnées de l’explorateur ou du temps machine. Les comparaisons, départages et réductions ont un ordre stable. La reproductibilité binaire porte sur les plateformes/runtime qualifiés ; ne pas annoncer une universalité non testée.

Coordonnées du cœur en entiers larges, métrique physique/modèle explicitée, conversions centralisées. L’adaptateur lit les tailles réelles du monde/chunk/région. Aucun `32`, `512`, niveau marin ou offset de spawn dispersé dans le code métier. Des voisinages de calcul ne remplacent pas les contrats d’exutoire/débit/corridor aux frontières.

## Eau et cavernes
Distinguer relief, surface d’eau, dépressions, océan connecté, eau douce et eau saline. Les débits sont conservés avec pertes/réserves explicitement comptées. Le détail ne double pas les apports. Les petites pentes sont quantifiées sans créer d’écoulement montant ni de barrage involontaire.

Les cavernes naturelles suivent des familles géologiques et comportent des réseaux intéressants. **Tout site fantastique publié doit avoir une galerie de connexion réellement praticable après voxelisation et décoration.** Par défaut proposé, cette galerie appartient à un réseau relié à la surface, sans minage aléatoire obligatoire ; une difficulté verticale intentionnelle reste possible via un profil d’équipement défini. Un test de simples cellules d’air ne remplace pas le test du gabarit du joueur. Rareté spatiale, diversité et absence de placement opportuniste sont obligatoires.

## Intégrité et acceptation
Nouvelle sauvegarde dédiée uniquement pour V1. Ne pas remplacer silencieusement le générateur d’un monde ancien. Manifeste persistant, configurations gelées, snapshots et caches versionnés. En cas de données incompatibles/corrompues, message explicite et arrêt sûr du chargement concerné ; pas de retour discret à vanilla.

Toutes les tâches ont des tests et des preuves. `NOT_RUN`/`BLOCKED` ne valent jamais réussite. L’intégrateur possède les contrats, le manifeste, le verrou MCP et la décision de fusion. Les agents ne publient rien et ne modifient pas une sauvegarde personnelle. Les hypothèses et seuils provisoires restent visibles et ne sont pas convertis en faits par répétition.
