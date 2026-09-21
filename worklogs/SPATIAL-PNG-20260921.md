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
