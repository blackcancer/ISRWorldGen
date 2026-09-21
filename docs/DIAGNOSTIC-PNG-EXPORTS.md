# PNG des résultats de tests

Le workflow **Spatial diagnostic PNGs** calcule les champs avec les classes réelles
du Core et publie les fichiers via les artefacts GitHub Actions (30 jours). Il ne
lance pas le jeu et ne crée ni release du mod ni verdict de qualification native.

## Fixture actuelle

Seed `-437287116`, profil explicite `balanced`, 64 sites d'atlas. Le constructeur
de fixture et les contraintes sont ceux des tests L03-B existants, pas un nouvel
audit de l'installation du joueur. La grille est **256 x 256 mesures** sur
131 072 x 131 072 blocs, au centre de cellules de 512 blocs. X augmente à droite,
Z vers le bas. Un pixel n'est donc pas un bloc. Les aperçus 1 024 x 1 024 sont des
agrandissements au plus proche, sans information ajoutée.

Le relief est celui de `LandscapeModelBuilder`, avant érosion L06. Les régions
sont ses cellules Voronoï dominantes, les plaques ses IDs tectoniques. Le drainage
utilise un graphe de voisinage à huit directions échantillonné sur ce relief. Les
eaux sous le niveau marin ne sont océaniques que si une connexion à un bord du
monde est établie par un masque séparé. Ces cellules sont les terminaux marins
explicites de cette fixture. Ce protocole ne matérialise pas les rivières en blocs.

Climat et bilan de l'eau viennent des solveurs L04 réels. Le substrat uniforme,
les coefficients de sol et la continentalité simplifiée sont des entrées de test
explicitement enregistrées, pas les distributions L14/L15 finales. Humidité du sol
ne signifie pas fertilité. Ni minerais, végétation, neige/glace ni grottes ne sont
représentés. Les tons clairs de la carte de relief indiquent une altitude.

## Livraison

Dans l'artefact : `spatial-diagnostics/balanced-seed--437287116/` contient les
valeurs exactes `fields.json`, leur SHA-256 et les paramètres dans `manifest.json`.
Le dossier `png/` contient 14 couches PNG natives, huit scalaires en 16 bits et six
couches RGB. Les palettes fixes, unités, bornes, valeurs hors palette et checksums
sont inscrits dans `manifest-png.json`. `png/index.html` est une galerie consultable
hors ligne ; `png/preview/` contient les agrandissements de lecture.

Couches : hauteur physique, hauteur virtuelle de routage, relief coloré, régions,
plaques, familles de paysages, bassins, hydrologie superposée, accumulation de
flux, température, précipitations, ruissellement, recharge et humidité du sol.
La hauteur de routage n'est jamais présentée comme un terrain physiquement rempli.
Les PNG scalaires sont quantifiés dans la palette annoncée ; les valeurs JSON
conservent la précision du calcul. Aucun lissage ou contraste automatique par
image ne masque les raccordements. Les couches purement catégorielles conservent
la table ID/couleur. Les assertions contrôlent domaines numériques, répétabilité,
conservation, invariance du relief et cohérence des receveurs avant publication.

## Reproduction

Relancer le workflow depuis Actions, ou exécuter le test `SpatialDiagnosticExportTests`
du projet portable. Il utilise `ISR_SPATIAL_DIAGNOSTICS_ROOT` si défini, sinon un
nouveau répertoire jetable sous `.local/spatial-diagnostics`. Le chemin est écrit
dans le résultat de test. Exporter ensuite avec un Python réel 3.10+ :

```text
python tools/export_spatial_png.py --root CHEMIN_PARENT_DES_FIXTURES --self-test
```

Le script refuse un dossier `png/` existant ; il ne remplace pas un résultat ancien.
Il n'ajoute aucune dépendance Python ou DLL. Une interruption laisse un artefact
incomplet sans manifeste PNG de succès ; repartir dans un nouveau répertoire.
Ne jamais utiliser ces sorties comme données de sauvegarde du monde.
