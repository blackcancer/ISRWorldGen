# Relief brut v7 — le volume des massifs précède le détail des crêtes

Base : 5d4256ad7f666d5f3bc38fea6164aa33c72c4130. La correction b5719689 des ramifications et ses limites mémoire sont conservées, ainsi que l'élargissement des contacts et le socle sous-marin du parent. Aucun recul vers une base antérieure.

## Revue avant correction

Les 3 cartes 262144² de 5d4256ad et les cartes précédentes b571 ont été examinées à emprise complète. L'élargissement améliore les volumes mais les axes de crête restent dominants par rapport à la surface montagneuse. La carte ETOPO Alpes-Pô-Ligurien conservée par la campagne montre un massif étendu avec un réseau interne, non une ligne de sommets portant presque tout le dénivelé. C'est un contrôle morphologique, pas une équivalence implicite mètres/blocs ni une simulation des détails d'érosion de la Terre actuelle. Verdict précédent : REJECT.

## Changement de composition

RawMassifComposition construit un volume montagneux au sein du support géologique existant. La morphologie imbriquée subdivise ce volume ; elle ne choisit ni l'emplacement des continents ni celui des orogènes. Les crêtes planifiées restent présentes comme détail et leur contribution est limitée à 0.22 de la marge disponible. L'enveloppe analytique du résultat reste inférieure ou égale à 0.766, sans clamp d'altitude ou normalisation par carte. Les basses terres hors influence restent intactes. Les termes de bathymétrie et leur séparation du réseau terrestre ne changent pas.

Le relief bruité n'est pas annoncé comme un résultat de mécanique tectonique. Ce candidat utilise des supports géologiques et des primitives structurales multiscalaires. Algorithme explicite raw-structural-relief-v7-massif-volume-before-crest-detail ; aucune nouvelle bibliothèque ni appel Vintage Story.

## Contrôles ajoutés

MassifCompositionChecks appelle les vraies fonctions C# : absence de montagne produite par la texture seule, volume non confiné à l'axe, limites analytiques, contribution axiale bornée, monotonie du support, invariance au changement simultané de coordonnées et d'échelle physique, répétabilité, concurrence, différence entre seeds et refus des entrées invalides. Tous les contrôles existants restent actifs. Aucun de ces contrôles ne vaut verdict géographique.

## Etat à publication

Tests à exécuter par la CI Windows/Linux Release ; pas de PASS anticipé. La campagne conserve 3 seeds fixes, les 2 mondes complets 131072/262144 et 1024² vraies mesures par carte. PNG16 et float64 exacts, fonds marins non masqués, palettes inchangées. Comparaison ETOPO attendue après génération, et revue morphologique par l'assistant. Erosion, jeu, MCP, qualification globale, revue indépendante de code : NOT_RUN. Etat géographique NON_ACCEPTE jusqu'à cette revue ; aucun lot promu.

## Ecritures

Arbre issu de la tête vérifiée, commit descendant et mise à jour non forcée. Tout conflit de main impose une relecture. Ni sauvegarde personnelle, ni état de lot, ni format persistant ni catalogue natif n'est modifié. Les sorties CI restent isolées, non écrasables, et les sources exactes sont archivées. L'effet externe se limite au code explicitement demandé sur main et à ses campagnes de contrôle.
