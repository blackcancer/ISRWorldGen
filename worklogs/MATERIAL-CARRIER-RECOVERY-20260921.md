# Reprise du porteur oceanique apres echec 512²

Base : 279c4d31795296e642522d1bfd1fdee48b78fecf. La campagne 256² du candidat 3f7c17f9 est reussie (run 35615949881), mais la densification 512² echoue (run 35616467541) apres 42 controles courts : un moment oceanique ou une cohorte heritee a perdu son volume porteur. Le journal original ne donne pas les valeurs de la cellule ; la cause de cette cellule ne doit pas etre inventee.

Le contre-exemple reduit utilise le vrai CrustTransport.Advect : O=double.Epsilon, moment=50*O et fraction de face .125. Le volume recu arrondit a zero, le moment recu reste positif. Ce cas reproduit exactement la classe d'invariant en defaut sans simuler une generation geographique en Python.

Le candidat v3 conserve la loi positive donneur au premier ordre, mais evalue O, age et fraction heritee via les memes paquets de volume representables, retention comprise. Aucun seuil de suppression, aucune tolerance de bilan nouvelle, aucun MUSCL ni lissage des altitudes. Le recyclage calcule pareillement les traceurs sur le volume restant, pour qu'un volume retire completement ne laisse pas d'age orphelin. Les concentrations non finies sont refusees.

Cinq nouveaux controles executables couvrent ancien contre-exemple, correction, transports repetes normaux/subnormaux, recyclage total, accord avec le stencil donneur sur les valeurs resolues. Les limites, duree de 36 unites de temps, trois seeds et resolution 512² du workflow restent inchangees. Les erreurs portent maintenant origine, cellule et valeurs exactes.

Publication de la correction = pas encore preuve de reussite : seule la nouvelle execution Windows/Linux et sa comparaison qualifient le commit. En cas de nouvelle erreur, garder les donnees exactes et corriger le cas reduit, pas replier silencieusement la campagne a 256². Le modele materiel historique, les API du jeu, les registres, les sauvegardes et les dependances restent inchanges. L'erosion et l'acceptation geographique restent interdites tant que le relief brut n'est pas convaincant.
