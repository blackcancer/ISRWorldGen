# Retour de la memoire visqueuse dans le relief — revue 6d62044b

## Code et decision

Base : 4f31f29a1bf11d8b03276d5466947a3fa5a102d7. Code C# execute : 6d62044b310182e5e12baa45df71f0d6be86d1a7.
Branche : codex/strain-weakening-20260922. Campagne : https://github.com/blackcancer/ISRWorldGen/actions/runs/35752017163 . Resultat global SUCCESS.
Conserver le candidat experimental et les preuves ; NE PAS presenter le terrain comme accepte ni remplacer le generateur de reference. Le code reste sur sa branche, sans fusion dans main. Erosion non lancee. Aucun lot DONE. Revue d'un second agent et recette native : NOT_RUN.

## Effet reel et hypothese precise

GenerateWithWeakening transporte Q=C*kappa, une memoire de deformation VISQUEUSE cumulee, sur des paquets du porteur continental C. La source provient du taux sqrt(0.5*D:D) du champ mecanique resolu, avec compensation incompressible verticale. La rotation rigide ne produit pas cette deformation ; le cisaillement peut en produire mais n'est pas une preuve d'ouverture. La memoire suit aussi la redistribution laterale de croute inferieure. Le porteur parallele est controle contre C, jamais substitue a la matiere du solveur. Son bilan est controle avec production explicite.

La memoire diminue la viscosite continentale, et la viscosite modifie les vitesses resolues, donc le transport, l'epaisseur et les altitudes. Ce n'est plus un observateur passif. La loi exp(-kappa/.5), son residu .35, son support metrique de 10000 unites et la cadence mecanique de 1 sont des choix experimentaux NON calibres. Aucune fracture plastique, topologie de rupture, creation de faille, guerison, chaleur, fusion mantellique ou nouvelle regle d'apport oceanique n'est revendiquee. Les lois d'echange oceaniques et le prior d'age uniforme restent ceux du Core. La methode reste un equilibre de membrane reduit avec trainement basal et mouvements preferentiels prescrits, pas une tectonique terrestre complete.

Le solveur ThinSheetDeformation est repris exactement de la branche rheology : blob e3547e47747f0989437c5decafb43fd23877472a. Aucun autre changement de cette branche n'a ete importe. Les deux modes de cette campagne utilisent ce meme solveur et la meme mesure de convergence locale dans ses vitesses. Ils different UNIQUEMENT par l'affaiblissement : control avec residu=1 et weakening avec residu=.35. Le temoin n'est PAS l'ancienne generation purement cinematique. Les etats initiaux materiels et les hauteurs initiales sont identiques pour chaque paire.

## Execution et verification numerique

Les deux plateformes compilent le vrai Core net10.0 avec global.json inchange, puis reussissent 111 controles C# distincts : 29 nouveaux, 22 de deformation et 60 rift/chronologie. Les deux artefacts de controles ont ete telecharges et leurs inventaires relus. Les douze histoires completes (3 seeds x 2 modes x Windows/Linux) et la comparaison sont reussies. Duree 36, atlas entier 1000000 x 1000000, matiere/hauteur 512 x 512, mecanique 128 x 128. Pas altimetrique 1953.125 blocs ; pas mecanique 7812.5. Les tests de redimensionnement vers 131072 et 262144 portent sur une position homologue, pas une recette exhaustive de tous les parametres.

La CI compare les dix champs de chaque experience, soit 15728640 paires de valeurs. Ecart maximal de tous les champs : 3.932396075434497e-12 (memoire specifique). Ecart maximal des hauteurs : 3.410605131648481e-13 bloc. Tolerance absolue 1e-8 inchangee et pixels PNG16 identiques entre plateformes. Cela ne garantit pas tous les materiels/runtimes. Les douze refus d'ecrasement verifient le message, le code natif non nul et la preservation des empreintes, puis neutralisent explicitement le code attendu du wrapper pwsh.

Verifications locales executees :
- SHA-256 des six archives Linux, des deux archives de controles et du rapport interplateforme ; hashes des 60 champs float64 Linux.
- 3145728 pixels d'altitude initiale/finale decodes avec Pillow, independamment de l'encodeur du depot. Conversion fixe Y=pixel*383/65535, sous la mer comme sur les terres. Pas d'ecretage, d'ombrage ni de contraste individuel.
- Altitude native signee elevation-model.f64le preservee ; relation Y=168+12*h exactement retrouvee.
- Relation porteur continental / memoire / concentration et bilan de production ; accord travail-dissipation dans toutes les resolutions de membrane exportees.
- Une matrice 128 x 128 construite independamment a partir de la forme quadratique de dissipation puis resolue avec scipy.linalg.solve retrouve les 128 vitesses C# de la fixture 8 x 8 a 6.732288337918391e-14 pres. Residu independant 4.5084253813161786e-13 ; ce n'est pas la reecriture Python d'un monde.
- Huit controles de l'exporteur reel sur fixtures synthétiques : decodage independant, niveaux sous-marins, sources conservees, reutilisation refusee, NaN/plages invalides et seconde source corrompue refusees avant les premiers PNG.
- Les trois nouveaux fichiers du Core locaux correspondent exactement a l'archive source compilee ; le blob du solveur importe est confirme.

Artefacts Linux control/weakening : 10705871741 / 10707550246 (seed -437287116), 10707375532 / 10707145608 (20260906), 10707510257 / 10707445450 (73). Controles : 10705233492 Linux, 10706061226 Windows. Rapport interplateforme : 10706766048. Les mondes Windows n'ont pas ete retelecharges localement : leur comparaison complete est celle du job CI execute, pas une comparaison locale inventee.

## Changements de hauteur a etat initial identique

| Seed | Difference absolue maximale | Difference quadratique moyenne | Part des cellules changees de plus de 1 bloc |
|---|---:|---:|---:|
| -437287116 | 10.4830279351 blocs | 1.2892523560 bloc | 17.5854 % |
| 20260906 | 12.4482298657 blocs | 1.3224697049 bloc | 16.2132 % |
| 73 | 10.9443101039 blocs | 1.0421671975 bloc | 11.9640 % |

La colonne quadratique est la racine de la moyenne des differences au carre (RMS), pas la moyenne absolue. Ce tableau prouve la retroaction, pas une amelioration de realisme. Les maxima Y control/weakening sont 193.5126/199.4412, 203.0269/208.2013 et 196.1099/201.9212. La reference marine est Y=168.

## Revue geographique et affichage

Les SIX heightmaps finales entieres ont ete examinees simultanement avec la meme echelle. L'affaiblissement redistribue les reliefs, mais les domaines continentaux restent trop doux, les chaines ne se structurent pas suffisamment et les bandes sous-marines restent schematiques. Le prior d'age oceanique et la topologie des domaines ne sont pas corriges par ce mecanisme.

Diagnostic predefini : fraction des centres dont le voisinage periodique 9x9 a un denivele <=1 bloc, a resolution identique. Control/weakening : 20.5067/20.7947 %, 26.8776/26.9646 %, 37.9715/38.3133 %. Cet indicateur ne s'ameliore pas ; ce n'est pas une raison de supprimer toutes les plaines, ni un score a maximiser. Il interdit ici de confondre augmentation de quelques sommets et amelioration d'ensemble.

Le dossier de livraison contient les valeurs exactes, les PNG16, neuf vues (3 initiales communes et 6 finales), et 18 transects systematiques X/Z aux fractions .25,.5,.75 avec CSV. L'interpolation transverse des coupes est declaree ; elle ne cree pas de detail calcule supplementaire. Le curseur de la galerie lit le float64, pas les niveaux 8 bits d'aperçu.

Test navigateur local : Chromium 144.0.7559.96, HTML injecte par set_content. Navigation file:// bloquee par la politique du navigateur, non desactivee. Une erreur locale de saut de ligne dans le JavaScript de la galerie a ete corrigee, sans toucher les donnees. Les neuf canvas natifs, la lecture exacte d'une cellule, le telechargement PNG16 octet a octet et l'absence d'erreur JavaScript ont ensuite ete verifies. Il s'agit d'un controle cible d'affichage, pas d'une qualification exhaustive multi-navigateurs.

## Suite ciblee

La retroaction materiau -> resistance -> vitesse -> matiere est maintenant executable et mesurable. Elle doit preceder une vraie loi de rupture et une decision topologique ; kappa ne doit pas etre converti directement en carte de dorsales. La prochaine experience doit distinguer resistance ductile/plastique, localisation et separation des fragments, conserver les memes materiaux initiaux et apporter de vraies generations comparatives, sans pousser ce simple affaiblissement pour fabriquer des sommets. La composante thermique oceanique et les forces motrices restent a reprendre causalement.

Acceptation geographique : REJECTED. Nouvelle calibration ETOPO, qualification Vintage Story avec DLL ciblees, jeu/MCP, revue de second agent : NOT_RUN. Aucune erosion, sauvegarde, regle native, dependance NuGet ou etat de lot modifie. Branche non fusionnee ; les resultats numeriques ne constituent pas une autorisation implicite de fusion.
