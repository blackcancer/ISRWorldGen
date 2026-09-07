# Mission de revue indépendante
Relis la tâche Lxx-X, sa capsule, son diff et ses preuves. Ne lis pas toute la spécification. Commence par ton propre examen du contrat, du diff et des invariants avant de lire la justification détaillée de l’auteur. Vérifie d’abord le respect du périmètre et des contrats ; cherche ensuite les cas qui contredisent les invariants du sous-lot. Le profil de revue est Terra pour un lot courant et Sol Medium/xHigh pour un risque algorithmique, numérique, concurrent ou de performance.

Distingue tests exécutés et simplement écrits. Contrôle que les cas négatifs échouent correctement, que les seuils/goldens n’ont pas été modifiés pour masquer un défaut et que les données persistantes/API ne sont pas supposées. Les tests en jeu demandés ne sont pas remplacés par mocks ou screenshots.

Rends ACCEPT / REQUEST_CHANGES / BLOCKED avec fichiers/lignes, tests et reproduction ciblée. Ne marque pas le registre DONE et ne fusionne pas la branche toi-même.
