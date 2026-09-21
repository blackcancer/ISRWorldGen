# RCV-A/B/C — export autonome du relief solide

Base lue : d5ea4a8db8ea0292158a1957604a0de967b5e947. Aucun code C#, SDK, API du jeu,
paramètre de monde, bibliothèque NuGet, registre ou sauvegarde n'est modifié.

## Périmètre livré

`tools/export_relief_continu.py` lit les champs initiaux ou finaux float64 d'un
manifeste de campagne matérielle existante. Les sources sont vérifiées puis copiées
sans modification. Encoder PNG historique inchangé ; décodage indépendant par
Pillow. Plage Y0-383-v1, arrondi pair, palettes indépendantes du niveau marin et des
catégories. Aperçu gris calculé directement depuis le float64, pas doublement
quantifié à partir du PNG16. Les données non finies, tronquées, hors plage ou
présentées comme surface d'eau sont refusées.

Le lecteur HTML autonome présente la valeur exacte au centre de cellule sous le
curseur, le téléchargement des sources/PNG16, des coupes X/Z aux fractions 1/4,
1/2 et 3/4, leurs CSV, la comparaison de profils sur même seed/emprise et des
calques indépendants désactivés au chargement. Le niveau de référence est une
isoligne sans aplat, pas une classification d'océan connecté. Les cas ambigus sont
traités par triangulation NW-SE déclarée ; les raccords périodiques sont conservés.

La géométrie est lue dans le manifeste : pas de monde carré ou million de blocs
inventé par le rendu. Les axes et rapports graphiques sont explicités. Les pentes
sont mesurées depuis le float64 ; les altitudes natives non exportées ne sont pas
prétendues récupérées : l'inverse affine est fourni comme RECONSTRUCTION, avec ce
statut dans le manifeste et les CSV. Cette reconstruction n'est pas une preuve
bit-à-bit des altitudes internes du générateur.

## Recette et transitions

23 tests Python locaux réussis. La CI ajoutée répète les tests sous Windows et
Linux et exécute neuf assertions dans un vrai Chromium. Son résultat doit être
lu après exécution, pas déduit du YAML. Les dépendances NumPy/Pillow concernent
uniquement les outils hors mod ; Playwright seulement la recette du lecteur.

Transitions d'export : source vérifiée -> réservation exclusive du dossier ->
INCOMPLETE -> .staging -> sources copiées -> dérivés -> lecteur -> relecture des
sources -> vérification/empreintes -> bundle -> COMPLETE.pending -> renommage
atomique du marqueur COMPLETE.json. Le marqueur désigne par hash le vérificateur,
qui désigne tous les fichiers par hash. Une sortie sans reçu complet et vérifié
n'est pas une preuve publiée. L'ancien marqueur INCOMPLETE est ensuite retiré ;
un arrêt après COMPLETE mais avant ce retrait se résout par vérification du reçu.

Des interruptions sont injectées sur les sept frontières nommées ; les anciennes
preuves restent octet-à-octet intactes, la relance sur le même dossier est refusée,
et une reprise dans un dossier neuf est testée. Un changement de source avant
publication empêche la complétude. Les tentatives incomplètes sont conservées
pour inspection, jamais supprimées automatiquement ou confondues avec un succès.
La publication Git utilise un commit additif sur une branche propre, pas de
force-push. La fusion n'est pas une validation géographique.

## Utilisation

```powershell
python -m pip install numpy==2.3.5 pillow==12.0.0
python tools/export_relief_continu.py --source CHEMIN_CAMPAGNE/seed-73 --out .local/rcv-73-final --field height --exporter-commit SHA_EXPORT
python tools/export_relief_continu.py --out .local/rcv-73-final --verify
```

Le lecteur est `.local/rcv-73-final/bundle/index.html`. Une nouvelle édition exige
un nouveau dossier. `--level` ne change que les repères de lecture, jamais les
altitudes ni leurs codes. Les anciens champs doivent explicitement déclarer
`waterSurfacePresent: false`, `seabedMasked: false` ; aucun métadatum absent n'est
rempli en supposant que la source est valide.

## Limites maintenues

Cette passe ne calcule aucune nouvelle tectonique et ne corrige aucune hauteur.
Pas de traitement automatique des NoData partiels, de raffinement sous-maille,
d'identification géologique de transects ciblés, de cartes de delta intégrées ou
d'ombrage. La vue quantitative reste exploitable sans ces couches facultatives.
Le protocole complet ne reçoit donc pas un PASS global du seul exporteur.
Les tests TRCV09 navigateur et les environnements exécutés sont rapportés à part.
Le contrôle du redimensionnement du générateur n'est pas réexécuté par un lecteur.
Revue d'un second agent, validation ETOPO recalibrée, jeu et MCP : NOT_RUN.
Érosion suspendue ; acceptation géographique : NOT_EVALUATED par l'exporteur.
