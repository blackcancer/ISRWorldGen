# Publication des diagnostics spatiaux

Mandat utilisateur : publier des PNG heightmap, régions, hydro et autres résultats
de tests, pendant la poursuite autonome sur main. Base : a9c33f3a10c16906dd9dbf6701e6818e61de73ad.

Changement de tests/outillage uniquement : une fixture Core réutilise le factory
L03-B existant et appelle les véritables classes de relief/climat/drainage. Les
paramètres auxiliaires restent identifiés comme synthétiques. Aucun moteur de
monde final, pont natif, érosion ou champ encore absent n'est simulé visuellement.
Le registre d'avancement et les contrats partagés ne sont pas modifiés.

L'exporteur PNG emploie uniquement la bibliothèque standard Python. Les pixels
16 bits et RGB sont testés en encode/decode, les CRC corrompus sont refusés et
l'agrandissement au plus proche est contrôlé. Les champs C# sont protégés par leur
SHA-256 ; l'export contrôle dimensions, domaines et liens de drainage avant
production, puis vérifie chaque PNG contre ses pixels attendus. Les répertoires
existants sont refusés et la source reste inchangée. Les données brutes et palettes
sont livrées pour contrôle indépendant, pas seulement les images.

La nouvelle campagne s'exécute en Release Windows/Linux et publie les artefacts
sans secrets ni DLL du jeu. Elle n'appelle pas la recette aveugle T03-05/06.
Le projet portable lie une unique classe d'aide L03B sans ses campagnes de preuve.
Les campagnes Core existantes restent actives pour la non-régression.

Statut à la soumission : tests Python d'encodage exécutés localement et réussis ;
C# et export sur données réelles à vérifier dans Actions, aucun PASS anticipé.
NativeGame, FullSolution et revue indépendante : NOT_RUN. Le présent texte n'est
pas une attestation de réussite des contrôles natifs ou d'acceptation de lot.

## Résultats observés après exécution

Commit de données : `8f23fb57e446311d72c621033842c2bac6d6525e`.
Le premier candidat b9a5a6a a été refusé à la compilation par MSTEST0037 ; les
assertions comparatives ont été remplacées par leurs formes typées, sans changer
l'oracle. Le run final `35573354481` est SUCCESS en Release sur Windows et Linux :
test Core exécuté, vérification des sources et publication de 14 PNG originaux
plus 14 aperçus. Le run de régression `35573354409` est également SUCCESS sur
Windows/Linux en Debug/Release. Cela ne qualifie ni le jeu ni la solution native.

Artefacts relus et décodés :
- Windows Release : `10627091712`, SHA-256 de fields.json
  `7c2849b4fc39da8ffb77a9ff5202cdc25ef07445c4f8e8f2adcbfb3083180054`.
- Linux Release : `10627096767`, SHA-256 de fields.json
  `b22989ac014aa61b4715861d74499beb9c447925949c7cad78518901f33784fd`.

Les PNG ont été décodés avec Pillow indépendamment de l'encodeur Python standard.
Les huit couches scalaires 16 bits correspondent à la quantification annoncée des
valeurs C# ; les SHA et dimensions de toutes les couches et aperçus sont corrects.
Les pixels des 28 PNG sont identiques entre les deux plateformes observées.

**Attention : les tableaux double précision ne sont PAS identiques octet pour
octet entre plateformes.** Sur cette fixture, 125 altitudes diffèrent (écart max
2.842170943040401e-14 bloc), et ces écarts se propagent à certains champs climatiques
et débits (écart max de débit 2.1245796233415604e-9 L³/Ymod). Aucun ID de région,
plaque, bassin ou receveur n'a changé. La cause n'est pas diagnostiquée ici ; ne
pas annoncer une qualification binaire inter-OS, ni masquer le problème par un
arrondi de génération sans décision de contrat/migration.

Inspection visuelle : relief initial très doux à 512 blocs par pixel ; réseau
humide surtout côtier dans cette configuration climatique simplifiée. Ces cartes
servent à exposer les limites présentes, pas à prouver un paysage réaliste terminé.
Les bassins correspondent à leurs terminaux de drainage de fixture ; les champs
non implémentés (érosion finale, végétation, neige, etc.) ne sont pas fabriqués.

La suite exige une revue humaine des paysages et les validations natives déjà
prévues. Aucun statut du registre n'est promu par cette publication de diagnostics.
