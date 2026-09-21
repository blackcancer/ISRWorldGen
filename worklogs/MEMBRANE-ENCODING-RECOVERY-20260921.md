# Recuperation de l'export mecanique — aucune modification du calcul de relief

Parent : 6d6de1eb3e43f48eebb7f9ec414b899efc2fa451. Run original 35633787474. Le job Ubuntu -437287116 (106446100832) a compile et execute les 83 controles C#, puis les deux histoires completes. Max Y242.04110838051798, residu mecanique maximal 6.090309344667738e-12. L'echec survient APRES le calcul et les PNG de relief, dans l'export des champs mecaniques. Le run original reste FAILED, pas PASS.

## Cause verifiee dans les donnees originales
Artefact 10655767896, SHA256 a954fde8179d39572dc2bf1d6180010d568a6b9777ffeb2c3020d4b322876291. La couche shear-rate native 128² atteint -0.11023116165482506 pour une plage d'affichage codee en dur [-0.1,+0.1]. Les vitesses et les deformations sont finies ; la divergence est dans [-0.083955695,0.072725217]. Le probleme est une plage de quantification auxiliaire, pas une tolerance physique depassee. Aucune valeur de hauteur, materiau, vitesse ou effort n'est corrigee pour passer l'export.

## Correction et garde
Les vitesses gardent une enveloppe D'AFFICHAGE declaree de +/-4000 unites-reference/temps-modele. Les derives sont encodes dans +/-2*4000*(1/dx+1/dz), dx et dz etant les pas de la grille mecanique native en unites de reference. A 128² sur un million cela donne +/-2.048. Cette borne suit le stencil et non les extrema de la seed. La resistance est encodee sur 0..50 : son prior accepte C,O<=150 et a une borne inferieure a 48.465. Aucune borne constitutive, residu, seuil interplateforme ou conservation n'est modifiee. Le depassement d'une plage reste un refus explicite et detaille, jamais un ecretage.

L'export verifie en plus le cisaillement et la divergence en les recalculant depuis les vitesses de faces, dans la metrique native, avec un ecart maximal de 1e-12. Toute la collection mecanique est precontrolee avant creation d'un repertoire PNG. Une tentative sur un repertoire de preuves deja exporte est refusee avant modification. Le mode self-test verifie le contre-exemple mesure, les bornes analytiques, la quantification/decodage, les constantes et les entrees non finies.

## Recuperation executee localement
Dans une COPIE NEUVE des champs bruts de l'artefact rejete, l'encodeur corrige a termine sans erreur. Tous les .f64le ont conserve leur empreinte ; les trois PNG principaux height/initial-height/baseline-height sont IDENTIQUES OCTET A OCTET aux fichiers originaux. Une deuxieme execution a ete refusee avec FileExistsError et tous les fichiers de preuve ont conserve leurs empreintes. Aucun artefact distant original n'a ete ecrase ou modifie. Le recu local encoding-recovery-check.json accompagne la livraison.

Le changement de ce commit concerne exclusivement l'export et cette note. Les sources C#, les modeles initiaux, les seeds, la duree, les resolutions et la conversion Y restent ceux de 6d6de1eb. La CI doit recalculer et qualifier ce nouveau commit ; un export local reussi ne transforme pas l'ancien workflow FAILED en succes. Pas d'erosion, modification de sauvegarde, dependance ou appel API du jeu. Acceptation geographique et revue externe : NON ACQUISES.
