# Revue de l'incrément domaines matériels — 8382b672

Code examiné : 8382b6727440f3e0617ac8693baaa4f7839d9320. Référence conservée : cf9bb4288f455594e34888bdc2bd8d5df03ffe08. Run : 35612892513.
Décision : contrôles numériques réussis dans le périmètre exécuté ; géographie NON_ACCEPTÉE ; érosion NON_AUTORISÉE.

## Correction retenue
L'appartenance aux plaques n'est plus recalculée au plus proche site mobile dans le mode material. Elle est initialisée sur le Voronoï puis transportée comme mesure positive avec les faces donneur utilisées pour la croûte. Ses fractions pilotent les vitesses locales et le gradient du contraste entre appartenances donne les normales de contact. Des inventaires par marqueur et des diagnostics de mélange sont conservés.
Ces mesures sont des coordonnées planes : elles ne sont ni une masse de croûte survivante par plaque ni des plaques rigides. Création de basalt, recyclage et déplacement de croûte inférieure restent distincts. Les vitesses de plaques sont encore prescrites, non résolues par équilibre de forces. Le candidat est explicitement sélectionné ; le Core garde le mode de référence par défaut.

Le premier commit ec6eb359 a compilé et réussi ses 43 contrôles courts, mais l'histoire complète 512² a dépassé 150 km d'épaisseur équivalente. Le correctif 8382b672 conserve cette limite, le transport donneur et la conversion verticale. Il maintient une largeur de couplage des vitesses, deux fois DeformationWidth, pour empêcher leur concentration à l'échelle de la grille. Trois moyennes métriques séparables approchent un noyau de portée déclarée. Seules les vitesses sont concernées, jamais les altitudes ou les inventaires de croûte. Cette fermeture cinématique n'est pas une rhéologie calibrée ni une solution d'équilibre mécanique.

## Exécutions observées
Les six jobs du run 35612892513 sont terminés avec succès : génération Windows/Linux × material/reference, puis deux comparaisons interplateformes. Les 47 contrôles exécutables comprennent les 27 antérieurs, 16 de transport/identité/contacts/échelle et 4 de couplage métrique. Chaque mode calcule trois seeds de 512² cellules sur 36 unités de temps modèle. Aucune campagne n'a été raccourcie pour éviter la défaillance.

Artefacts réellement téléchargés :
- Linux material 10645242484, SHA-256 ZIP c77ce27ef4646e684cf53f6b355c65fdd012468fe348e88f0b3b68a6c5c82f24.
- Windows material 10644907939, SHA-256 ZIP 886dd5c010ce57ee6cefb55d7ca027972be4eef741b02bf40f5232e218ab1b2d.
- Linux reference 10644727817, SHA-256 ZIP 9012bbb084f162b80e68b22a3f8d61d9ed3196676980156d795e9dc1fa97aed5.
- Comparaison material 10644778132, SHA-256 ZIP 8cd40035c39faa5f6443ce063fd855257ce4384f1137e33b836248dd0cec24d7.

Les valeurs float64, leurs SHA-256 et les PNG16 ont été vérifiés localement avec NumPy/Pillow, distincts de l'encodeur du dépôt. Chaque pixel height/initial-height correspond à code=arrondi(Y*65535/383), sans masque marin. La comparaison Windows/Linux indépendante retrouve des pixels PNG16 identiques ; écart maximal de hauteur double 3.979039320256561e-13 bloc, sans changement de la tolérance CI de 1e-8. Les dix champs antérieurs de Linux reference sont identiques octet à octet à cf9bb428 sur les trois seeds.

## Mesures de la génération, pas critères universels de réalisme
| Seed | Y maximum référence → material | Épaisseur continentale maximum material | Fraction émergée material | Ensembles émergés ≥1 % | Cellules d'appartenance différente |
|---|---:|---:|---:|---:|---:|
| -437287116 | 358.328 → 242.235 | 73.499 | 11.803 % | 3 | 8.953 % |
| 73 | 275.485 → 213.204 | 57.532 | 17.068 % | 5 | 5.650 % |
| 20260906 | 349.069 → 225.026 | 64.034 | 24.286 % | 4 | 7.927 % |

L'épaisseur est en kilomètres équivalents internes du modèle, pas en blocs ni une mesure terrestre calibrée. Les ensembles émergés sont comptés en connexité périodique avec un seuil diagnostique de 1 % ; aucun nombre de continents n'en est garanti pour des seeds arbitraires. La baisse des maxima n'est pas annoncée comme une amélioration esthétique. Le champ solide change parce que sa déformation change, pas par contraste, écrêtage ou filtrage de l'image.

## Revue géographique des six vues entières
Les trois champs material ont été ouverts et comparés à reference sur les mêmes emprises, mêmes graines et mêmes bornes de gris. Les reliefs restent excessivement doux, certaines formes initiales anguleuses persistent, et les contacts océaniques dessinent encore des bandes schématiques. Le déplacement matériel est mieux spécifié et testé, mais sa réponse mécanique et la morphologie ne sont pas acceptées. Les dimensions actuelles permettent un contrôle macroscopique, pas de valider des crêtes ou vallées à l'échelle du bloc.

Aucun nouveau jeu de références terrestres ni recalibrage morphométrique n'a été exécuté dans cet incrément. La comparaison de transport ne remplace pas la confrontation aux références ETOPO requise avant acceptation du relief. La géographie reste rejetée sans réclamer une approbation utilisateur. Ne pas compenser ces limites par une texture de montagnes ou commencer l'érosion.

## Échelle et suites
Le domaine entier mesure un million d'unités de référence. Les tailles 131072, 262144 et 1000000 blocs le mettent à l'échelle sans recadrage, avec les mêmes valeurs aux coordonnées correspondantes. Les pas sont 256, 512 et 1953.125 blocs. 512² cellules ne deviennent pas davantage de mesures par agrandissement.
Le mécanisme de domaines transportés est maintenant une base disponible pour la réponse mécanique aux contacts. L'asymétrie de flexure des fosses et les arcs ne sont PAS livrés ; l'étape suivante doit les déduire des matériaux et contraintes, non dessiner des sillons sur ces PNG. L'effet de la largeur de couplage sur le relief devra être qualifié par un modèle mécanique mieux fondé, pas uniquement choisi pour rester sous une limite.

Aucun appel Vintage Story ajouté, aucune bibliothèque activée, aucune sauvegarde, aucun registre ni profil natif modifié. Les assemblages du Core et le programme de campagne sont compilés en CI, pas la solution native complète. Jeu, MCP et revue d'un second agent : NOT_RUN. Aucun lot promu DONE. La galerie autonome conserve toutes les seeds, les emprises entières et les téléchargements PNG16 ; sa syntaxe JavaScript a été vérifiée par node --check.

Livraison hors Git : ISRWorldGen_Domaines_8382b672.zip, 114748926 octets, SHA-256 f8c4673ee4beb0b4ebb7fe4f9a234dcc9ad988f9e39d65a29467ed6261884a63. Elle comprend les deux campagnes Linux, les sources exactes, les comparaisons, la galerie et les vérifications indépendantes. Les fichiers volumineux ne sont pas ajoutés à l'historique du dépôt.
