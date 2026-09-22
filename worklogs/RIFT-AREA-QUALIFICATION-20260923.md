# Projection conservative des rifts — qualification C#

Base : 4f31f29a1bf11d8b03276d5466947a3fa5a102d7. Import du candidat local de projection, avec 29 contrôles conservés et quatre contrôles supplémentaires. Le helper de bilan utilise maintenant la métrique physique déclarée, non la constante .01. Le code de projection original est inchangé lors de cette première campagne : pas de correction numérique sans contre-exemple exécuté.

Objectif : compiler le vrai Core puis vérifier les fragments continentaux résiduels, les bandes océaniques datées et leurs intersections sous-maille. L'oracle rectangulaire de centroïde ne réutilise pas le découpage polygonal. Les trois expériences couvrent l'emprise entière de référence 1000000², mais ce ne sont pas des mondes procéduraux ni des heightmaps.

## Frontières d'exécution

Les objets Git sont préparés avant toute référence publique. Une branche d'essai nouvelle pointe sur le commit complet, sans modification de main. Les jobs Windows/Linux ont des espaces isolés ; aucun accès aux sauvegardes, secrets ou DLL du jeu. Le répertoire de résultats existant est refusé. INCOMPLETE précède les champs, COMPLETE suit les vérifications ; un échec conserve les données et FAILED, sans nettoyage destructif. L'exporteur vérifie les trois sources avant le premier PNG ; il refuse une reprise sur des PNG existants. Reprise après interruption : conserver le dossier échoué et utiliser une nouvelle campagne, jamais écraser ses preuves. La comparaison exige les 82 régressions et les 33 nouveaux contrôles ; tolérance de champ 1e-8, images identiques. Aucune tolérance de collision ou conservation n'est modifiée.

Analyse statique du périmètre et du modèle de transitions réalisée ; revue indépendante d'un second agent NOT_RUN. Pas de fusion ni acceptation de lot sans cette revue. État de compilation/tests de ce commit à sa création : NOT_RUN ; les reçus de campagne font foi, pas ce plan.

La topologie de rupture mondiale, les forces motrices et le relief restent à reprendre. Ne pas injecter les espaces inconnus comme océan d'âge nul. Aucune érosion, aucun appel à l'API Vintage Story, aucun changement de framework ou dépendance. Les PR5 et PR6 restent indépendantes et non fusionnées.
