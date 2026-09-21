# Origines materielles — raccord du candidat sur main

Base code : 8382b6727440f3e0617ac8693baaa4f7839d9320. Revue f70b59dd conservee. Aucun fichier existant remplace : le modele par coordonnees transportées reste disponible et son option par defaut ne change pas.

Le nouveau candidat suit C, O, O*age et O herité PAR ORIGINE avec le donneur existant. La creation ajoute O sans age; la subduction retire exclusivement l'origine plongeante; la redistribution continentale transporte chaque origine dans les memes proportions que le flux total. Les inventaires continentaux par origine sont verifies a chaque etape. Les normales sont lues sur les interfaces materielles presentes, sans centres mobiles.

Le couplage metrique de 8382b672 est reutilise sur les supports de vitesse des origines pour ne pas reintroduire le choc non resolu. Ce support n'est ni une rheologie calibree ni un equilibre de forces. Origine de materiau et appartenance mecanique apres accretion restent distinctes a traiter. Pas de flexure ni d'arc volcanique implementes ici.

L'initialisation reutilise TectonicHistory.Generate avec duree zero : pas de copie de l'ancien initialiseur ni de modifications du modele historique. MaterialBoundSnapshot ne presente pas une masse de croute comme les anciennes coordonnees de domaine. La simulation reste isolee dans WorldGen.MaterialBoundEvolution et ne change aucun monde natif.

Qualification preparee : 15 controles specifiques plus les controles historiques de base et de polarite, trois graines completes a 256² avant densification, Windows/Linux Release et comparaison des vrais champs/PNG16 avec les tolerances existantes. Les petites tailles reprennent le million d'unites entier, sans decoupage. Les sources de l'archive anterieure ont ete reconciliees, non recopiees sur main.

Statut a la publication de ce candidat : compilation C# et execution nouvelle CI NOT_RUN jusqu'aux journaux du commit; aucune preuve ancienne ne vaut execution de ce code. Une reussite numerique ne vaut pas acceptation geographique. Erosion, DLL natives, MCP Visual Studio et revue d'un second agent NOT_RUN. Aucun registre ni sauvegarde ni dependance modifie.

Publication additive avec base epinglee et avance rapide uniquement. En cas d'echec, conserver les sources/logs exacts; pas de relaxation des limites ni d'ecretage d'altitude pour obtenir un PASS. Une restauration eventuelle est un nouveau commit cible, jamais un reset force de main.
