# L19 — Socle public des habitats et connexions aquatiques

**Complément 1.5 des missions L19-A, L19-B, L19-C, L19-D, L19-E, L19-F ; ni remplacement de leurs anciens critères ni nouvelle tâche.**

## Objectif et travail

L19-A ajoute les producteurs/familles aquatiques à l’inventaire réel. L19-B fait geler C13-AQ, ses capacités, limites, sémantique géométrique, versions et packaging. Les emprises verticales et requêtes 3D ne sont pas facultatives pour prétendre couvrir les eaux.

L19-D collecte les géométries, caractéristiques et reçus, puis les persiste même sans consommateur. Aucun nouvel algorithme de peuplement, scan biologique, calcul de stocks alimentaires ou écriture dans les blocs/cartes de spawn. Le cas de support modifié par corail doit conserver fond de référence et occupation initiale qualifiée.

L19-E implémente les opérations C13 étendues, le filtrage 3D, le voisinage hydrologique borné, les erreurs et diagnostics. Une arête n’est pas un trajet de poisson ; un trou de coverage n’est pas un bassin fermé. Aucun chargement de chunk provoqué par les requêtes. L19-C transmet le guide correspondant à cette interface réelle, avec trois exemples : eau douce, marine, terre/eau. Sa revue documentaire ne clôture pas les essais de F.

L19-F réalise le consommateur externe, la lecture après déchargement/restart, l’installation tardive, les versions/absences/quotas et les essais sur monde modifié. L’assemblage de contrat public est sa seule référence ISRWorldGen. Les assertions sur les populations vivantes ou une navigabilité garantie ne font pas partie du contrat.

Ordre 1.4 inchangé : A → B → D → E → C → F. Tous les prérequis 1.4 restent ; L13-A attend F. La pause L19-B est conservée jusqu’à mandat explicite de reprise.

## Lectures et propriété

Lire la fiche de la mission active, ses contrats obligatoires et ce complément. `specs/S-AQ.md` définit le périmètre transversal ; n’ouvrir que la recette T-AQ du domaine attribué, pas les autres capsules. Lire aussi `contracts/C13-AQ.md` pour L19 ; aucun autre lot ne dépend de son implémentation publique. Les sources API sont ciblées avant appel et les liens ne déclenchent pas une lecture récursive du corpus.

Conserver les chemins réservés du lot. Les adaptations de producteurs, contrats, fichiers partagés, projets et registries exigent un mandat d’intégrateur. Les preuves nouvelles se rangent dans le sous-dossier du sous-lot et `artifacts/maps/<lot>/<test>/<seed>/` selon le standard du dépôt. Ne pas copier le code d’un autre module pour contourner son périmètre.

## Acceptation et passation

Scénarios additionnels sous responsabilité principale du lot : T-AQ-13, T-AQ-14, T-AQ-15, T-AQ-16, T-AQ-17, T-AQ-18, T-AQ-19, T-AQ-20, T-AQ-21, T-AQ-22, T-AQ-23

Les contrats 1.4/1.5 doivent être approuvés pour le périmètre concerné ; ne pas déduire DONE de ce document. Fournir fixtures, témoins négatifs, sorties numériques, cartes/coupes pertinentes, commandes, baseline et résultats réels dans HANDOFF. Une preuve analytique ne remplace pas l’essai du moteur.

Un propriétaire par fichier/contrat partagé, un seul propriétaire du jeu/MCP, revue indépendante et sauvegardes jetables. L’adoption de ce complément ne lève pas de pause, ne publie rien et n’autorise pas une réécriture du monde joué.
