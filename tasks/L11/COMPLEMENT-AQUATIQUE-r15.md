# L11 — Faune native et raccord à la colonne finale

**Complément 1.5 des missions L11-A, L11-B, L11-C ; ni remplacement de leurs anciens critères ni nouvelle tâche.**

## Objectif et travail

L11-A inventorie les entités aquatiques/semi-aquatiques et les mécanismes de spawning effectivement chargés, ainsi que cartes, blocs, collision, lumière et phase de leurs lectures. L17-A inventorie séparément les communautés fixes ; les deux inventaires partagent les codes mais pas l’autorité de placement.

L11-B prouve la faune au worldgen et la colonne eau/support/peuplements. L11-C qualifie runtime, sauvegarde/reload et serveur après les campagnes L17/L18. Corriger seulement les données/raccords natifs incompatibles : pas de deuxième spawner ni de nouveaux profils biologiques.

Conserver les métadonnées dans leur sens natif, y compris après les deltas de récifs. Les sources publiques ne prouvent ni l’ordre local exact ni l’identité des assets. Une catégorie réellement absente est documentée ; son comportement n’est pas déclaré testé.

## Lectures et propriété

Lire la fiche de la mission active, ses contrats obligatoires et ce complément. `specs/S-AQ.md` définit le périmètre transversal ; n’ouvrir que la recette T-AQ du domaine attribué, pas les autres capsules. Les sources API sont ciblées avant appel et les liens ne déclenchent pas une lecture récursive du corpus.

Conserver les chemins réservés du lot. Les adaptations de producteurs, contrats, fichiers partagés, projets et registries exigent un mandat d’intégrateur. Les preuves nouvelles se rangent dans le sous-dossier du sous-lot et `artifacts/maps/<lot>/<test>/<seed>/` selon le standard du dépôt. Ne pas copier le code d’un autre module pour contourner son périmètre.

## Acceptation et passation

Scénarios additionnels sous responsabilité principale du lot : T-AQ-02, T-AQ-10, T-AQ-11

Les contrats 1.4/1.5 doivent être approuvés pour le périmètre concerné ; ne pas déduire DONE de ce document. Fournir fixtures, témoins négatifs, sorties numériques, cartes/coupes pertinentes, commandes, baseline et résultats réels dans HANDOFF. Une preuve analytique ne remplace pas l’essai du moteur.

Un propriétaire par fichier/contrat partagé, un seul propriétaire du jeu/MCP, revue indépendante et sauvegardes jetables. L’adoption de ce complément ne lève pas de pause, ne publie rien et n’autorise pas une réécriture du monde joué.
