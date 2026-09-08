# ISRWorldGen — synthèse de la référence 1.2
## Objectif
Produire des mondes variés, crédibles et intéressants à explorer, avec continents, océans, reliefs, bassins versants, ruisseaux, rivières, fleuves, lacs et cavernes. Le maillage Voronoï/Delaunay structure les calculs ; il ne doit pas apparaître sous forme de biomes polygonaux ou de montagnes systématiques sur ses arêtes. Le détail aléatoire reste subordonné à la géographie.

## Périmètre confirmé
Les cinq distributions sont des exigences de la V1 : **strates rocheuses, sols et fertilité, minerais et gisements, végétation, neige et glace**. Elles partagent climat, terrain et géologie ; elles ne sont pas cinq cartes indépendantes. Les ressources, blocs, usages, comportements agricoles et progression natives sont conservés selon la cible auditée, sans reproduire forcément la distribution exacte de la seed vanilla.

Le périmètre existant est conservé : cavernes géologiques ; sites fantastiques rares intégrés aux galeries et certifiés accessibles ; persistance ; configuration ; diagnostics ; structures/spawn ; solo et serveur dédié qualifiés. Le catalogue fantastique déjà prévu dans S09 n’est pas supprimé par cette mise à jour. Les détails artistiques, ratios de rareté et budgets sont des paramètres/choix à qualifier, pas des résultats acquis.

## Architecture
Atlas global borné → géologie volumique et relief initial → climat et bilan hydrique avec stockage neigeux → drainage et ports partagés → boucle bornée d’érosion/dépôts → raffinement conservatif. Les sols sont issus de cette surface ; les gisements sont conditionnés par la géologie ; la végétation par le contexte écologique. Cavernes et masques de ressources partagent le même sous-sol. Les chunks ne font que matérialiser les descriptions canoniques.

La génération structurelle est indépendante de la date de première visite. L’état saisonnier, les cultures, l’exploitation minière et les constructions suivent le temps du jeu et ne sont pas régénérés depuis l’atlas. Le gel/dégel conserve le type d’eau, les niveaux et la sémantique de courant ; la neige temporaire n’efface pas le sol.

## Statut des décisions
Acquis : ISRWorldGen, C#, Visual Studio Community 2026, template déjà installé, MCP Visual Studio, modèle hiérarchique et exigences d’exploration/compatibilité. Les algorithmes exacts, seuils, tables de profils et formes supplémentaires sont des décisions de réalisation à justifier par tests. Les noms de contrats C08-C12 sont internes au mod, pas supposés natifs.

L’utilisateur indique un développement à **L05-C**. Cette version complète le plan à cet endroit, sans redémarrage global, renumérotation ni effacement d’historique. Les nouveaux L14-L18 s’intercalent selon leurs dépendances ; leur numéro ne signifie pas qu’ils attendent la fin de L13.

## Limites
Pas de tectonique active en partie, hydraulique physique universelle, érosion des constructions, migration automatique d’anciens terrains ou moteur de rendu obligatoire. Pas de refonte automatique de l’agriculture ni d’IA animale dans ces lots. Un glacier dynamique, banquise physique ou nouveaux filons complexes sont des extensions candidates, non des dépendances imposées. ISRTreeGen peut rester dans une solution commune mais ses assets/générateurs admis demeurent utilisables sans le générateur de monde.

## Validation
La livraison finale reste un mod compilé et distribuable avec preuves C#, tests en jeu via MCP lorsque requis, corpus indépendant, inspection humaine et mesures Release. Cette livraison-ci est documentaire : elle n’a pas exécuté le mod ni audité le dépôt actif. Les 48 nouveaux scénarios sont NOT_RUN ; les résultats historiques du projet sont à préserver.
