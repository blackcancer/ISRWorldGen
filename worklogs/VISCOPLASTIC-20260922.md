# Seuil viscoplastique — expérience causale, pas un modèle de fracture

Base c0437a8953f9e0932606134bad882addb92ca4b6 (PR 5), main conservée. Périmètre : une nouvelle loi constitutive et sa mémoire dans le pipeline expérimental ; aucun changement du jeu, du SDK, des dépendances ou du registre. Pas d'érosion ni de séparation topologique déclarée.

## Modèle avant résultats
Une branche newtonienne de viscosité mu en série avec une branche de Bingham de seuil tau_y et viscosité de surcontrainte mu_p. Invariant e=sqrt(.5 D:D), compensation verticale incompressible incluse. r=mu_p/(mu+mu_p), choisi .2. Si e<=tau_y/(2mu), la déformation est entièrement ductile et la source de mémoire plastique est exactement zéro. Sinon tau=2*r*mu*e+(1-r)*tau_y et e_p=(1-r)*(e-tau_y/(2mu)). e_v+e_p=e et la contrainte est identique sur les deux branches. Pas de stockage élastique, guérison, pression résolue, critère de Mohr-Coulomb ni fissure ouverte.

La mémoire Q=C*kappa_p suit les paquets continentaux et la redistribution de croûte inférieure. Elle diminue le seuil, pas la viscosité ductile. Tau_y initial=.04 en unités normalisées (viscosité relative/Myr modèle), résidu de seuil=.35, support métrique10000 et échelle de mémoire .5. Ces paramètres sont des hypothèses d'essai NON calibrées à la Terre, jamais des MPa fictifs. La résistance au dépassement n'est pas une tolérance numérique.

La membrane MAC minimise une énergie convexe. La quadrature du cisaillement aux quatre coins de chaque cellule détermine la moyenne ARITHMETIQUE des contraintes de coin ; ce n'est pas la moyenne harmonique du précédent opérateur. Les DEUX variantes de cette campagne emploient le nouvel opérateur pour ne pas confondre discrétisation et plasticité. La campagne compare réponse ductile seule et réponse avec branche plastique/mémoire ; elle ne sépare pas l'effet du seuil de celui de son affaiblissement. Les anciens appels sans option Plasticity gardent leur chemin et leur identité.

Newton utilise le Hessien consistant et un CG préconditionné, avec recherche de descente de l'énergie. L'acceptation porte sur le vrai résidu non linéaire <=1e-10. Aucun retour silencieux au solveur cinématique, clipping de contrainte, changement de tolérance interplateforme (1e-8) ou retouche de relief. Une résolution non convergée interrompt l'expérience.

## Préparation et exécution bornée
Vérification locale préliminaire : potentiel quadratique explicite indépendant, dérivée par différences finies (erreur max~1.1e-9 sur la fixture). Ce n'est pas une compilation C#. Pas de SDK local disponible ; l'exécution C# doit être celle du workflow.

Branche nouvelle fondée sur un parent exact ; pas de force-push ni fusion automatique. Une publication interrompue est récupérée par lecture de la branche et des SHA, jamais par écrasement. Jobs éphémères contents:read, persist-credentials:false, chemins .local propres, capture des sources et erreurs. Générateur refuse un chemin existant avant écriture ; le second appel teste refus exact+code non nul+hashes préservés. L'exporteur déjà qualifié précontrôle les deux champs avant écriture des PNG. Un échec reste dans son run et ses artefacts ; nouvelle tentative sur nouveau commit. Revue d'un second agent indisponible : NOT_RUN, ne pas revendiquer validation indépendante ni fusion.

Campagne prévue : contrôles du nouveau modèle et 111 contrôles conservés ; trois seeds fixes -437287116,20260906,73 × deux variantes × Windows/Linux. Atlas ENTIER 1e6×1e6, altitudes/matière512², mécanique128², durée36. Les vitesses et taux sont interpolés vers la grille matérielle sans en faire du nouveau détail résolu. Le champ plastique mécanique exporté est celui du DERNIER instant de résolution, indiqué dans last-mechanics.json (pas une nouvelle résolution à t=36). Altitudes solides natives et PNG16 Y0..383, fonds marins non masqués. Aucun seuil marin ne commande la plasticité.

Acceptation géographique maintenue NON ACQUISE. Une plasticité localisée ne démontre ni une coupure de plaque, ni des fragments conjugués, ni une ouverture. La topologie et les sources océaniques restent inchangées.

## Référence et limites
ASPECT, documentation officielle du modèle visco-plastique : https://aspect-documentation.readthedocs.io/en/latest/parameters/Material_20model.html . Elle motive la séparation fluage/seuil/mémoire plastique ; elle ne fournit ni notre surcontrainte régularisée, ni les constantes du jeu. L'état thermique océanique uniforme et les forces préférentielles restent des limites antérieures non corrigées ici. Qualification native/MCP, calibration ETOPO et revue d'un second agent : NOT_RUN.
