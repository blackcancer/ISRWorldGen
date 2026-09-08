# L12 — Diagnostics des milieux aquatiques

**Complément 1.5 des missions L12-B, L12-C ; ni remplacement de leurs anciens critères ni nouvelle tâche.**

## Objectif et travail

L12-B prévoit les paramètres écologiques/quotas approuvés sans modifier silencieusement un monde déjà créé. L12-C intègre les couches des producteurs : bathymétrie, niveau local, fond/support, type d’eau, peuplement et saison. Les cartes respectent palettes, unités, provenance et valeurs manquantes ; prévoir des coupes verticales.

L19-E possède ses diagnostics de requêtes/coverage/connexions via l’API. Les helpers de cartographie peuvent être partagés sous mandat de l’intégrateur, sans dépendance obligatoire de L12-C à L19-E. Les cartes complètent assertions et parcours, jamais un faux PASS visuel.

## Lectures et propriété

Lire la fiche de la mission active, ses contrats obligatoires et ce complément. `specs/S-AQ.md` définit le périmètre transversal ; n’ouvrir que la recette T-AQ du domaine attribué, pas les autres capsules. Les sources API sont ciblées avant appel et les liens ne déclenchent pas une lecture récursive du corpus.

Conserver les chemins réservés du lot. Les adaptations de producteurs, contrats, fichiers partagés, projets et registries exigent un mandat d’intégrateur. Les preuves nouvelles se rangent dans le sous-dossier du sous-lot et `artifacts/maps/<lot>/<test>/<seed>/` selon le standard du dépôt. Ne pas copier le code d’un autre module pour contourner son périmètre.

## Acceptation et passation

Scénarios additionnels sous responsabilité principale du lot : Contribuer aux scénarios référencés ci-dessus, sans se substituer à leur propriétaire de qualification.

Les contrats 1.4/1.5 doivent être approuvés pour le périmètre concerné ; ne pas déduire DONE de ce document. Fournir fixtures, témoins négatifs, sorties numériques, cartes/coupes pertinentes, commandes, baseline et résultats réels dans HANDOFF. Une preuve analytique ne remplace pas l’essai du moteur.

Un propriétaire par fichier/contrat partagé, un seul propriétaire du jeu/MCP, revue indépendante et sauvegardes jetables. L’adoption de ce complément ne lève pas de pause, ne publie rien et n’autorise pas une réécriture du monde joué.
