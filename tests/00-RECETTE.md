# Recette du mod et définition de V1 validée
## Portée
Les 84 scénarios historiques T00-01 à T13-06 restent applicables ; leur état réel doit être repris des preuves du dépôt. La version 1.2 ajoute 48 scénarios, tous NOT_RUN dans cette livraison documentaire. Leur présence n’est pas une suite C# déjà compilable ; les contrôles documentaires sont distincts.

Un test de conformité à une exigence peut inclure plusieurs assertions. Chaque assertion bloquante doit être réussie, pas seulement la majorité. Les cas en jeu ne sont pas remplacés par des mocks. Une tâche peut avancer avec un stub prévu, mais le résultat du test réel reste NOT_RUN jusqu’à son exécution.

## Fréquences
Sur chaque changement : compilation, tests unitaires du module, fixtures analytiques et régression de défaut associé. Sur chaque fusion : contrats et tests des consommateurs touchés, contrôle déterminisme et raccords sur corpus rapide. À chaque gate : tous les tests des sous-lots requis et reprise du scénario de démonstration. Avant release : corpus complet, holdout, tests de corruption/crash, campagne multijoueur, performance/endurance et parcours humains.

Le corpus rapide comporte 16 seeds, le corpus atlas/bassin 256, et la recette moteur 16. La partie holdout de 64 seeds ne sert pas à l’ajustement des presets. Les mesures de rareté peuvent nécessiter une extension déterministe pour atteindre un échantillon suffisant ; conserver les seeds sans site. Les seeds de laboratoire forcées ne rentrent jamais dans ces statistiques.

## Oracles numériques et géographiques
Identité stricte des ports quantifiés depuis les deux côtés. Identité des voxels et snapshots canoniques sous ordres/threads/caches différents sur le profil qualifié. Bilan hydrique selon C02 (tolérance absolue + relative). Bilan sédimentaire T06-02. Zéro site fantastique publié sans certificat d’accès valide. Zéro cycle de drainage non représenté comme réservoir/sous-réseau permis. Zéro fallback silencieux vers une autre version de générateur.

Les tests de parcours utilisent le gabarit, support et transitions du profil ; une flood-fill de l’air est seulement un contrôle préliminaire. Le certificat final est revérifié après les passes de décor/structures natives. Les courants visibles ne prouvent pas à eux seuls le fonctionnement de l’eau : laisser évoluer le moteur.

## Grille qualitative
Chaque famille de relief et chaque thème fantastique est évalué sur cinq axes notés de 0 à 4 : cohérence des formes, absence de répétition technique, lisibilité de la découverte, intérêt du parcours et cohérence avec le reste du monde. Réussite proposée : aucun axe à 0/1 et moyenne au moins 3, après gel de la grille avant les captures. Conserver commentaires et désaccords ; cette note guide une décision humaine, elle ne remplace pas les invariants.

Observer cartes à plusieurs zooms, minimap native, altitude du joueur et coupes souterraines. Varier les vues et conditions d’éclairage ; ne pas montrer uniquement les merveilles. Comparer aux témoins défectueux F16. Une chaîne orientée par une tectonique cohérente n’est pas une erreur simplement parce qu’elle produit une anisotropie.

## Parcours de jeu obligatoires
Un parcours de surface couvre spawn sûr, collecte de ressources de départ, traversée de plusieurs reliefs, lecture d’affluents et descente d’un bassin vers son embouchure. Un parcours souterrain couvre accès naturel, embranchements, réseau humide/sec, découverte d’un site fantastique et possibilité d’identifier une route de retour selon le profil. Un parcours serveur fait explorer simultanément une frontière par deux clients et valide sauvegarde/rechargement.

Construire un témoin (blocs et contenu d’inventaire), purger seulement les caches dérivés et reprendre l’exploration voisine. Le témoin ne doit jamais être régénéré ou perdu. Le mode test qui force les merveilles est explicitement distingué des parties où elles sont rares.

## Performance et faux positifs
Mesurer Release sans debugger, matériel/OS/runtime et paramètres enregistrés. Geler les budgets de `registry/quality-budgets.json` avant T09-02/T13-03 ; un état PROPOSED bloque la recette chiffrée. Conserver p50/p95/p99 et pics RAM, pas seulement une moyenne. Trois exécutions et deux heures d’endurance minimum pour le profil final proposé. Les seuils de rareté sont ajustables par profil avant gel ; l’échantillonnage rapporte son incertitude par seed.

Injecter des défauts connus pour vérifier la suite : montée de rivière d’un bloc, site isolé, accès bouché, budget dupliqué, snapshot tronqué, codec de climat faux, mauvais fond marin. Si les tests ne les détectent pas, réparer les oracles avant de qualifier le générateur.

## Procès-verbal
La V1 nécessite gates G0–G5 closes, rapports complets, tous les cas bloquants PASS, aucune fonctionnalité revendiquée sans preuve et installation propre. Une réserve cosmétique mineure peut être conservée avec validation explicite ; pas une corruption, un accès impossible ou une incohérence de drainage. Utiliser le modèle `templates/RELEASE-REVIEW.md` et conserver les sources des résultats.

## Complément obligatoire 1.2 — distributions
Les suites T05U, T14, T15, T16, T17 et T18 ajoutent 48 scénarios bloquants ; le corpus total comporte 132 scénarios principaux. Les résultats historiques restent ceux du dépôt réel. Tous les nouveaux scénarios sont NOT_RUN dans cette livraison.

La recette ajoute coupes de roches et interfaces de faille, sols minces/alluviaux/marécageux, gisements en hôtes autorisés et après excavation, prospection par mode, ressources végétales et peuplements, puis neige/glace selon saison et type d’eau. Les tests négatifs injectent une faute discriminante afin de prouver l’oracle, sans désactiver l’invariant dans le code de production.

Sur le bassin complet, parcourir au minimum un transect montagne/source → versant → vallée alluviale → fleuve/estuaire, avec coupes géologiques et exploration souterraine. Tester le climat froid/humide et froid/sec, les sols/forêts, l’accès aux minerais et la prospection. Faire tourner un cycle saisonnier selon le protocole, recharger une zone absente, modifier volontairement sol/arbre/glace puis vérifier la persistance des actions du joueur.

Les tests T15-08 et T17-10 sont des probes natifs précoces, non des équivalents des campagnes finales. Les budgets [distribution-budgets](../registry/distribution-budgets.json) sont fixés avant holdout. Aucun seuil final requis ne reste null ; les champs proposés ne sont pas des performances promises. Le responsable de chaque seuil fournit une preuve de calibration et l’intégrateur gèle le profil.

L11-C rejoue les régressions croisées listées dans la matrice de passes. L09-C vérifie les accès après toutes les écritures et dans les états saisonniers naturels supportés. Un accès bouché volontairement par le joueur n’est jamais rouvert par worldgen. L13 inclut solo/serveur, Release sans debugger pour les performances, ressources économiques et agriculture natives, installation propre et contraintes de sauvegarde.
