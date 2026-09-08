# L17 — Communautés fixes terrestres et aquatiques

**Complément 1.5 des missions L17-A, L17-B, L17-C, L17-D ; ni remplacement de leurs anciens critères ni nouvelle tâche.**

## Objectif et travail

L17-A : catalogue et matrice par catégorie de contenu, milieu, support, profondeur, générateur public, sortie/état et propriétaire. L17-B : composition spatiale cohérente de plantes émergées/submergées, algues, herbiers, coraux/décors réellement disponibles et fonds nus ; contraintes dures distinctes de préférences/proxies.

L17-C : emprises maximales, halos, RNG et arbitrage déterministes ; appel natif qualifié ; contrôles des couches de fluide ; deltas de fond/peuplement enregistrés ; aucun doublon entre plantes d’un récif et passe externe. L07-C est un prérequis supplémentaire. Ne pas commencer en supposant une eau native déjà stable parce que le modèle Core l’est.

L17-D : monde final, récolte/interactions natives, rechargement, saison et coûts. Rejouer après les passes finales les contraintes hydrauliques/structures/accès. Pas de replantation worldgen ni d’entités animales générées ici. Le catalogue public est livré par L19, pas par un service parallèle L17.

## Lectures et propriété

Lire la fiche de la mission active, ses contrats obligatoires et ce complément. `specs/S-AQ.md` définit le périmètre transversal ; n’ouvrir que la recette T-AQ du domaine attribué, pas les autres capsules. Les sources API sont ciblées avant appel et les liens ne déclenchent pas une lecture récursive du corpus.

Conserver les chemins réservés du lot. Les adaptations de producteurs, contrats, fichiers partagés, projets et registries exigent un mandat d’intégrateur. Les preuves nouvelles se rangent dans le sous-dossier du sous-lot et `artifacts/maps/<lot>/<test>/<seed>/` selon le standard du dépôt. Ne pas copier le code d’un autre module pour contourner son périmètre.

## Acceptation et passation

Scénarios additionnels sous responsabilité principale du lot : T-AQ-01, T-AQ-04, T-AQ-05, T-AQ-06, T-AQ-07, T-AQ-08, T-AQ-09

Les contrats 1.4/1.5 doivent être approuvés pour le périmètre concerné ; ne pas déduire DONE de ce document. Fournir fixtures, témoins négatifs, sorties numériques, cartes/coupes pertinentes, commandes, baseline et résultats réels dans HANDOFF. Une preuve analytique ne remplace pas l’essai du moteur.

Un propriétaire par fichier/contrat partagé, un seul propriétaire du jeu/MCP, revue indépendante et sauvegardes jetables. L’adoption de ce complément ne lève pas de pause, ne publie rien et n’autorise pas une réécriture du monde joué.
