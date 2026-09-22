# Conservation des contrastes matériels avant déformation

Base main : 1c95640a86ba2441545c1507b46ff42292a13817. Incrément expérimental, sans remplacement du générateur par défaut, sans fusion des branches de mécanique ou de chronologie océanique.

## Hypothèse et exclusions
La diffusion numérique du transport donneur peut atténuer les différences d'épaisseur et étaler les interfaces. Cette expérience évalue ce biais à matériaux initiaux et pas de temps identiques. Elle n'ajoute ni forces, ni fragmentation mécanique, ni âge océanique historique, ni relief de bruit. La préservation des contrastes ne prouve pas une géographie réaliste.

Le transport alternatif reconstruit uniquement les volumes porteurs par minmod, avec deux étapes SSP-RK2 et vitesses gelées pendant l'appel. Les moments d'âge et fractions héritées voyagent à concentration amont constante sur les mêmes paquets de volume représentables, y compris dans la combinaison convexe des étapes. Ce n'est PAS une reconstruction d'ordre deux des concentrations ni un intégrateur d'ordre deux démontré pour toute l'histoire non linéaire. Les effets d'arrondi sous-normal restent possibles sur le volume lui-même ; aucun âge ne doit survivre à un porteur nul.

Le transport historique et ses identités sont conservés. Deux entrées explicites sélectionnent le candidat ou son contrôle donneur au même pas (.20 au lieu de .35 pour le coefficient de borne temporelle). Les lois de création/recyclage, de relaxation, les matériaux initiaux, le niveau de référence et les budgets restent identiques. Une identité de transport distincte est ajoutée au checksum uniquement pour les nouveaux appels. La diffusion de relaxation géologique n'est pas confondue avec la diffusion numérique d'advection.

## Contrôles prévus
63 contrôles antérieurs et 13 nouveaux : cas sous-normal historique, support des traceurs, conservation, constantes, erreur analytique sur translation périodique, raffinement, fronts sans nouveaux extrema, symétrie des axes, invariance par translation, entrées invalides et redimensionnement de l'atlas complet.
Trois seeds fixes sur Windows/Linux, grille matérielle 512², durée 36, atlas entier 1000000². Chacune calcule contrôle donneur et candidat avec le même état initial et le même nombre de pas. Champs natifs signés en km et conversion fixe Y=168+12h conservés séparément. Les contrôles interplateformes restent à leur tolérance antérieure ; aucune acceptation géographique automatique.

## Publication et récupération
Le payload part de l'arbre de main vérifié ; seuls les neuf chemins déclarés de cet incrément changent. Les deux fichiers existants sont appariés à leurs blobs main avant modification. Publication d'abord sur une branche de candidat, sans force-push ni déplacement de main. Création des blobs/arbre sans modification de référence ; commit parent explicite ; branche créée ensuite. Une interruption avant branche laisse main intact ; après branche, relire la référence et le SHA exact avant toute reprise. Ne pas recréer arbitrairement la branche ni écraser un travail concurrent.

Le lanceur refuse un répertoire de preuves déjà existant. Chaque job possède un répertoire isolé. Un arrêt laisse des preuves partielles attribuées à la campagne échouée ; absence de succès du processus ou de comparaison n'est jamais un PASS. La capture de source s'exécute même après échec et ne récupère aucune sauvegarde. Une nouvelle campagne se fait dans un espace neuf. Les tests numériques n'accèdent ni au jeu, ni au registre utilisateur, ni à une DLL Vintage Story.

Préparation locale : syntaxe Python, XML/YAML et correspondance de payload contrôlées ; référence analytique Python du transport exécutée. Exécution C#, campagne mondiale et comparaison interplateformes restent à observer dans Actions au moment de ce commit. Revue de second agent, recette native/MCP et validation géographique : NOT_RUN. La publication de cette branche expérimentale ne vaut pas validation de la garde indépendante de fusion. Érosion NON LANCÉE.
