# S-AQ — Fonds et vie aquatiques, spécification transversale V1

**Plan 1.5 · normatif après adoption explicite · aucune implémentation présumée.**

## Périmètre et propriété

S-AQ complète S07/S08/S11/S17/S18/S19 et leurs contrats, sans remplacer leurs autres exigences. Le fond, les eaux libres, les peuplements fixes, la faune native et les données exposées au futur mod constituent cinq objets différents.

L07 possède la géométrie et l’hydrologie de référence ; L08 les volumes souterrains déjà générés ; L14/L15 la description du substrat/support ; L17 la composition et les modifications locales autorisées des peuplements fixes. L11 est seul responsable du raccord aux mécanismes du jeu, dont la faune native. L19 consomme les descriptions et reçus initiaux : aucune influence sur terrain, tirages aléatoires ou spawning.

## Couverture obligatoire

La capacité couvre milieux marins et d’eau douce, fonds peu profonds/profonds, estuaires et deltas lorsque produits, eaux libres, peuplements submergés, et interfaces terre/eau. Les eaux souterraines existantes ne sont pas fusionnées avec la surface. La liste d’assets et d’espèces s’établit depuis la cible réelle ; on n’ajoute pas d’espèces, de liquides ou de familles fantastiques pour rendre un exemple testable.

Toutes les familles minimales C13-AQ doivent produire des réponses positives sur des fixtures appropriées. Une catégorie d’asset réellement absente est documentée et ne crée pas une exigence de nouveau contenu ; sa simulation ne reçoit pas un faux PASS. Une capacité implémentable mais manquante n’est pas déclarée facultative pour clôturer une mission.

## Composition, support et placements

Les communautés ont des exigences de type d’eau, profondeur, support, contexte climatique et enveloppe spatiale. Les seuils et poids appartiennent à des profils versionnés, audités puis gelés avant la campagne de validation. Aucun réalisme biologique de précision n’est revendiqué sans modèle ; la V1 vise une distribution cohérente, variée et compatible avec les mécanismes natifs.

Le fond de référence, le support natif et la fertilité agricole sont distincts. Une plante dont l’implémentation exige un support donné ne doit pas être forcée par une fertilité artificielle. Les zones nues restent permises et utiles, sans masquer une catégorie de génération perdue.

Réutiliser les méthodes natives publiques adaptées, vérifier les DLL/signatures locales et contrôler le résultat après leurs effets. Pour les récifs, observer toutes les écritures de fond, structures, plantes et décors, leur emprise et leurs interactions. Ne pas présumer qu’un générateur appelé est sans état ou qu’il préserve nos réservations. Si l’adaptation sûre est impossible dans l’enveloppe, rejeter le patch avec un motif traçable ; ne pas effacer la protection ni accepter une coupe silencieuse.

L’ownership des sous-placements reste unique : les plantes générées à l’intérieur d’un récif appartiennent à son reçu, pas à une seconde passe de peuplement. Les réservations hydrauliques publiées ne sont pas modifiées pour faire entrer une décoration. Les métadonnées natives de hauteur/fluide sont entretenues dans leur sémantique exacte par L11.

## Faune native et futur mod

L11-A inventorie codes, règles, cartes et voies de spawning réellement chargés. L11-B prouve l’apparition au worldgen ; L11-C prouve runtime, reload et serveur. Maintenir les règles existantes, corriger seulement les entrées/raccords défectueux causés par ISRWorldGen. Ni nouveaux taux de population arbitraires, ni patch global des espèces, ni second spawner pour masquer un défaut. Aucune exactitude déterministe de trajectoires ou d’identifiants d’entités natives n’est promise.

L19 prépare les usages d’alimentation, refuge, reproduction et passage via propriétés et géométries candidates. Les animaux, espèces et consommateurs déterminent eux-mêmes leurs besoins et leur accès courant. Les nouvelles IA, bancs, migrations, frayères actives, repousse écologique, chaînes alimentaires et simulations d’oxygène/nutriments ne sont pas développés dans ces lots.

## Saison, historique et performances

Le monde structurel ne dépend pas de la saison de première visite. Le modèle saisonnier de L18 et l’état réel sont distingués. Un gel de surface n’est ni une preuve d’un volume totalement solide, ni une mesure de l’oxygène sous glace. Les modifications du joueur sont conservées.

Les index sont compacts, régionaux et fondés sur des données déjà publiées. Les requêtes n’explorent pas la planète et ne génèrent pas de chunks. Tout coût de recherche, d’I/O de métadonnées, de pages, de résultats, de liens et de mémoire est borné. Les limites contractuelles sont gelées en B et les budgets mesurés/qualifiés en F, sans résultat de performance anticipé.

## Exigences
### R-AQ-01 — Catalogue des peuplements submergés

Inventorier tous les contenus fixes aquatiques natifs chargés, leurs règles et propriétaires, sans assimiler classes techniques et catégories biologiques.

Qualification principale : L17-A ; scénario T-AQ-01.

### R-AQ-02 — Catalogue de faune et voies de spawn

Inventorier les entités aquatiques et semi-aquatiques effectivement disponibles et leurs voies worldgen/runtime, sans nouvelle espèce ni second spawner.

Qualification principale : L11-A ; scénario T-AQ-02.

### R-AQ-03 — Fond, niveau local et compartiments verticaux

Fournir géométrie aquatique et connexions structurelles à résolution explicite, distinguant fond, surface, rive et volumes superposés.

Qualification principale : L07-B ; scénario T-AQ-03.

### R-AQ-04 — Support sous-marin distinct de la fertilité agricole

Utiliser fond, substrat et support natif réels, avec critères écologiques séparés de la fertilité terrestre.

Qualification principale : L17-B ; scénario T-AQ-04.

### R-AQ-05 — Distribution par milieux et gradients

Composer fonds nus et peuplements selon profondeur, type d’eau, substrat et contexte climatique, sans tapis uniforme ni seuil universel non documenté.

Qualification principale : L17-B ; scénario T-AQ-05.

### R-AQ-06 — Emprise et déterminisme des patches

Borner les placements sous-marins et arbitrer les patches partagés sans dépendance à l’ordre des chunks ou threads.

Qualification principale : L17-C ; scénario T-AQ-06.

### R-AQ-07 — Placement natif et reçu effectif

Respecter solides/liquides et supports natifs, et enregistrer l’emprise réellement placée, y compris rejets ou appels sans effet.

Qualification principale : L17-C ; scénario T-AQ-07.

### R-AQ-08 — Récifs, fond final et zones protégées

Attribuer un propriétaire aux modifications de fond et décors des récifs ; interdire toute altération des réservations hydrauliques, structures et accès obligatoires.

Qualification principale : L17-C ; scénario T-AQ-08.

### R-AQ-09 — Récolte et permanence des modifications

Préserver les comportements et ressources natifs des peuplements submergés, sans réensemencement worldgen au rechargement.

Qualification principale : L17-D ; scénario T-AQ-09.

### R-AQ-10 — Apparition native au worldgen

Préserver le spawning aquatique natif à la génération, avec une seule autorité et les cartes/métadonnées attendues.

Qualification principale : L11-B ; scénario T-AQ-10.

### R-AQ-11 — Apparition et persistance natives pendant le jeu

Qualifier séparément le runtime, la sauvegarde et le chargement de faune native, sans modifier ses comportements au titre de L19.

Qualification principale : L11-C ; scénario T-AQ-11.

### R-AQ-12 — Gel, dégel et volumes d’eau

Conserver identités/types d’eau et comportement natif lors du gel/dégel, sans assimiler surface gelée et volume complètement gelé.

Qualification principale : L18-D ; scénario T-AQ-12.

### R-AQ-13 — Contrat et dictionnaire aquatiques complets

Étendre l’inventaire et C13 avec eau, fond, colonne, surface, berge, verticalité, connexions, unités, provenance et validité.

Qualification principale : L19-B ; scénario T-AQ-13.

### R-AQ-14 — Requêtes réellement tridimensionnelles

Implémenter consultations XYZ et recherches à domaine vertical explicite, avec distance à la géométrie et couverture volumique.

Qualification principale : L19-E ; scénario T-AQ-14.

### R-AQ-15 — Connexions connues sans fausse navigabilité

Exposer un voisinage hydrologique borné et orienté lorsque pertinent, sans produire de trajet animal ni nier des connexions non couvertes.

Qualification principale : L19-E ; scénario T-AQ-15.

### R-AQ-16 — Habitats et ressources candidates sans population fictive

Publier des vues de ressources, couvert, refuge et reproduction potentiels depuis les caractéristiques produites, pas depuis une simulation animale.

Qualification principale : L19-D ; scénario T-AQ-16.

### R-AQ-17 — Collecte durable sans consommateur

Produire les métadonnées aquatiques sans mod animalier, avec publication/reçus cohérents et références persistantes à coût borné.

Qualification principale : L19-D ; scénario T-AQ-17.

### R-AQ-18 — Installation tardive et migrations

Rendre consultables les données aquatiques après ajout tardif du consommateur et traiter honnêtement les mondes sans historique exact.

Qualification principale : L19-F ; scénario T-AQ-18.

### R-AQ-19 — Couverture, quotas et absence de génération implicite

Borner recherche 3D, voisinage et lecture de métadonnées ; distinguer inconnu, vide, résultat tronqué et couverture complète.

Qualification principale : L19-E ; scénario T-AQ-19.

### R-AQ-20 — État historique après modification et saison

Ne jamais faire passer un catalogue initial pour une observation actuelle après actions sur l’eau, les fonds, les plantes ou le gel.

Qualification principale : L19-F ; scénario T-AQ-20.

### R-AQ-21 — Consommateur intermods aquatique indépendant

Un mod externe doit utiliser réellement les données aquatiques sans références aux classes internes d’ISRWorldGen ni logique biologique.

Qualification principale : L19-F ; scénario T-AQ-21.

### R-AQ-22 — Cartographie, coupes et provenance

Exporter diagnostics aquatiques reproductibles, sans remplacer les assertions numériques par une image.

Qualification principale : L19-E ; scénario T-AQ-22.

### R-AQ-23 — Budget et non-influence du socle

Mesurer surcoûts worldgen/stockage/requête et démontrer que L19 n’altère pas les décisions de génération aquatique de la même révision.

Qualification principale : L19-F ; scénario T-AQ-23.

### R-AQ-24 — Recette V1 des milieux aquatiques

Inclure les preuves aquatiques dans la qualification V1 et les rejouer sur le candidat final sans dépendance à L20.

Qualification principale : L13-A ; scénario T-AQ-24.

## Références

Contrat public complémentaire : [C13-AQ](../contracts/C13-AQ.md). Scénarios : [T-AQ](../tests/T-AQ.md). Sources et qualification locale : [audit API](../docs/19-SOURCES-API-AQUATIQUES.md). Aucun de ces textes ne remplace un build ni un test natif.
