# Synthèse du cahier des charges
## Finalité
Le mod doit produire des mondes crédibles et agréables à explorer dans Vintage Story, sans remplacer les motifs visibles actuels par une mosaïque de polygones. Les reliefs, les eaux, les roches, le climat et les réseaux souterrains doivent partager une même organisation. Le résultat recherché est un réalisme condensé pour le jeu, pas une simulation scientifique exhaustive de la Terre.

Le maillage Voronoï/Delaunay est une structure de calcul. Il n’impose ni une cellule par biome, ni une montagne par frontière, ni une rivière suivant chaque arête. Les grands paysages doivent dépendre des structures géologiques et des bassins versants. Un bruit éventuel ne peut qu’enrichir le détail, sans décider seul des continents ou couper un cours d’eau.

## Périmètre de la première version complète
La version 1 comprend les continents et océans avec bathymétrie, les reliefs de plusieurs familles, un climat simplifié, des bassins connectés, des ruisseaux, rivières et fleuves, des lacs avec exutoires ou bassins fermés explicitement justifiés, ainsi que des cavernes géologiques. Elle comprend aussi des paysages souterrains fantastiques rares et connectés, la configuration, la persistance, les diagnostics, l’intégration des ressources et de la survie vanilla et la qualification solo/serveur.

Les cinq familles fantastiques proposées dans la discussion — forêt fongique, cathédrale de cristaux, lac intérieur, gouffre à ponts et jardin minéral — constituent le catalogue initial à réaliser. Leur exécution artistique pourra être ajustée sans retirer les garanties d’accès, la rareté ou la diversité. Le gigantisme n’est pas une condition : les volumes doivent respecter le budget vertical réel du monde.

Sont exclus de V1 : une tectonique active pendant la partie, une mécanique des fluides complète réagissant physiquement à chaque barrage, l’érosion qui détruit les constructions du joueur, la migration automatique de terrains existants, un moteur de rendu/shaders personnalisé obligatoire et une compatibilité universelle avec tous les mods de génération. Une extension future peut traiter ces sujets séparément.

## Architecture retenue
Un **atlas global borné en mémoire** est préparé à la création du monde. Il contient les grandes structures et les connexions majeures. Des solveurs régionaux/bassins raffinent les données avec des conditions aux limites publiées. La génération des chunks matérialise ensuite une portion de descriptions immuables. Une limite de cache n’est jamais une ligne de partage des eaux.

Le pipeline de calcul comporte une boucle bornée climat–drainage–érosion avant publication. Les données d’une itération sont distinctes des suivantes ; après publication, aucun affluent tardif ne peut modifier un fleuve déjà construit. Le détail redistribue un budget hydrologique existant plutôt que de créer de l’eau supplémentaire.

Les cavernes sont planifiées sous forme de réseaux puis converties en volumes. Les sites fantastiques sont intégrés à ces réseaux avant la voxelisation, avec des corridors réservés et une validation finale après les décorations. Un site connecté seulement dans le graphe mais muré dans les blocs est invalide.

## Décisions acquises et choix de démarrage
Les besoins de génération, le caractère rare et connecté du fantastique, C#, Visual Studio Community 2026 et l’usage du MCP sont acquis. `WorldGen/worldgen`, les profils de dimensions, la densité exacte des merveilles et les budgets de performance sont des propositions de démarrage, identifiées comme telles. Ils ne sont pas présentés comme des choix déjà validés par l’utilisateur.

La documentation en ligne consultée correspond à Vintage Story 1.22.7. La version installée, son runtime, les références du template et le MCP doivent être relevés dans L00. Il est interdit de reprendre implicitement la cible d’un autre projet ou de choisir un framework parce que l’IDE est récent. Voir [l’audit](04-AUDIT-API.md).

## Ce qui constitue une livraison
Le dossier final doit inclure un mod compilé et empaqueté, ses sources et données, un manifeste des dépendances, une documentation utilisateur FR/EN minimale, les profils de test, une matrice de compatibilité et les preuves de recette. Les jalons précédents sont des prototypes internes, même si un aperçu de terrain est déjà visible.

La validation complète exige des tests automatisés et une inspection humaine dans le jeu et sur sa carte. Elle n’exige pas une preuve universelle d’absence de tout motif sur toutes les seeds ; elle exige des mesures de détection, un corpus fixé, des tests de raccords stricts et une revue contradictoire des paysages.
