# Séparation matérielle — revue 8f31084c

## Code et décision

Base : 492ce5711706172e667aff15fe20a3d1f28a7eba (PR7). Branche : codex/material-cut-20260924. Code exécuté final : 8f31084c9f36fce6836f477a7b47b3f1ff3c068f. Main reste 4f31f29a1bf11d8b03276d5466947a3fa5a102d7 ; PR5/6/7 non modifiées.

Campagne finale : https://github.com/blackcancer/ISRWorldGen/actions/runs/35971073418 . Les contrôles Windows/Linux et la comparaison ont réussi ; le rapport est effectivement publié, téléchargé et reproduit localement. Conserver cette expérience dans une PR dépendante de PR7, sans fusion automatique. Aucun lot DONE ni acceptation géographique.

## Fonction ajoutée et distinction indispensable

MaterialCutTopology suit la connectivité d'une triangulation matérielle finie après des ruptures de liaisons DATÉES ET FOURNIES EN ENTRÉE. Une fissure interne arrêtée ne sépare pas nécessairement le continent. At applique ensemble les événements de même date et calcule les composantes ; la première séparation est déduite de cet historique fourni, pas d'une loi mécanique nouvelle.

Open traite deux fragments, un tracé ouvert entre deux frontières et des translations complètes à partir de la séparation. Le tracé peut comporter plusieurs segments non colinéaires. Les triangles continentaux, leurs épaisseurs et leurs origines restent conservés. Les déplacements normaux produisent les bandes océaniques conjuguées datées ; les pauses et le mouvement de dorsale restent dans la chronologie. Une translation commune ou un glissement tangentiel rectiligne ne créent pas de basalte.

ProjectTriangles réutilise la projection conservative de PR7. Les surfaces partiellement couvertes sont intégrées, les paquets gardent leurs origines/événements/flancs, et l'extérieur reste inconnu. La plasticité seule ne devient pas un événement de rupture. Aucun relief n'est dessiné ni recalculé ici.

La géométrie initiale et finale est vérifiée avant publication de l'ouverture. La conformité du maillage reste une précondition : At seul n'est pas une certification géométrique générale. Fermetures, recouvrements, tracés fermés/branchés, troisième fragment et événements ultérieurs sont refusés. Le composant ne résout ni propagation spontanée, ni pression/ténacité, ni forces après rupture, ni collision continue entre chaque instant, ni rotation des fragments ou jonction triple. Les mouvements restent prescrits.

## Exécution C# et comparaison

149 contrôles distincts par plateforme : 34 nouveaux, 33 projections conservées, 22 déformations et 60 ruptures/chronologies. Les anciens appels restent inchangés. Trois nouveaux cas sont exécutés sur chaque OS : droit, non rectiligne avec pause et passage périodique. Chaque projection couvre le cadre entier de 1000000 × 1000000 unités en 512². Le maillage source comporte 48 triangles et six liaisons rompues fournies : il ne s'agit pas de mécanique en 512² ni de trois mondes.

Les 3932160 paires de valeurs des cinq champs nouveaux (3 × 5 × 512²) sont identiques entre Windows/Linux. Les paquets, les géométries et les neuf PNG16 concordent également. Tolérance de champ 1e-8 inchangée. Les deux archives finales ont été téléchargées et la comparaison réexécutée localement ; le rapport obtenu est identique à celui de la CI. Ce résultat ne couvre pas tous les matériels/runtimes.

Artefacts finaux et SHA-256 vérifiés :
- Linux 10795614638 : 50a5f08520403bdcba5337e60cf47a46e8110218171881b588d7ae8a81688315.
- Windows 10796227763 : daf6e84d128b71e1dab7e7b571bb54fff36c47514f4087346b6d9abea4885618.
- Comparaisons 10795687750 : a1cbbb08d5240efc5c70da008b3aadaefb8290b7db292a351c84eabb56aff90a.

L'ancienne projection conserve exactement 21 fichiers par OS contre les ORIGINAUX f3482012 de chaque plateforme : quinze float64 et six JSON de géométrie/paquets. Le code local est identique à la source Linux archivée ; la source Windows diffère uniquement par CRLF, sans autre différence textuelle. Les deux archives originales sont conservées. Les réutilisations de sorties sont refusées avec le code2, le message attendu et toutes les empreintes inchangées.

## Oracles indépendants

NetworkX 3.6.1 reconstruit le graphe d'incidence et les composantes des neuf snapshots depuis les faces et les événements C#. Il retrouve un domaine intact, un domaine avec fissure interne, puis deux fragments. La proximité visuelle n'est jamais utilisée comme liaison.

L'intégration directe des phases de mouvement retrouve les sommets continentaux et les sommets océaniques suivant leurs dates de naissance ; erreur maximale nulle sur ces exemples. Les volumes des cinq origines continentales sont conservés.

Shapely 2.1.2 / GEOS 3.13.1 recalcule 372308 contributions triangulaires, dont 49205 intersections partielles, ainsi que leurs centroïdes et intégrales d'âge affine. Les erreurs maximales par champ/unité sont conservées dans verification-independante.json ; toutes sont inférieures à 2.98e-12 sur les cas exécutés. Ce maximum ne constitue pas un score géologique mélangeant les unités.

| Exemple | Surface océanique créée km² modèle | Volume continental km³ modèle | Moment d'âge océanique km³ × Myr modèle |
|---|---:|---:|---:|
| Droit | 2400000 | 1428000000 | 84000000 |
| Non rectiligne avec pause | 1680000 | 1427550000 | 56280000 |
| Raccord périodique | 2400000 | 1427550000 | 84000000 |

Le tracé non rectiligne déplace les limites de régions d'épaisseurs différentes : son volume initial diffère légèrement de celui du cas droit. La conservation est vérifiée dans chaque cas et par origine, pas par une égalité fictive entre cas. La pause réduit l'apport océanique tout en laissant vieillir le plancher existant.

2359296 pixels des neuf PNG16 sont décodés indépendamment par Pillow. Fractions : code/65535 ; âge : code*60/65534 Myr modèle, avec 65535 pour l'absence de matière océanique. Les sources float64 sont conservées. Aucun PNG d'altitude n'est revendiqué.

## Défauts de recette identifiés et corrigés

Run35969859800, d71ac6f5 : C# et projections réussis sur les deux OS, mais la garde d'intégrité utilisait les hashes JSON Linux pour Windows. Les 21 fichiers nouveaux de chaque OS étaient identiques à ses propres originaux f3482012 ; les JSON originaux différaient uniquement LF/CRLF. 5d66464e sélectionne les hashes ORIGINAUX par plateforme. Onze contrôles locaux couvrent les bonnes/mauvaises plateformes, six corruptions et un OS non qualifié. Aucune tolérance ni valeur attendue numérique n'est changée.

Run35970439149, 5d66464e : toutes les comparaisons réussissent mais upload-artifact omet les deux rapports sous .local à cause de l'exclusion des fichiers cachés. Les entrées sont préservées et comparées localement. 8f31084c autorise uniquement les deux chemins de rapports, inclut ces chemins cachés et refuse l'absence de fichier. La campagne finale35971073418 réexécute tous les contrôles et publie réellement les rapports. Aucun changement C# entre ces corrections de recette.

## Livraison et limites

ISRWorldGen_Separation_8f31084c conserve les données, paquets, PNG16, deux archives de source, reçus et scripts indépendants. La galerie utilise sept images incorporées sans dépendance réseau. Leur décodage et la structure HTML ont été contrôlés ; le schéma des quatre états a été examiné visuellement. Test navigateur : NOT_RUN.

La connexion entre séparation topologique et projection des ouvertures est maintenant exécutable. La localisation mécanique de la rupture et ses mouvements restent fournis ; ils ne sont pas encore produits automatiquement par le monde. Il n'y a donc AUCUNE nouvelle heightmap mondiale, ni calibration terrestre renouvelée. Le relief antérieur reste REJECTED et l'érosion NON LANCÉE.

Revue d'un second agent, couplage aux forces mondiales, qualification Vintage Story/MCP : NOT_RUN. Aucun appel API du jeu, framework, dépendance, registre, profil ou sauvegarde modifié. Les oracles numériques indépendants ne remplacent pas une revue d'un second agent.
