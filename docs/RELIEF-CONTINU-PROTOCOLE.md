# Protocole de sortie et de revue du relief continu

**Identifiant : `RCV-1.0` — règle de recette du projet, pas une norme géologique.**  
**Périmètre : altitude du solide avant érosion, terres et fonds marins ensemble.**  
La publication de ce document ne signifie ni que ses contrôles sont implémentés, ni qu'une nouvelle campagne a été exécutée. L'érosion demeure suspendue jusqu'à la décision prévue dans `RELIEF-VALIDATION-GATE.md`.

## 0. Lecture ciblée et état constaté

Lire `02-SOCLE.md`, `15-CARTOGRAPHIE-DE-RECETTE.md` et ce protocole. Pour le verdict géographique, lire aussi `RELIEF-VALIDATION-GATE.md`. Ces compléments ne réinitialisent ni les lots ni leurs preuves.

Au point de référence `6af7cf953f3d4d2ab9a6a408313760d7afbb9b15`, `tools/export_tectonic_history.py` utilise déjà une échelle unique Y=0..383 pour les hauteurs, refuse un masque marin déclaré et produit des aperçus gris depuis les mêmes valeurs. L'impression de deux niveaux ne prouve donc pas un masquage. Il faut distinguer une donnée réellement plate, une conversion verticale, une quantification d'aperçu et un défaut d'affichage. Ce constat sur un exporteur ne vaut pas audit de toutes les galeries ni de tous les candidats.

Les règles RCV ci-dessous sont des exigences pour les prochaines recettes. Les seuils graphiques sont des conventions de lecture, pas des valeurs géophysiques ni des paramètres de génération.

## 1. RCV-01 — Une seule grandeur d'altitude

Le champ principal est `Hsolide(x,z)` : sommet du substrat solide représenté par le générateur, sous la mer comme sur terre. Il ne représente ni la surface de l'eau, ni une classe de matériau, ni l'épaisseur de croûte, ni une hauteur virtuelle de routage.

Il est interdit à l'exporteur de remplacer H par `max(H, niveauMarin)`, d'utiliser une constante sous la mer, ou de modifier les hauteurs selon un masque de continent, d'océan, de province ou de plaque.

La définition exacte du solide doit être enregistrée : socle tectonique brut, sommet de roche, terrain avec sédiments, ou surface voxelisée. Des étapes différentes portent des noms différents et ne sont pas comparées comme si elles représentaient la même chose.

Conserver le champ natif avant compression verticale et le champ projeté en blocs lorsqu'ils diffèrent. Déclarer datum, unité, sens positif, formule de conversion, paramètres et éventuelle non-linéarité. Une simple identité « 1 bloc = 1 mètre terrestre » ne doit pas être inventée. Un champ non exporté reste signalé comme tel.

La continuité recherchée est celle d'une surface topographique/bathymétrique complète et lisible. Elle n'impose ni pente constante, ni absence de falaises, ni bruit dans les plaines. Une différence entre croûtes peut produire des domaines distincts ; ce sont leur organisation et leurs raccordements que la revue doit expliquer.

## 2. RCV-02 — Données exactes et heightmap numérique

### 2.1 Source faisant autorité

Conserver chaque altitude calculée en float64, little-endian, ordre des lignes explicite. Les fichiers existants peuvent garder leur nom (`height.f64le`, etc.) ; le manifeste relie chaque rôle à son chemin, sans renommage implicite.

Pour une grille centrée sur les cellules :
`x(i)=xmin+(i+0,5)*dx`, `z(j)=zmin+(j+0,5)*dz`.
Les dimensions sont `nx`, `nz`, pas nécessairement égales. Déclarer orientation des lignes et des axes, limites de domaine et raccord périodique éventuel.

Les valeurs non finies ou manquantes ne deviennent jamais zéro. Une sortie partielle conserve son masque de validité et son statut d'échec/incomplétude, sans pouvoir obtenir une acceptation du monde complet. Aucun `NoData` ne doit se confondre avec une altitude valide.

### 2.2 PNG16 de mesure

Exporter un PNG sans perte, un canal gris, profondeur 16 bits, type couleur 0, sans palette indexée, transparence, légende ni calque intégré. Les pixels sont des codes numériques ; le lecteur de mesure ne doit pas leur appliquer une correction de couleur.

Pour une plage gelée `[Hmin,Hmax]` et un arrondi au plus proche, égalités vers l'entier pair :

```text
q = round_to_even(65535 * (H - Hmin) / (Hmax - Hmin))
H_decode = Hmin + q * (Hmax - Hmin) / 65535
erreur_max_admise = (Hmax - Hmin) / (2 * 65535) + epsilon_numerique
```

Déclarer l'epsilon de calcul avant exécution ; il n'autorise pas à masquer une erreur d'encodage. Quantification sans écrêtage : une valeur hors plage fait échouer l'export concerné ; les sources exactes restent conservées.

Le PNG numérique ne contient pas de chunks imposant une transformation colorimétrique (`gAMA`, `sRGB`, `iCCP`, `cICP`, `cHRM`) ; cette convention est vérifiée. Le PNG est un encodage quantifié, pas une conservation exacte des float64.

Le profil existant `Y0-383-v1` reste disponible : `Hmin=0`, `Hmax=383`. Son erreur théorique de quantification est d'environ 0,002922 bloc. Ce n'est pas une hauteur de monde imposée au mod. Une autre plage exige un profil versionné, fixé pour toute la comparaison, et le réexport des deux côtés depuis leurs sources.

Conserver les altitudes natives signées, notamment négatives sous le datum marin, dans leur unité de modèle. Un PNG en Y absolu positif ne supprime pas ces altitudes relatives lorsqu'elles sont correctement documentées.

## 3. RCV-03 — Vues dérivées, aucune autorité sur le terrain

### Vue A — référence grise, obligatoire et affichée en premier

`height-preview.png` est dérivée du même champ, avec `g=round_to_even(255*(H-Hmin)/(Hmax-Hmin))`, puis `R=G=B=g`. Même plage sur toutes les seeds et toutes les étapes comparées. Aucun alpha.

Sous `Y0-383-v1`, un niveau gris 8 bits couvre environ 1,502 bloc : des valeurs différentes peuvent donc paraître identiques. L'interface doit proposer la valeur float64 sous le curseur, le PNG16 et les profils ; elle ne remédie pas à cette limite en inventant du détail.

Aucun ombrage, contour marin, relief artificiel, filtre CSS de contraste ou changement de gamma n'est actif au chargement. Le profil d'affichage de l'aperçu est déclaré et constant ; le contrôle numérique utilise toujours la source, pas une capture d'écran.

### Vue B — couleur globale, complément de lecture

La couleur dépend uniquement de `t=(H-Hmin)/(Hmax-Hmin)`. Elle n'utilise ni le niveau de mer ni les classes géologiques. Une altitude identique reçoit la même couleur dans toutes les cartes comparées.

Palette de projet `altitude-sequentielle-v1`, interpolation linéaire des composantes sRGB encodées, arrondi vers l'entier pair :

| t | Couleur |
|---|---|
| 0,00 | `#101820` |
| 0,25 | `#30465A` |
| 0,50 | `#62788B` |
| 0,75 | `#A4B4BF` |
| 1,00 | `#F4F1E6` |

Les composantes croissent sur chaque intervalle ; aucune borne ne dépend de la mer. Publier les 256 entrées RGB effectivement utilisées, leur hash et une légende graduée en unités d'altitude. Cette palette est une convention de lecture continue, pas une convention géologique universelle.

Une palette topographique/bathymétrique traditionnelle peut être fournie dans une vue de localisation supplémentaire, identifiée comme telle. Elle ne remplace ni la vue A ni la vue B et ne sert pas seule au verdict sur les raccordements.

### Vue C — pente et ombrage séparés

Calculer les pentes depuis le champ exact, en précisant différences finies, unités et traitement des bords. Les pentes en espace du modèle et en espace de jeu sont distinctes si la conversion est anisotrope.

L'ombrage est une couche désactivable : azimut 315°, élévation 45° par défaut, conventions d'axes enregistrées. Aucune exagération verticale implicite. Une vue exagérée supplémentaire affiche son facteur et ne remplace pas la vue non exagérée. Ne pas utiliser un unique éclairage comme preuve d'absence d'artefact directionnel.

### Vue D — différences avant/après

Exporter `delta-height.f64le = Hcandidat - Hreference` sur les mêmes coordonnées et au même stade. La carte de différence utilise une plage symétrique gelée avant comparaison et signale tout dépassement ; elle ne change pas les cartes d'altitude.

Même état initial et mêmes conversions pour isoler un algorithme. Si l'initialisation change, publier les deux états initiaux et décrire ce qui n'est plus contrôlé. Pas de comparaison pixel à pixel entre grilles incompatibles sans méthode de rééchantillonnage documentée.

## 4. RCV-04 — Mer et frontières en calques indépendants

Le calque marin principal est une isoligne `Hsolide = niveauMarin`, extraite des valeurs exactes, désactivée par défaut. Aucun remplissage opaque, même lorsque le calque est activé. Publier ses segments et paramètres d'interpolation ; la ligne n'a pas une précision supérieure à la grille source.

L'isoligne altimétrique peut aussi entourer une dépression intérieure : ce n'est pas automatiquement une côte océanique. Une couche « océan connecté » nécessite le calcul de connectivité et ses conditions aux limites ; sinon l'appeler seulement « sous le niveau de référence ». Ne pas inventer une source océanique sur une bordure de recadrage.

Plaques, matériaux, provinces, régions et contraintes sont des calques distincts. Ils servent à expliquer un relief, pas à le peindre. La grille technique n'est pas tracée par défaut.

Test impératif : sur un même champ H gelé et une même plage, déplacer le niveau du calque marin ne change aucun pixel des vues A/B, aucun champ de pente, ni aucune valeur de profil. Seuls l'isoligne et les diagnostics explicitement dépendants de ce seuil changent. Ce test porte sur l'affichage : il n'affirme pas qu'une modification physique de charge d'eau doit laisser une nouvelle simulation inchangée.

## 5. RCV-05 — Montrer le monde, puis les ensembles et les détails

La première vue couvre le monde ENTIER configuré. L'emprise du calcul tectonique et l'emprise du monde en blocs sont toutes deux inscrites au manifeste. Préserver le mode atlas entier redimensionné : réduire la carte ne signifie pas découper une fenêtre dans un continent.

La campagne historique 1 000 000² à 512² reste une base de comparaison grossière. L'objectif de lecture globale est au moins 1024² mesures effectivement produites quand le solveur le permet ; une interpolation depuis 512² ne satisfait pas cet objectif. Ne pas dépasser silencieusement les budgets des solveurs pour afficher une résolution supérieure.

À chaque image, indiquer :
- emprise et pas altimétrique réels ;
- résolution matérielle et résolution mécanique, séparément ;
- résolution du rendu et éventuel agrandissement ;
- stade : état initial, après tectonique, avant érosion, ou autre stade explicite.

Les fenêtres régionales incluent les ensembles complets : bassin océanique, marge et arrière-pays, chaîne et piémonts. Une emprise indicative de 262 144 blocs peut être élargie plutôt que couper l'ensemble. Les fenêtres locales sont encadrées sur la grande vue et ne remplacent jamais celle-ci.

Un recadrage du même champ conserve son pas source. Un vrai raffinement doit déclarer son algorithme, ses apports du contexte global et ses conditions de raccord. Une structure sous-résolue reçoit `NOT_EVALUATED_AT_THIS_SCALE`, pas un PASS obtenu par agrandissement.

Aucun rééchantillonnage des données de référence dans la vue 1:1. Au zoom entier, privilégier le plus proche. Les vignettes éventuellement réduites sont identifiées avec leur méthode et ne servent pas à mesurer des minima/maxima.

Une carte rectangulaire respecte son rapport d'aspect. Un objet qui traverse un raccord périodique est examiné dans une fenêtre déroulée repérée, sans dupliquer ses surfaces dans les métriques.

## 6. RCV-06 — Profils continus et métriques

Pour chaque monde, publier six transects systématiques : trois traversées complètes selon X aux fractions de Z 0,25/0,50/0,75, et trois selon Z aux fractions de X 0,25/0,50/0,75. Ils sont gelés avant comparaison.

Ajouter des transects ciblés traversant les grandes structures présentes : marge passive, dorsale, zone de convergence et massif. Choisir les tracés sur la référence et les conserver pour le candidat. Les nouvelles anomalies peuvent avoir des tracés supplémentaires, explicitement ajoutés après observation. Une structure absente ou non résolue est signalée, pas remplacée par un exemple fabriqué.

Chaque profil fournit le CSV des coordonnées, distance, H natif et H en blocs, ainsi que sa méthode d'échantillonnage. Montrer les échantillons. Un trait d'interpolation linéaire reste annoncé comme tel, sans lissage ni interprétation de détail sous-maille. Les axes et l'exagération verticale sont identiques dans une paire avant/après. Le niveau marin est une simple droite ; pas d'aplat cachant le fond.

Mesurer sur le float64 :
- minimum, maximum, quantiles et distribution d'altitudes du monde entier ;
- pentes et dénivelés locaux à plusieurs distances explicites ;
- distribution des différences entre voisins et coupes traversant les marges ;
- surfaces quasi planes, valeurs répétées et saturations éventuelles ;
- directions préférentielles, raccords de tuiles et de domaine ;
- différences avant/après sur des domaines géographiques figés.

Les statistiques séparées au-dessus/en dessous de la mer sont complémentaires, jamais l'unique lecture. Elles ne doivent pas masquer qu'un pixel a changé de catégorie. Les seuils de quasi-planéité, les fenêtres et les règles de sélection sont fixés avant l'essai ; les indicateurs ne sont pas des scores de réalisme à maximiser.

## 7. RCV-07 — Tests à implémenter

Les IDs `TRCV-*` sont locaux à ce protocole ; ils ne remplacent aucun ID de lot existant. Leur présence dans le document ne signifie pas qu'ils ont été exécutés.

| ID | Contre-exemple et résultat exigé |
|---|---|
| TRCV-01 | Décodage PNG par une bibliothèque indépendante : dimensions, gris16, absence de calque/alpha et quantification conformes pour tous les pixels valides. |
| TRCV-02 | Rampe traversant le niveau marin : ordre des codes non décroissant, erreur bornée, aucune marche ajoutée au seuil. Des égalités de codes dues à la quantification sont normales. |
| TRCV-03 | Même H, niveau de calque déplacé : champs, pixels A/B et profils inchangés ; isoligne recalculée. |
| TRCV-04 | Même H, labels continent/océan/plaque permutés : vues A/B identiques. |
| TRCV-05 | Même altitude dans deux cartes dont les autres extrema diffèrent : même code et même couleur, sans normalisation automatique. |
| TRCV-06 | Pics, cuvette et points asymétriques connus : extrema préservés dans le brut ; ordre des axes, retournements, dimensions et coordonnées corrects. |
| TRCV-07 | NaN, infini, valeurs manquantes, fichier tronqué et dépassement de plage : échec explicite, pas de zéro ou de saturation silencieuse. |
| TRCV-08 | Lecture des chunks PNG et de la table couleur : aucune correction de mesure implicite ; palette et plage conformes à leur hash. |
| TRCV-09 | Masquage/réaffichage de tous les calques : le tampon d'altitude et la base sans calque sont inchangés. |
| TRCV-10 | Profils et différences : valeurs exactes aux points de grille, interpolation annoncée aux autres points ; aucune source supplémentaire créée au recadrage. |
| TRCV-11 | Raccords périodiques/tuiles, monde rectangulaire et changement de taille : orientation, voisinages, atlas entier et résolutions déclarées conformes. |
| TRCV-12 | Interruption/reprise d'export, dossier déjà présent et sources modifiées : sortie incomplète jamais publiée comme complète ; preuves antérieures intactes et reprise sans écrasement. |

Le lecteur indépendant ne réutilise pas le décodeur maison de l'encodeur. Comparer les valeurs décodées et leurs erreurs ; des différences d'octets de compression PNG ne prouvent pas, seules, une différence de hauteur.

Les tests interplateformes gardent leurs tolérances déjà qualifiées, enregistrées dans le rapport. Ce protocole n'autorise aucun élargissement.

## 8. RCV-08 — Preuves, publication et verdict

### Dossier d'une campagne

```text
campagne/seed/etape/
  manifest.json
  height.f64le
  elevation-model.f64le          # si différent de H en blocs
  png/height-16bit.png
  png/height-preview.png
  png/height-continuous-color.png
  overlays/
  profiles/
  metrics.json
  verification.json
  review.md
```

Cette arborescence nomme des rôles : conserver les chemins existants lorsqu'un manifeste les décrit déjà. Les fichiers volumineux restent dans les artefacts, pas dans l'historique du code.

Le manifeste inclut identifiant de protocole, seed, commit générateur, commit exporteur, état dirty et patch le cas échéant, runtime/OS, version algorithmique, paramètres, stade, dimensions et métrique de chaque grille, origine, orientation, topologie, unités/datums, transformation verticale, niveau du calque, bornes d'encodage, arrondi, palette, absence de masque, validity/noData, hashes et provenance des références. Le hash du champ exact relie toutes ses vues.

Construire dans un nouveau dossier temporaire. Écrire le marqueur de complétude seulement après les vérifications réussies. Ne pas écraser une campagne antérieure. Une réédition graphique depuis des données historiques indique les deux versions : elle ne devient pas une nouvelle génération. La récupération et le nettoyage doivent être qualifiés avant leur automatisation.

### Deux verdicts distincts

`verification.json` donne les statuts numériques réels `PASS`, `FAIL`, `BLOCKED`, `NOT_RUN`.

`review.md` conserve les statuts géographiques déjà définis par le projet : `REJECTED`, `ACCEPTED_RAW_RELIEF_WITH_LIMITATIONS`, `NOT_EVALUATED`. Un exporteur ne peut pas produire seul l'acceptation géographique.

L'assistant examine les vues entières, les coupes et les mesures ; il motive son verdict par comparaison à des reliefs réels, sans reporter cette responsabilité sur une validation esthétique de l'utilisateur. Une revue indépendante exigée ailleurs reste distincte et ne doit pas être inventée.

Utiliser des données topographiques/bathymétriques réelles telles qu'ETOPO 2022, avec produit, version, emprise, projection, unité, datum, résolution, noData, transformations et hash. Choisir des contextes variés avant la campagne. Une référence déjà érodée ne commande pas d'inventer des détails d'érosion dans le socle brut. Ni un histogramme bimodal, ni une plaine, ni une simple différence d'altitude entre grands domaines ne constitue, isolément, une preuve d'échec.

Les trois seeds historiques restent visibles, y compris les échecs. Avant toute qualification générale, définir un ensemble supplémentaire de seeds non utilisées pour régler les paramètres ; aucune seed n'est éliminée pour son mauvais résultat.

Refuser le relief brut lorsqu'une morphologie artificielle importante est démontrée, même si l'encodage passe. Un fond plat dans les données ne se corrige pas avec une palette. Inversement, si seules les vues dérivées sont fautives, corriger l'export sans modifier la géologie.

L'érosion ne reprend qu'après acceptation motivée de la base brute à son échelle requise, avec fonds marins et raccords compris. Une limite de résolution non levée ne vaut pas acceptation locale ni qualification native.

## 9. Application sans modifier la génération

Découpage d'implémentation : `RCV-A` données/encodage et tests TRCV-01..08 ; `RCV-B` galerie/calques/profils et tests TRCV-09..11 ; `RCV-C` publication/récupération et TRCV-12, puis comparaison d'une campagne figée avec un décodeur indépendant. Le contrôle géographique vient ensuite, sur les sorties conformes.

Ne pas lancer une nouvelle simulation uniquement pour changer l'affichage : réutiliser d'abord les float64 existants, conservés intacts, avec une nouvelle provenance d'export. Ne pas modifier SDK, NuGet, API Vintage Story, world settings, registre ou sauvegardes pour appliquer ce document.

## Sources et portée

Constats de code : `tools/export_tectonic_history.py`, `docs/15-CARTOGRAPHIE-DE-RECETTE.md`, `docs/RELIEF-VALIDATION-GATE.md`, lus sur la base `6af7cf953f3d4d2ab9a6a408313760d7afbb9b15`.

Référence technique pour les types de PNG et les métadonnées colorimétriques : W3C, PNG Specification, Third Edition, sections 6.1 et 11.3.2, `https://www.w3.org/TR/png-3/`.

Référence de données réunissant topographie et bathymétrie : NOAA NCEI, ETOPO Global Relief Model, `https://www.ncei.noaa.gov/products/etopo-global-relief-model`.

Les noms de fichiers proposés, la palette, les parcours et les critères de lecture RCV sont des décisions de projet. Ces sources ne constituent pas une validation du générateur.
