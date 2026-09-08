# C13-AQ — Extension aquatique du contrat environnemental C13

**Révision de plan 1.5 · contrat logique proposé à geler en L19-B et implémenter en D/E.**

Aucun nom ci-dessous n’est présenté comme une interface native Vintage Story. Ce document complète C13 de la révision 1.4 : il conserve lecture seule, API serveur, DTO immuables, identification du monde, versions séparées du schéma de stockage et de l’ABI, limites, couverture et absence d’I/O bloquante dans la voie immédiate. Il remplace uniquement la restriction aquatique qui résulterait d’une recherche exclusivement horizontale et de champs verticaux facultatifs.

## 1. Identités et géométrie

Une entité environnementale a WorldIdentity, Dimension, FeatureId, Revision et SourceRefs. Un WaterBodyId identifie un objet hydrologique, un WaterCompartmentId un compartiment ou fragment volumique représentable. Ni une altitude commune ni le même XZ ne suffisent à identifier une eau. Le référentiel C00, le niveau d’eau local, la résolution et les unités sont explicites.

Une surface océanique et une cavité noyée superposées sont distinctes. Une polyligne de rivière n’est pas un volume de nage. Une zone benthique (liée au fond), la colonne d’eau, la surface et la rive sont des domaines séparés. Les contenus amphibies peuvent relier une géométrie terrestre à une géométrie aquatique, sans certificat d’accessibilité animale.

Représenter l’eau par masques/profils/segments ou volumes compacts déjà produits, pas par un tableau mondial de voxels dupliqué. Une boîte englobante est un filtre d’index, jamais la géométrie exacte d’une baie ou d’un chenal. Les fragments partagés référencent la même identité canonique. L’identité ne provient ni de l’ordre de découverte, ni d’un numéro de composante transitoire d’un union-find, ni d’un GetHashCode non persistant.

Les profondeurs sont relatives à la surface locale dans la convention verticale gelée. Préciser l’origine au sommet du support ou à la limite fluide, les bornes inclusives/exclusives et les arrondis voxel. Une épaisseur d’eau historique et un tirant d’eau utilisable maintenant sont deux informations différentes.

## 2. Données minimales à produire

| Ensemble logique | Contenu obligatoire à la précision du producteur |
|---|---|
| WaterGeometry | Identités, géométrie/fragment vertical, niveau local, profondeur ou intervalle de profondeur, emprise, résolution, phase et références du producteur. |
| WaterEnvironment | Eau douce/salée/autre code réel/inconnue, milieu hydrologique, référence climatique et régime saisonnier disponible ; lien aux états de surface C12. |
| BottomDescription | Fond géologique de référence, support effectivement placé lorsqu’un reçu existe, classe matériau/dépôt, pente ou descripteur équivalent à sa résolution ; ne pas confondre fond de base et haut d’un récif. |
| FixedCommunity | Identité/emprise du peuplement, catégorie plante/algue/corail/décor, codes natifs persistants, domaine vertical, couverture avec dénominateur/unité, Planned ou GeneratedBaseline, reçus et motifs de rejet. |
| HabitatCandidate | Géométrie utile, familles d’usage candidates, propriétés du milieu, références des peuplements et critères génériques traçables ; aucune population, ration ou suitability spécifique à une espèce. |
| WaterConnection | Extrémités stables, nature du lien (contact d’eau, écoulement, débordement saisonnier ou interface terre/eau), sens hydraulique si connu, résolution, seuils/ruptures connus et portée temporelle. |
| Coverage | Couverture par couche **et volume/intervalle vertical**, trous/legacy, révision et limites de recherche. Une couverture marine ne couvre pas automatiquement les cavités sous-jacentes. |

La V1 implémente positivement ces familles sur les milieux produits et sur les fixtures obligatoires. Elle ne peut pas renvoyer Unsupported partout pour clore L19. Les champs affinés qui n’existent pas dans un producteur (vitesse voxel, température réelle de l’eau, oxygène, nutriments, turbidité, salinité mesurée, biomasse) restent ExplicitlyUnavailable avec cause et source, pas une valeur zéro ou un chiffre inventé.

Un régime d’écoulement de référence et une direction hydraulique ne sont pas une vitesse instantanée. « Zone de transition estuarienne » est une classe écologique issue des connexions et types d’eau ; elle n’oblige pas à créer un liquide saumâtre ni à présenter une salinité chiffrée. Une hypothèse ou proxy porte son nom et sa version.

## 3. État initial, évolution et publication

Planned, GeneratedBaseline et ObservedAt restent distincts. ObservedAt exige une observation réelle, un fournisseur, un temps de mesure et une portée. La date de requête n’est jamais une mesure. Les résultats historiques imposent une validation locale avant consommation, installation ou déplacement.

L07 publie la géométrie structurelle et la connectivité connue à sa résolution. L17 publie les peuplements et leurs deltas de support/occupation autorisés. L’adaptateur confirme les reçus après les passes concernées et les métadonnées natives requises ; il ne choisit pas une phase à partir de son nom seulement. Une géométrie mesurée avant le placement d’un récif reste référencée comme telle, pas promue « fond final ».

Les propriétés Planned ne sont pas effacées lorsque le placement échoue ; le reçu signale le rejet ou l’emprise effectivement matérialisée. Un retour booléen natif ne suffit pas à prouver cette emprise. L19 est consommateur de ces sorties et n’écrit pas de blocs pour les faire correspondre au plan.

Aucune mise à jour de population ni invalidation exhaustive bloc par bloc n’est ajoutée. Les modifications du joueur et le fonctionnement natif peuvent changer l’eau, le fond, la végétation ou la présence d’animaux. Le catalogue historique reste interprétable ; un consommateur actualise localement ce dont sa simulation a besoin. Un provider d’observation futur demeure optionnel et ne devient pas une dépendance V1 cachée.

## 4. Opérations publiques minimales

Les signatures sont à geler en B ; elles sont éprouvées par un vrai mod en F. Les opérations existantes C13 sont étendues, sans multiplier artificiellement les providers.

| Opération logique | Contrat aquatique |
|---|---|
| Décrire les capacités | Couches, domaines verticaux, types géométriques, recherche 3D, portée des connexions, limites, résolutions et validités réellement supportées. |
| Contexte à une position XYZ | Retourner le compartiment et domaine contenant la position ; eau absente, compartiment non couvert et dimension non supportée sont distingués. |
| Candidats dans une emprise/rayon | Supporter un volume 3D ou une emprise XZ avec intervalle Y explicite, types d’eau, familles et nombre maximal. La métrique choisie est documentée. |
| Voisinage de connexion | Retourner les connexions déjà publiées d’un compartiment, sous limite de liens/segments/pages et profondeur de parcours. Aucun pathfinding animal. |
| Couverture | Fournir les zones et tranches verticales effectivement documentées pour toutes les couches demandées. |

Une distance euclidienne à une géométrie et une distance le long d’un réseau sont des métriques différentes. La recherche « proche » par défaut ne promet jamais « joignable ». Pour une zone étendue, la distance porte sur la géométrie publiée ou une approximation explicitement bornée, pas seulement son centroïde.

Ordre stable sur distance puis identité, calculs robustes aux coordonnées négatives/débordements, masques d’intersection et dédoublonnage des fragments sont obligatoires. Une limite Top-K n’autorise pas à ignorer la couverture manquante ; un résultat partiel le reste même avec K éléments trouvés. Un identifiant de continuation, s’il est implémenté, est lié au monde, à la révision et aux filtres ; aucun suivi d’un océan entier n’est implicite.

## 5. Connectivité et usages futurs

Un lien hydraulique dirigé peut transmettre de l’eau sans être franchissable en amont ou en aval par un animal. Une chute, une zone gelée, un étranglement, une connexion temporaire ou un obstacle connu se décrit comme un fait de génération/projection ; aucune valeur CanSwimThrough universelle n’est exposée. Les connexions inconnues ne deviennent pas des impasses certifiées.

Les candidats pour alimentation, refuge, reproduction/nurserie, habitat en pleine eau, habitat du fond ou transition terre/eau sont des **vues sur des caractéristiques existantes**. Ils ne nécessitent ni simulation trophique, ni moteur générique de règles d’espèces. Un récif peut fournir une géométrie de couvert, pas un recensement de proies. Des supports fins et une faible profondeur peuvent être interrogés, sans annoncer qu’une espèce y pond réellement.

Le consommateur reste propriétaire des régimes, seuils biologiques, réservations, bancs, populations, trajets et état courant. L19 ne dépend ni de ce consommateur ni d’un registre POI natif. L11 conserve séparément ses responsabilités de compatibilité des entités natives ; L19 ne remplace pas AnimalSpawnMaps et n’influence pas les tirages de spawn.

## 6. Stockage, coût et migration

Réutiliser C05/L10 : sources canoniques durables et versions, reçus initiaux, index dérivés régionaux, publication cohérente et checksums. Les requêtes ordinaires peuvent lire des métadonnées persistées sous quota, jamais charger/générer des chunks. La voie immédiate en mémoire ne bloque pas sur le disque. Annulation, changement de monde et éviction ne livrent aucun ancien buffer ou mélange de révisions.

Un monde généré avec L19 1.5 doit posséder ses métadonnées aquatiques sans mod animalier. Un monde antérieur peut utiliser une migration **metadata-only** depuis des sources historiques exactes/versionnées, ou signaler LegacyMissing. Ni scan des blocs actuels présenté comme passé, ni nouvelle génération avec un algorithme plus récent, ni effacement de sauvegarde ne compense des sources absentes.

Si C13/son ABI est déjà distribué, le changement 3D doit être négocié par capacités/version ou version majeure selon sa compatibilité effective ; la révision documentaire « 1.5 » n’est pas automatiquement la version du DLL. Le contrat public demeure partagé selon la politique de chargement qualifiée en 1.4 ; aucun nouvel assembly de biologie n’est nécessaire.

## 7. Qualification

Cas obligatoires : volumes superposés, estuaire, chenal et chute, rive terre/eau, récif altérant le support, patch rejeté, profondeur hors enveloppe, façade absente/incompatible, couverture verticale partielle, persistance avant/après ajout du consommateur, monde modifié, serveur et budgets saturés. T-AQ-13 à 23 complètent T19 sans en annuler aucun.
