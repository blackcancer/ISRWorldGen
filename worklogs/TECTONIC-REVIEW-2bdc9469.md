# Verdict de l'incrément tectonique — 2bdc9469

## Décision : retirer le transport couplé expérimental de main

Base conservée : be40b872584744f3289d040bd05d514f8289cb1d. Essai rejeté : 2bdc94690710dbe3ffc310fe87171d4048a20003, run 35607252297. L'essai reste récupérable par son commit et ses artefacts ; aucun historique n'est forcé. La présente correction restaure exactement les quatre fichiers modifiés du candidat à leurs blobs de be40b872 et supprime ses deux fichiers ajoutés. Le correctif de polarité, ses contre-exemples et tous les contrôles interplateformes sont conservés.

## Résultats réellement observés

Les quatre jobs Windows/Linux × donneur/couplé compilent et réussissent leurs 37 contrôles locaux. Les six champs Linux (3 seeds × 2 méthodes) ont été téléchargés et contrôlés : SHA-256 float64, dimensions, décodage PNG16 avec Pillow, correspondance exacte entre pixels et quantification, absence de masque marin et bilan volumique. L'état initial est identique entre méthodes. Ce succès local ne suffit pas.

Le job de comparaison couplée 106358401259 échoue : 31.676827150870906 blocs de différence Windows/Linux sur la seed -437287116, contre une tolérance inchangée de 1e-8. Les premières différences de bilan apparaissent dès le pas 2 (temps 0.26089330846331). Les autres seeds présentent aussi des différences. Certains âges moyens deviennent démesurés dans les colonnes océaniques quasi vides ; cela exige de raisonner sur les moments extensifs et les relations entre traceurs, pas de lisser l'image. La cause algorithmique minimale du transport couplé n'est pas encore isolée avec certitude : ne pas convertir cette hypothèse en diagnostic prouvé.

La comparaison donneur atteint son étape de vérification avec succès ; son job est marqué cancelled par le fail-fast du job frère. La campagne complète de 2bdc9469 reste FAILED, jamais PASS. La référence be40b872 avait, elle, réussi les trois jobs du run 35605225740, comparaison interplateforme comprise.

## Pourquoi ne pas poursuivre cet essai en aveugle

Le gain de traduction analytique ne justifie ni une différence macroscopique entre plateformes ni un PASS géographique. Aucune tolérance n'est élargie, aucune assertion n'est retirée, aucune altitude ou quantité de matière n'est écrêtée. L'optimisation est retirée au lieu de bloquer la suite du socle tectonique dessus. Une reprise de MUSCL devra commencer par un contre-exemple réduit et un transport des traceurs avec bornes cohérentes, sur une expérience séparée, pas sur une modification du monde par défaut.

## Ce que garde la reprise tectonique

Un atlas de référence entier de 1 000 000 unités, transport conservatif de croûte continentale/océanique et de son moment d'âge, création et recyclage comptabilisés, réponse d'altitude solide commune. Les cartes de 131072, 262144 et 1000000 blocs utilisent le même atlas complet et non un recadrage. Dans la campagne donneur, les trois seeds conservent respectivement 7, 9 et 4 ensembles émergés d'au moins 1 % de l'atlas, en connexité périodique ; ce seuil est un diagnostic, pas une définition géologique d'un continent. Les pas réels sont 256, 512 et 1953.125 blocs pour 512² mesures. Les images ne fournissent pas de détails supplémentaires par agrandissement.

## Verdict géographique : NON ACCEPTE

L'examen des heightmaps disponibles n'autorise pas l'érosion. Les grandes masses restent trop anguleuses, des bandes aux contacts restent trop schématiques et les grands fonds trop peu structurés. Il faut distinguer ce modèle de croûte réduit du précédent générateur de formes : la hauteur résulte ici de matériaux transportés, mais les vitesses sont encore imposées sur des domaines directeurs Voronoï mobiles. Ce n'est pas un équilibre des forces ni une reconstruction rigide complète. Flexure des fosses et arcs non implémentés.

Prochain travail géographique prioritaire : cohérence entre mouvement des domaines et transport matériel, puis réponse mécanique asymétrique aux zones de subduction. Ne pas ajouter des chaînes de bruit pour maquiller ce manque et ne pas reprendre l'érosion avant la revue du relief brut, terrestre et sous-marin.

## Limites et récupération

Le retour est un nouveau commit, jamais un reset/force-push. Aucun fichier utilisateur, aucune partie ni registre n'est modifié. Les données rejetées restent attachées au commit qui les a produites ; ne pas les attribuer au correctif de récupération. Le workflow restauré doit requalifier le point de reprise et publier ses propres preuves. Compilation complète du mod avec DLL du jeu, session Visual Studio/MCP, exécution native et revue d'un second agent : NOT_RUN. Aucune nouvelle dépendance ni aucun appel API Vintage Story n'a été ajouté.