# Sources et portée des vérifications
**Date de consultation : 6 septembre 2026.** Les spécifications R-* sont des décisions et exigences de projet ; les références ci-dessous fondent les éléments d’API et les familles d’approches citées. Une publication ne valide ni le code futur du mod ni ses performances.

Les sources GitHub ont été lues via le connecteur disponible. Les branches mobiles sont identifiées par blob/commit quand observé ; la gate G0 doit fixer les sources correspondant à l’installation locale. Aucun fichier DLL du jeu et aucun article complet n’est redistribué.

## API-00 — Documentation Vintage Story — version de référence
Page consultée ; indique 1.22.7. Ne prouve pas la version locale.

Source : <https://apidocs.vintagestory.at/>

## API-01 — IServerEventAPI
Signatures worldgen ; recoupées avec Server/API/IServerEventAPI.cs dans vsapi, blob 13519acf4db98b73d0d3a9e60b81197401e0f09f.

Source : <https://apidocs.vintagestory.at/api/Vintagestory.API.Server.IServerEventAPI.html>

## API-02 — IWorldGenHandler
Source lue ; listes de handlers, WipeAllHandlers. Blob observé fee190d3e9e1e9e28241b87230fefeba465aea42 ; master est mobile.

Source : <https://github.com/anegostudios/vsapi/blob/master/Server/Worldgen/IWorldGenHandler.cs>

## API-03 — EnumWorldGenPass
Enums et description des passes observés ; ordre effectif à relever dans le jeu.

Source : <https://apidocs.vintagestory.at/api/Vintagestory.API.Server.EnumWorldGenPass.html>

## API-04 — GenTerra — source 1.22.7
Début de source lu : AssetsFinalize, niveau marin, inscription Terrain/standard. Commit identifié par lecture de la branche master, message version 1.22.7.

Source : <https://github.com/anegostudios/vsessentialsmod/blob/06673b67318e002332b6aa8b3d0308f11d2aba99/Systems/WorldGen/Standard/ChunkGen/1.GenTerra/GenTerra.cs>

## API-05 — IMapRegion
Propriétés de cartes et moddata ; ne fournit pas à elle seule un codec complet de toutes les cartes.

Source : <https://apidocs.vintagestory.at/api/Vintagestory.API.Common.IMapRegion.html>

## API-06 — IMapChunk
Sémantique des heightmaps et des données de colonne.

Source : <https://apidocs.vintagestory.at/api/Vintagestory.API.Common.IMapChunk.html>

## API-07 — ISaveGame
Seed int, WorldType, GetData/StoreData ; garanties transactionnelles non déduites.

Source : <https://apidocs.vintagestory.at/api/Vintagestory.API.Server.ISaveGame.html>

## API-08 — Projet VintagestoryAPI
Source lue ; TargetFramework référencé par $(FrameworkVersion). Blob observé 512c916c1e7742d537d97da9d4ba8512d12ddde4 ; versions locales à auditer.

Source : <https://github.com/anegostudios/vsapi/blob/master/VintagestoryAPI.csproj>

## DEV-01 — OpenAI — instructions AGENTS.md
Page officielle consultée, redirigée vers learn.chatgpt.com/docs/agent-configuration/agents-md ; découverte hiérarchique et limite propre aux instructions.

Source : <https://developers.openai.com/codex/guides/agents-md>

## DEV-02 — OpenAI — sous-agents
Page officielle consultée, redirigée vers learn.chatgpt.com/docs/agent-configuration/subagents ; délégation et coût des contextes indépendants.

Source : <https://developers.openai.com/codex/subagents>

## DEV-03 — Microsoft — dotnet test et VSTest
Exemples TRX ; compléter par la page dotnet-test pour choisir VSTest/MTP sur le SDK réel.

Source : <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest>

## ALG-01 — Amit Patel — Mapgen4
Présentation consultée ; maillage, évaporation, vent, pluie et rivières. Référence d’approche, pas une librairie C# imposée.

Source : <https://www.redblobgames.com/maps/mapgen4/>

## ALG-02 — Cordonnier et al. — Large Scale Terrain Generation from Tectonic Uplift and Fluvial Erosion (2016)
Notice/résumé de publication consultés ; choix du couplage, pas reprise intégrale ni promesse de performances.

Source : <https://inria.hal.science/hal-01262376>

## ALG-03 — Barnes et al. — Priority-Flood (article 2014, dépôt arXiv 2015)
Résumé consulté ; traitement des dépressions et applicabilité aux maillages irréguliers. Aucun PDF intégré à ce dossier.

Source : <https://arxiv.org/abs/1511.04463>

## ALG-04 — Barnes et al. — Fill–Spill–Merge (2021)
Page de publication consultée ; hiérarchie des dépressions et bilan du remplissage/débordement.

Source : <https://esurf.copernicus.org/articles/9/105/2021/>

## ALG-05 — Schott et al. — Terrain Amplification using Multi-scale Erosion (2024)
Présentation de recherche consultée ; raffinement conservant une cohérence hydrologique.

Source : <https://research.adobe.com/publication/terrain-amplification-using-multi-scale-erosion/>

## ALG-06 — Peytavie et al. — Procedural Riverscapes (2019)
Notice et résumé éditeur consultés ; morphologie fluviale. Les shaders/animations de la publication ne sont pas des dépendances obligatoires.

Source : <https://www.cs.purdue.edu/cgvlab/www/publications/Peytavie19CGF/>

## ALG-07 — NPS — How Mammoth Cave Formed
Page consultée ; inspiration pour galeries actives/fossiles et diversité des passages, non preuve d’un simulateur complet.

Source : <https://www.nps.gov/maca/learn/nature/how-mammoth-cave-formed.htm>

## Références historiques fournies dans la discussion
Les trois références initiales restent des pistes bibliographiques. Elles ne sont pas considérées comme des spécifications contractuelles ni comme des contenus intégralement audités lors de cette rédaction :

Amit Patel, Polygonal Map Generation for Games : <http://www-cs-students.stanford.edu/~amitp/game-programming/polygon-map-generation/>

Andrew Leach, mémoire fourni par l’utilisateur : <https://andrewlea.ch/honours.pdf>

SqueakySpacebar, Procedural Map Generation With Voronoi Diagrams : <https://squeakyspacebar.github.io/2017/07/12/Procedural-Map-Generation-With-Voronoi-Diagrams.html>

Un agent qui réutilise un code d’exemple doit examiner sa licence et ses hypothèses. Les algorithmes et idées peuvent inspirer notre implémentation ; l’accès public à un dépôt/article ne vaut pas permission de redistribuer ses assets ou tout son code sans respecter la licence.
