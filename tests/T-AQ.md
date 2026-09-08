# T-AQ — Recette transversale des fonds et de la vie aquatiques

**Plan 1.5 · 24 scénarios supplémentaires, sans renumérotation de T17/T19.**

Chaque responsable exécute ses tests au stade permis par ses dépendances. Les probes précoces ne certifient pas le monde final ; L17-D/L11-C/L19-F puis L13-A reprennent les effets croisés. Tous les scénarios du mod sont NOT_RUN dans ce dossier.

Une catégorie d’asset/entité réellement absente est identifiée dans l’inventaire et dans la couverture de campagne, avec justification approuvée. L’audit d’absence peut être exécuté ; la capacité de simulation correspondante n’est pas déclarée PASS. Aucune exemption de ce type ne dispense les familles minimales de données aquatiques d’une preuve positive sur fixture.

| Test | Responsable | Sujet |
|---|---|---|
| [T-AQ-01](aquatic/T-AQ-L17.md) | L17-A | Catalogue des peuplements submergés |
| [T-AQ-02](aquatic/T-AQ-L11.md) | L11-A | Catalogue de faune et voies de spawn |
| [T-AQ-03](aquatic/T-AQ-L07.md) | L07-B | Fond, niveau local et compartiments verticaux |
| [T-AQ-04](aquatic/T-AQ-L17.md) | L17-B | Support sous-marin distinct de la fertilité agricole |
| [T-AQ-05](aquatic/T-AQ-L17.md) | L17-B | Distribution par milieux et gradients |
| [T-AQ-06](aquatic/T-AQ-L17.md) | L17-C | Emprise et déterminisme des patches |
| [T-AQ-07](aquatic/T-AQ-L17.md) | L17-C | Placement natif et reçu effectif |
| [T-AQ-08](aquatic/T-AQ-L17.md) | L17-C | Récifs, fond final et zones protégées |
| [T-AQ-09](aquatic/T-AQ-L17.md) | L17-D | Récolte et permanence des modifications |
| [T-AQ-10](aquatic/T-AQ-L11.md) | L11-B | Apparition native au worldgen |
| [T-AQ-11](aquatic/T-AQ-L11.md) | L11-C | Apparition et persistance natives pendant le jeu |
| [T-AQ-12](aquatic/T-AQ-L18.md) | L18-D | Gel, dégel et volumes d’eau |
| [T-AQ-13](aquatic/T-AQ-L19-Contrat.md) | L19-B | Contrat et dictionnaire aquatiques complets |
| [T-AQ-14](aquatic/T-AQ-L19-Requetes.md) | L19-E | Requêtes réellement tridimensionnelles |
| [T-AQ-15](aquatic/T-AQ-L19-Requetes.md) | L19-E | Connexions connues sans fausse navigabilité |
| [T-AQ-16](aquatic/T-AQ-L19-Collecte.md) | L19-D | Habitats et ressources candidates sans population fictive |
| [T-AQ-17](aquatic/T-AQ-L19-Collecte.md) | L19-D | Collecte durable sans consommateur |
| [T-AQ-18](aquatic/T-AQ-L19-Qualification.md) | L19-F | Installation tardive et migrations |
| [T-AQ-19](aquatic/T-AQ-L19-Requetes.md) | L19-E | Couverture, quotas et absence de génération implicite |
| [T-AQ-20](aquatic/T-AQ-L19-Qualification.md) | L19-F | État historique après modification et saison |
| [T-AQ-21](aquatic/T-AQ-L19-Qualification.md) | L19-F | Consommateur intermods aquatique indépendant |
| [T-AQ-22](aquatic/T-AQ-L19-Requetes.md) | L19-E | Cartographie, coupes et provenance |
| [T-AQ-23](aquatic/T-AQ-L19-Qualification.md) | L19-F | Budget et non-influence du socle |
| [T-AQ-24](aquatic/T-AQ-L13.md) | L13-A | Recette V1 des milieux aquatiques |

## Corpus minimum

Matrice contrôlée : eau douce/salée/transition ; faible/grande profondeur ; sable/dépôt/roche selon assets ; terrain plat/pente ; zone nue/peuplement/récif ; surface/colonne/fond/berge ; régions adjacentes et coordonnées négatives ; eaux superposées ; chenal/chute/connexion inconnue ; été/hiver ; chargement/déchargement/restart. Ne pas exiger que chaque seed naturelle contienne chaque combinaison : les fixtures complètent un corpus de seeds figé et un holdout indépendant.

Les interactions natives de chaque catégorie réellement présente ont une fixture positive et un témoin négatif. Le chemin de récolte et les comportements applicables sont vérifiés, sans inventer une recette inexistante. La densité d’entités vivantes se contrôle selon un protocole reproductible/statistique adapté, pas avec un hash d’identifiants d’entités arbitraires.

## Diagnostics obligatoires lorsque pertinents

Bathymétrie et niveau local séparés, fond/support, type d’eau, communautés et couverture, connexions orientées connues et trous de connaissance ; coupe verticale au moins sur la fixture superposée. Le manifeste contient versions, seed, emprise, repère, unités, résolution, palette fixe, valeurs manquantes et hashes. Les fonds inconnus ne sont pas coloriés comme des fonds nus.

## Gates

Les probes et assemblages précoces alimentent G3 sans anticiper les tests finaux. G4 conserve les exigences de la révision 1.4, notamment L19-F, et ajoute les preuves aquatiques applicables avant recette. G5 exige la campagne L13 incluant T-AQ-24 et la traçabilité de T-AQ-01…23 sur la même baseline. Aucun T20 ni G6 n’entre dans la clôture V1.
