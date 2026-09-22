# Reconstruction de dorsales historiques — e1800d62

## Objet et décision

Code exécuté : `e1800d620c8d72531e6815c1e2a2527d82741b9a`, parent `041939f8d62cd935857ed4cbc42469a960a762d6`.
Campagne : https://github.com/blackcancer/ISRWorldGen/actions/runs/35695887166
Branche expérimentale de la PR 3 : `codex/ocean-evidence-20260922`. Aucun remplacement du générateur de main, aucune érosion.
Conserver la brique de reconstruction pour la suite. **Ne pas retenir le scénario imposé comme générateur de monde réaliste.**

## Changement de code

`SpreadingAtlas.cs` indexe les emprises réellement balayées par les segments de dorsale des `SpreadingTimeline`.
Les quatre extrémités spatiales/temporelles de chaque phase sont transportées vers l'instant d'observation. Une phase dormante déplace la matière sans en créer. Les copies périodiques sont explicites, bornées et interrogées dans le bon repère déroulé.

L'index ne décide pas de l'âge : il écarte seulement des trajectoires impossibles. L'inversion chronologique existante conserve l'autorité sur la date. Un secteur non couvert ou des événements incompatibles font échouer la reconstruction ; pas de distance à la côte ou à la frontière actuelle, pas d'âge aléatoire ou uniforme de secours.
La concordance de témoins du même événement à un raccord a une résolution fixe de 1e-9 Myr pour les arrondis ; le premier témoin canonique est conservé, sans moyenne ni correction de ses valeurs. Les tolérances interplateformes de champ restent à 1e-8.

Aucun fichier du Core existant n'est remplacé. Les cinq autres ajouts sont le projet/lanceur/contrôles, le comparateur et le workflow. SDK, NuGet, adaptateur Vintage Story, registres, profils et sauvegardes restent inchangés.

## Hypothèse de l'expérience : volontairement contrôlée, pas une planète reconstruite

Même dorsale courbe prescrite pour les trois seeds ; 64 segments et deux flancs conjugués. Deux périodes actives de -200 à -100 puis de -100 à 0 ; vitesses matérielles transverses de 2000 puis 3000 unités de référence par Myr modèle.
Les déplacements cumulés définissent les dates ; les 128 branches ne sont pas des plaques nouvellement simulées.

Les matériaux initiaux continentaux ET océaniques correspondent exactement aux assemblages antérieurs. Le générateur reçoit ensuite la carte d'âges de ce scénario et évolue pendant 36 unités avec ses opérateurs et sa loi verticale existants. Pas de subsidence thermique comptée une seconde fois et aucune retouche des images.
Cette comparaison change la chronologie initiale, donc potentiellement les choix de subduction et les trajectoires matérielles ultérieures ; elle ne garantit pas un champ de vitesse effectif identique à chaque pas.

**Les événements ne sont pas encore engendrés par une rupture des assemblages continentaux.** Ils sont proposés indépendamment de ceux-ci pour contrôler la liaison événements -> âges -> relief. Une couverture numérique complète de ce scénario ne prouve pas l'histoire de formation des continents, le bilan matériel des phases antérieures ou la validité géologique de l'état initial.
La quantité héritée n'est pas détruite ni magiquement renouvelée : on lui fournit ici une hypothèse de date. Le transport ultérieur conserve encore des moments d'âge, pas l'intégralité de la distribution des événements.

## Vérifications réellement exécutées

- 62 contrôles C# antérieurs + 20 nouveaux sur Windows et Linux.
- Six expériences C# (trois seeds × deux plateformes), chacune avec les états 0 et 36, sur l'atlas entier 1 000 000 × 1 000 000 ; grille 512², pas 1953,125.
- Comparaison des 14 champs initiaux/finaux par seed : différence maximale observée 0, tolérance 1e-8 conservée, codes de hauteur 16 bits identiques. Cela ne vaut pas garantie tous matériels/runtimes.
- Archives Linux et 42 champs float64 contrôlés par SHA-256. Les six nouveaux fichiers de source de la campagne ont été confrontés octet à octet au payload local.
- Oracle indépendant d'intégration directe des deux vitesses : 686394 cellules portant de la matière océanique, erreur maximale des dates 2,842170943040401e-14 Myr. Cet oracle contrôle ce scénario, pas les histoires arbitraires.
- Réexport RCV : 9 rasters comprenant 3 références à 36 et les 6 nouveaux états ; 2359296 pixels gris16 décodés avec Pillow, 54 profils systématiques. Plage Y0..383, pas de masque marin ni de contraste individuel.
- Les altitudes natives signées sont préservées dans les données originales. Le lecteur RCV reconstruit en plus une version affine secondaire, identifiée comme telle.

Artefacts Linux : 10679924052 (-437287116), 10680432998 (20260906), 10679784518 (73).
Contrôles : 10680013569 et 10680362959 ; comparaison : 10680646236.

## Diagnostic à coordonnées fixes

Domaine fixé depuis l'état INITIAL de la référence : C < 0,1 km équivalent et O > 6,9 km équivalent. Ce domaine peut contenir des colonnes ensuite émergées ; il n'est pas un masque océanique recalculé.
Quasi-planéité : dénivelé <= 0,5 bloc dans une fenêtre périodique 9×9, soit 17578,125 unités/blocs de côté dans cette campagne. Seuils diagnostiques fixés avant réception des sorties, non normes géologiques.

| Seed | Fraction quasi plane, âge uniforme | Fraction quasi plane, scénario prescrit | P95-P5 des altitudes du domaine, avant / après |
|---|---:|---:|---:|
| -437287116 | 12,41 % | 1,80 % | 27,83 / 48,79 blocs |
| 20260906 | 16,54 % | 1,34 % | 33,38 / 53,37 blocs |
| 73 | 32,22 % | 1,92 % | 8,71 / 34,95 blocs |

Ce n'est pas un score à maximiser : ajouter une rampe suffit à augmenter ces indicateurs sans produire un monde réaliste.
Les comparaisons entières montrent une bathymétrie plus variable mais un grand système imposé, des masses continentales encore tabulaires et des raccords schématiques. La coupe X50 conserve la même échelle et confirme que les variations appartiennent au solide et non à un calque d'eau.
**Acceptation géographique : REJECTED_AS_WORLD_GENERATOR.** Le progrès porte sur la reconstruction et le contrôle de sa conséquence, pas sur une tectonique complète.

## Suite précise et limites

Raccorder les événements de dorsale et les domaines balayés à une histoire de séparation des matériaux continentaux et des plaques, avec leurs trajectoires conjuguées, fermeture/recyclage et domaines de validité. Ne pas remplacer ce scénario par une collection de courbes aléatoires ni considérer une provenance fournie comme une histoire physiquement résolue.
La recette en jeu, la comparaison ETOPO renouvelée, une revue de second agent et un nouveau test navigateur de la galerie comparative sont NOT_RUN. Aucun lot marqué DONE.

Référence de méthode consultée, distincte des choix du modèle : EarthByte, « Age, spreading rates and spreading asymmetry of the world's ocean crust », https://www.earthbyte.org/age-spreading-rates-and-spreading-asymmetry-of-the-worlds-ocean-crust/ ; GPlates, https://portal.gplates.org/portal/present_day_agegrid/ .
Ces sources relient les grilles d'âge à des isochrones et un modèle cinématique. Elles ne calibrent ni notre dorsale prescrite ni les paramètres du jeu.
