# Revue du raccordement rupture continentale -> chronologie oceanique

## Etat et perimetre

Code execute : 23eecc3f13eb993bd8cdffa09f5195988bd95555. Base de main : 1c95640a86ba2441545c1507b46ff42292a13817.
Campagne complete : https://github.com/blackcancer/ISRWorldGen/actions/runs/35726369017 . Les deux jobs Windows/Linux et le comparateur sont SUCCESS.
Le candidat a ete integre sur main par fast-forward NON FORCE apres relecture de main et comparaison de l'ascendance : trois commits et 17 fichiers AJOUTES, aucune suppression ou modification d'un fichier de la base. Les nouveaux operateurs restent appeles explicitement par leurs lanceurs de verification. Aucun generateur de monde par defaut, registre, sauvegarde, SDK, NuGet ou adaptateur natif n'est remplace.

C'est une qualification numerique d'une coupe de rupture et de son adaptateur de datation, pas une acceptation geographique du monde. Le relief reste NON ACCEPTE ; erosion NON LANCEE. Revue d'un second agent, compilation complete avec DLL Vintage Story et recette native/MCP : NOT_RUN. Aucun lot marque DONE.

## Developpement realise

RiftNecking, jusque-la candidat non compile, est maintenant compile et execute dans le vrai Core. Une faiblesse materielle concentre l'amincissement sous le chargement impose, puis un critere constitutif fourni declenche la separation. La croute continentale residuelle est conservee par origine. L'ouverture reelle entre fragments borne la surface de plancher oceanique nouvellement cree ; son volume est une source materielle distincte.

RiftSpreadingAdapter derive les deux chronologies conjuguees de ces EVENEMENTS DE RUPTURE, avec migration de dorsale, vitesses, changements et pauses explicites. Les trois bibliotheques OceanBirthMap, SpreadingKinematics et SpreadingTimeline proviennent des blobs controles de la branche ocean-evidence, sans importer son scenario de grande dorsale prescrite ni modifier le solveur du monde. Les inconnues et les histoires incompatibles restent des erreurs, jamais des ages par defaut.

## Defauts reproduits et corriges

Premier code 268931fd8faa41855a870c0a1dd3f93fd75ea54c, run 35724508934 : compilation reussie et 18 controles du rift PASS, mais trois familles d'integration FAIL sur les deux plateformes. Des points exactement aux extremites d'un segment ou d'une periode perdaient leur naissance apres rotation ; un raccord temporel actif retenait parfois l'ancien evenement ; une extinction suivie de translation pouvait perdre son dernier temoin.

La correction e7b40d870944f03305c4a9e9f58df8b9b756c200 a reussi le run 35725727881. Elle resout les extremites connues dans une precision explicite dependante de la metrique et du conditionnement, conserve les valeurs interieures et refuse les reperes ou intervalles insuffisamment resolus. Les resolutions maximales sont 1e-9 Myr et 1e-10 de la longueur du segment. Ce sont des limites de decision numerique, pas une calibration terrestre ni une garantie de predicats exacts universels. La tolerance interplateforme reste 1e-8 absolu.

Une vraie ambiguite pendant une pause n'est PAS resolue en attribuant arbitrairement la date la plus recente : le test impose toujours son refus. Les points exterieurs au domaine cree, les ages manquants et les evenements incompatibles restent refuses. Les contre-exemples initiaux et leurs journaux sont conserves, pas declares retrospectivement reussis.

## Recette effectivement executee

Chaque plateforme execute 60 controles : 18 pour l'amincissement/rupture, 14 familles pour le raccordement geometrique et chronologique, 27 assertions historiques pertinentes et 1 controle/export des champs de diagnostic. Les 27 anciennes assertions concernent exclusivement les trois bibliotheques reprises ; les tests thermiques, de mondes complets et d'index spatial des autres branches ne sont pas revendiques.

Les trajectoires directes de 800 particules verifient les dates inversees sur deux flancs, quatre orientations, trois asymetries et plusieurs regimes. Erreur maximale date directe/inversee : 2.318145675417327e-13 Myr modele. La grille de diagnostic contient 1664 cellules avec porteur oceanique ; son oracle analytique de date a un ecart maximal de 0.

La comparaison Windows/Linux porte sur 76727 valeurs numeriques : ecart maximal observe 0, tolerance 1e-8 conservee. Les identifiants, etats et inventaires des tests sont controles. Ce resultat ne garantit pas tous les materiels/runtimes. Les JSON originaux des deux plateformes ont ete telecharges et le comparateur a ete reexecute localement avec le meme resultat.

Les 121 coupes exportees conservent chaque volume continental par origine ; un recalcul independant depuis les parcelles JSON donne une erreur relative maximale de 4.989228078297206e-16. Les graphiques publies utilisent ces valeurs C#, pas la reference Python precedente.

Artefacts recuperes et verifies par SHA-256 :
- Linux 10693528945 : b9efc20cc024bcb51ffd763cd0eefa3f35d0c597443b3378f12ba4950a80a2e6
- Windows 10693554147 : fe05164b62ee4a157be00b8d366c9f278a1547827b319932d93a39c32c5a1392
- Comparaison 10692648602 : b56958cc928d5dd93757380e31fb037838453408eebeef7d5fb384a2072b889e

Les archives source Windows/Linux ne sont PAS identiques octet a octet : 678 fichiers texte different uniquement par CRLF/LF. L'inventaire est identique et chaque difference a ete verifiee comme une fin de ligne, sans modifier les archives originales. Ne pas confondre l'identite numerique observee et une identite binaire non obtenue des archives source.

## Diagnostics et echelle

La coupe de laboratoire est extrudee sur un cadre de 1 000 000 x 1 000 000 unites, grille 128 x 128, pas 7812.5 unites. Ce n'est PAS un monde procedural, ni une preuve qu'un modele 1D fragmente deja les continents de l'atlas. Les coupes indiquent l'EPAISSEUR DE CROUTE, pas une altitude ; aucune nouvelle heightmap solide n'a ete produite. Hors du porteur nouveau, l'age est absent (etiquette -1), pas un ocean d'age zero. Les fichiers float64 derives des JSON gardent un masque explicite de presence.

Le test de changement d'echelle 131072/262144/1000000 conserve les memes dates et le checksum de l'atlas entier. Il verifie cette brique de chronologie, pas l'ensemble de la generation des reliefs.

## Prochaine integration necessaire

Produire les chargements et les chemins de rupture depuis les materiaux et le champ de contraintes bidimensionnel du monde, distinguer provenance geologique et appartenance mecanique apres separation, puis suivre fermeture/recyclage et etat thermique. La coupe actuelle recoit ses resistances et son critere de rupture ; apres rupture ses fragments translatent sans deformation interne. Ces hypotheses ne doivent pas etre presentees comme une tectonique globale achevee.

Ne pas installer une collection de rifts arbitraires ni retoucher les pixels pour faire passer cette etape. La prochaine validation geographique exige de nouvelles heightmaps solides completes avec bathymetrie, meme atlas, echelles declarees et comparaison aux references terrestres. Aucune erosion avant cette revue.
