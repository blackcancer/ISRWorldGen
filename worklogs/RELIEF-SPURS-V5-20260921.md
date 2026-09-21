# Relief brut v5 — correction des ramifications en peigne

Base exacte : 39c378f3ecc84abb72d1acab642d5ec814f87d6a. Les apports arrivés sur main pendant le travail sont préservés : provinces de socle, bassins marins, RawRidgeNetwork existant, matrice 131072/262144. Les versions préparatoires alternatives non intégrées ne sont pas ajoutées comme second générateur concurrent.

## Problème observé
La carte régionale 262144 de la seed -437287116 issue du run 35589109454 présente des crêtes centrales très linéaires et des ramifications latérales trop semblables/perpendiculaires. Ce diagnostic visuel est un REJECT de la qualité géographique, même avec une campagne numérique réussie.

## Correction
RawRidgeNetwork reçoit des branchements obliques non appariés, deux niveaux secondaires bornés, des hauteurs décroissantes et une amplitude longitudinale corrélée. La géométrie garde les vrais coudes et élimine les subdivisions collinéaires sans relancer les tirages. Les extrémités ouvertes décroissent vers le socle ; les boucles conservent leur fermeture. L'enveloppe de sommet et d'épaulement reste compacte et le maximum empêche les doubles sommets par addition aux jonctions.

Les budgets sont vérifiés avant allocation (stations, segments, références de l'index). Des entrées extrêmes sont refusées plutôt qu'utilisées dans une boucle non bornée. L'identifiant est raw-structural-relief-v5-irregular-spurs-supported-shoulders. Aucun changement de format de sauvegarde ou d'appel Vintage Story ; la génération native n'utilise pas ce candidat par remplacement implicite.

## Vérification prévue dans cette soumission
Contrôles antérieurs maintenus et suite AdvancedRidgeChecks ajoutée : géométrie copiée, subdivision/reversal, refus des entrées invalides, terminaisons, ordre/concurrence et coutures de l'index. La CI doit encore exécuter C# ; pas de PASS anticipé. Les deux emprises complètes et trois seeds restent calculées en Release Windows/Linux. Les float64 exacts et PNG16 couvrent le fond marin sans eau masquante.

## Limites de qualification
L'algorithme utilise des primitives de relief, pas une simulation de tectonique historique. La construction squelettique a un précédent méthodologique dans Génevaux et al., Terrain Modelling from Feature Primitives, 2015, doi:10.1111/cgf.12530 ; aucune garantie de cet article n'est revendiquée. Les références réelles conservées par la campagne proviennent d'ETOPO2022. Leur échelle en mètres n'est pas assimilée à celle des blocs comprimés verticalement.

Etat géographique à la soumission : NON_ACCEPTE. L'assistant doit examiner les nouvelles sorties en contexte complet ; le passage des tests ne vaut pas acceptation. Erosion, jeu, MCP et revue indépendante : NOT_RUN. L'utilisateur n'est pas sollicité comme valideur. Aucun registre de lot n'est promu.

## Intégrité
Commit descendant de la tête relue, update_ref non forcé ; tout nouveau changement concurrent impose une réconciliation. Les sorties CI sont isolées et refusent l'écrasement. Aucun secret, profil personnel ou monde du joueur n'est touché. Source, paramètres et empreintes restent dans les artefacts ; une interruption ou un échec ne devient jamais PASS.
