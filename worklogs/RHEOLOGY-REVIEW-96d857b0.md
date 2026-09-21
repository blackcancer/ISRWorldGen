# Revue de la déformation visqueuse — 96d857b0

## Décision

NE PAS remplacer le générateur courant. Le candidat C# reste sur `codex/rheology-20260921`, commit `96d857b0f66ed43e2d1f4037197dfede66c9e26c`, base `875ce237e6d775fd4b9028a2b30256322d525ba1`. Ce compte rendu est le seul ajout sur main. La mécanique est exécutée et contrôlée, mais les reliefs obtenus sont moins marqués que ceux des assemblages e1d88001. Pas de fusion du candidat, de réécriture de l'historique, de réglage silencieux ou de changement de sauvegarde. Érosion NON LANCÉE. Aucun lot DONE.

## Code réellement exécuté

Nouveaux `ThinSheetDeformation.cs`, `MaterialRheology.cs`, point d'entrée explicite `MaterialBoundHistory.GenerateWithRheology`, lanceur `WorldGen.Rheology`, export et vérification dédiée. L'opérateur MAC résout un équilibre périodique de contraintes de membrane visqueuse et de traction basale linéaire : v - div(2 mu [epsilon + tr(epsilon) I]) = vitesse préférentielle matérielle. Les vitesses de faces obtenues pilotent le vrai transport conservatif de la croûte. Le calcul ne filtre ni les altitudes ni les quantités de matière.

La résistance est un mélange harmonique de réponses continentale et océanique dépendant des matériaux et de l'âge océanique transportés. C'est une hypothèse constitutive continue et bornée, NON une calibration de viscosités terrestres. Le modèle ne résout pas la convection mantellique, le slab pull, les couples des plaques, le bilan thermique du manteau, une loi d'endommagement, de rupture ou de nouvelles plaques. L'orientation des structures héritées n'intervient pas encore. La relaxation continentale et la polarité de subduction historiques restent inchangées. Ne pas assimiler cet incrément à une tectonique complète.

## Campagne et provenance

Run : https://github.com/blackcancer/ISRWorldGen/actions/runs/35647490997
Six générations Windows/Linux × seeds -437287116, 20260906, 73 réussies, puis septième job de comparaison réussi. Chaque exécution contient 85 contrôles, dont 63 antérieurs et 22 nouveaux. Ces contrôles couvrent notamment translation commune, réponses de Fourier analytiques, convergence métrique, échange d'axes, dissipation positive et travail, localisation dans une bande faible, refus de non-convergence, CFL réelle, conservation par origine et redimensionnement de l'atlas entier.

Trois mondes de 1 000 000 × 1 000 000 unités, durée modèle 36. Grille matérielle/altimétrique 512² (pas 1953,125). Grille mécanique 128² (pas 7812,5), recalculée toutes les 1 unité de temps modèle : 37 résolutions par monde. Les vitesses sont interpolées aux bonnes faces MAC ; il ne s'agit pas d'une résolution mécanique 512². Résidus réels de force maximaux : 9,9660e-13 ; 9,9946e-13 ; 9,9397e-13. Le travail basal et la dissipation ont été revérifiés. Aucune tolérance n'a été élargie.

Artefacts Linux téléchargés : 10661190651 (-437287116), 10661520680 (20260906), 10660951012 (73). Rapport interplateforme téléchargé : 10660886417. Les SHA-256 des archives et de tous les champs float64 ont été vérifiés. Pillow a décodé indépendamment les six PNG16 initial/final : tous les pixels correspondent à round(Y*65535/383), sans masque marin. Le rapport interplateforme donne un écart maximal nul pour les couches comparées et des pixels PNG16 identiques sur cette campagne ; aucune universalité tous matériels/runtimes n'est revendiquée.

La comparaison locale utilise les véritables sorties e1d88001. Les checksums d'assemblage ET d'état matériel initial sont identiques pour chaque seed, les champs initiaux comparés ont un écart nul, et les paramètres tectoniques sont identiques. Contrairement au changement précédent d'initialisation, les matériaux océaniques initiaux sont également les mêmes. L'altitude utilise toujours Y=168+12*km modèle. Aucun ajustement du niveau marin ou du contraste. Erreur relative du bilan continental final : 0 ; 1,84e-16 ; 1,89e-16.

## Résultat géographique — REJET

| Seed | Maximum au-dessus de la mer, ancien / candidat (blocs) | P95 des hauteurs terrestres, ancien / candidat (blocs) |
|---|---|---|
| -437287116 | 37,229 / 25,513 | 25,605 / 15,409 |
| 20260906 | 49,332 / 35,027 | 31,529 / 20,368 |
| 73 | 48,958 / 28,111 | 31,192 / 20,431 |

Les trois heightmaps entières ont été examinées, avec les états initiaux et la même conversion fixe. Les massifs sont plus doux et plus diffus ; de grandes surfaces océaniques restent quasi uniformes et des bandes sous-marines restent trop rectilignes. Le succès mécanique n'améliore donc pas suffisamment la morphologie ; sur le contraste des reliefs terrestres, il la dégrade. Le terrain précédent étant lui-même non accepté, le conserver ne le valide pas pour autant.

Ces mesures ne suffisent pas à attribuer chaque défaut à un paramètre isolé : formulation mécanique, coefficients constitutifs, portée spatiale et résolution mécanique doivent être distingués. L'expérience change l'opérateur ET la loi de résistance par rapport au lissage précédent. Ne pas affirmer que l'hétérogénéité de résistance, à elle seule, cause tous les écarts. L'option homogène est disponible, mais aucune campagne complète avec cette option n'a été exécutée ici.

## Reprise précise, sans boucle de réglage esthétique

La prochaine étape part du solveur testé sur la branche, pas d'une nouvelle copie divergent de main. Commencer par une comparaison mécanique homogène/hétérogène à forçage et résolution identiques, puis qualifier une loi non linéaire de localisation et la résistance héritée sur collision/extension/coulissement analytiques avant les trois mondes entiers. Ne pas réduire arbitrairement la portée, augmenter la hauteur ou ajouter du bruit pour obtenir un meilleur rendu. Les états de rupture et l'appartenance mécanique ne doivent pas être confondus avec l'origine géologique.

Référence conceptuelle : England & McKenzie 1982 (10.1111/j.1365-246X.1982.tb04969.x), avec leur correction 1983 (10.1111/j.1365-246X.1983.tb03328.x), et Jiménez-Munt et al. 2005 (10.1016/j.tecto.2005.08.015). Leur différence entre déformations newtonienne et non linéaire motive l'expérience suivante, mais ne valide pas les coefficients du candidat. Notre opérateur est dérivé de l'énergie explicitée dans le code et testé contre ses propres solutions analytiques ; il n'est pas présenté comme une reproduction intégrale de ces publications.

La comparaison statistique ETOPO n'a pas été refaite : un candidat déjà moins convaincant visuellement n'est pas déclaré calibré aux reliefs terrestres. Revue d'un second agent, compilation complète avec les DLL Vintage Story, jeu et MCP Visual Studio : NOT_RUN. Aucun appel Vintage Story ni dépendance nouvelle. Les cartes, données et sources sont conservées dans les artefacts de la branche avec leur SHA exact. Le retour sur ce candidat n'exige aucun changement du générateur natif ou de partie personnelle.
