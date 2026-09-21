# Spectre thermique océanique transporté — expérience explicite

Base de code : 48b8bb985c9d0a57f97d17427d1d6e9c27c4f9c7, branche de rhéologie. Main est préservé. Une nouvelle branche accueille le candidat ; aucune fusion ni activation native n'est autorisée par la seule CI. Érosion suspendue, revue géographique NOT_EVALUATED, revue indépendante NOT_RUN.

## Problème isolé
Le moment O*âge suffit pour calculer un âge moyen, pas une température moyenne sous une loi non linéaire. Le refroidissement de la moyenne des âges n'est pas la moyenne des refroidissements. En outre le terme historique en sqrt(âge) continue à s'abaisser sans limite. L'état initial à âge 50 uniforme explique aussi les grands fonds uniformes : cet incrément ne prétend PAS reconstruire cette préhistoire et ne lui invente pas une carte d'âges aléatoires.

## Réponse calculée
Modèle analytique réduit de conduction verticale d'une plaque finie à propriétés constantes : épaisseur 125 km, diffusivité 1e-6 m²/s, dilatation 3.2e-5/K, contraste 1350 K. Ce sont des paramètres de référence explicites, pas une inversion calibrée d'ETOPO. Les modes impairs ont des amplitudes exp(-n² t/tau), tau=L²/(pi²*kappa), et des poids 8/(pi²*n²). Douze modes sont conservés ; la somme restante est regroupée au mode impair suivant, le plus lent omis. La fraction froide à naissance vaut zéro, la limite ancienne vaut un. La majoration de l'erreur liée au spectre est publiée séparément de l'erreur de quantification PNG.

Chaque origine matérielle transporte ses amplitudes extensives sur les mêmes paquets de volume océanique représentables que MaterialPlateCohorts. Le vieillissement fait décroître les modes ; le basalte nouveau ajoute des modes chauds ; le recyclage enlève uniquement ceux de l'origine plongeante choisie. Les bilans de chaque mode gardent RequireBalance sans en changer la tolérance. Les sous-normaux ne reçoivent pas de plancher arbitraire. Ni les hauteurs ni la matière ne sont lissées.

Le datum de colonne est apparié à l'ancienne loi à UN âge 50 fixé pour toute la campagne : h_sec = h_sec_historique(age=50) - f_ocean * alpha*DeltaT*L/2 * (fraction_froide - F(50)). La branche de charge d'eau existante est conservée : c'est toujours la hauteur du SOLIDE. Cet ancrage permet le contrôle expérimental et ne devient pas une calibration absolue de la bathymétrie terrestre.

Couplage unidirectionnel : le calcul mécanique et la polarité emploient encore les âges existants. Aucun retour thermique sur la viscosité ni convection, flexure ou création de nouvelles frontières. Les trois réponses d'altitude (ancienne loi, loi finie appliquée à l'âge moyen, spectre transporté) sont évaluées sur le MEME état matériel final. Un contrôle C# compare aussi les hashes du matériau et les reçus mécaniques avec/sans spectre sur une histoire courte.

## Périmètre et recette
Nouveaux OceanPlateCooling.cs, lanceur WorldGen.OceanCooling, export et vérificateur. Raccord opt-in GenerateWithOceanCooling et paramètre interne de snapshot ; les trois anciens points d'entrée conservent leurs calculs. Framework, DLL du jeu, dépendances NuGet, profils natifs, registre et sauvegardes inchangés.
Seize nouveaux contrôles ciblés plus suite antérieure, puis 3 seeds historiques × Windows/Linux sur atlas ENTIER 1 000 000², matière/altimétrie512² et mécanique128², durée36 inchangée. Les autres tailles s'obtiennent par atlas entier redimensionné, jamais recadré. Les sorties restent des altitudes exactes et PNG16 à conversion0..383, sans eau masquante. Les résultats de CI seront lus avant de déclarer une exécution réussie.

## Publication, interruption et récupération
Etat A : base en lecture seule ; nouveaux fichiers locaux séparés. Etat B : nouveaux blobs/arbre/commit inatteignables, sans changement de ref. Etat C : création d'une branche nouvelle après comparaison de la base ; main jamais mis à jour. Etat D : sortie CI dans un dossier neuf, un job par seed/OS. Echec : conserver source et journal, aucun PASS ; nouvelle campagne sur nouveau commit pour une correction. Reprise après erreur d'API : relire la ref exacte, ne pas réappliquer aveuglément. Rapport final écrit après vérification ; aucune ancienne archive écrasée. Analyse statique de ce circuit effectuée ; revue d'un second agent indisponible et non revendiquée. Aucun changement d'état de lot ou fusion automatique.

## Source externe
Richards et al. (2018), doi:10.1029/2018JB015998, distingue refroidissement de demi-espace et de plaque finie, et montre les limites des modèles constants face à leurs observations corrigées. Notre série constante n'est PAS la reproduction de leur modèle complet dépendant de température/pression. Les règles de transport et de mélange du présent opérateur sont dérivées explicitement et contrôlées sur leurs cas analytiques.
