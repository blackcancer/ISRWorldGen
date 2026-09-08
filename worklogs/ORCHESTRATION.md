# Passation d’orchestration

## État intégré au 2026-09-06

- `main` contient le cœur analytique L02-C, l’adaptateur natif T02-05 et toute la chaîne L00-C corrigée jusqu’au commit intégré `44afa0f96df4581a34a5e568014868f8c967335c`.
- L00-A, L00-B, L01-A/B/C et L02-A/B sont `DONE`.
- L00-C reste `IN_PROGRESS` : revue statique `ACCEPT`, moteur T00-04/05/06 `NOT_RUN` sur le candidat intégré.
- L02-C reste `IN_PROGRESS` : cœur analytique et opt-in natif statique `ACCEPT`, moteur T02-05 `NOT_RUN`.
- L10-A reste `IN_PROGRESS` : abstraction acceptée, T10-01 attend encore le monde intégré final.

## Revues et régression

- L00-C auteur `agent:l00-c`, dernier SHA relu `3c15e6cbb06fe75cf69d6e6c7992288df53c0cab`, aucun P1/P2.
- T02-05 natif auteur `agent:l02c-native`, SHAs relus `c058b9a790dcef49625d7791e1de34f5eaad3451` et `3bc0a7f9ceb9d7cd12b94de35c727158378f2c11`, aucun P1/P2.
- État combiné `44afa0f` : restore et format PASS, solution Debug/Release 0 avertissement/0 erreur, 186/186 tests .NET dans chaque configuration et gate L00-C Debug/Release PASS.
- L’ancienne campagne L00-C sur `f118017` est un échec conservé : 9/9 mapchunks mais 0/72 chunks voxel. Elle ne vaut pas preuve du candidat courant.

## Verrou Visual Studio / jeu

- Dernier contrôle : aucune solution ouverte, debugger `Design`, aucun processus débogué.
- Prochain détenteur prévu : l’agent L00-C, uniquement après checkpoint Git propre.
- Tous les lancements serveur et client doivent passer par Visual Studio/MCP.
- Le client utilise exclusivement le premier profil `ISRWorldGen Client (authenticated user data)` sans `--dataPath`; aucune sauvegarde personnelle ne doit être ouverte.

## Prochaine action exacte

1. Pousser le checkpoint `44afa0f` documenté.
2. Lancer une campagne serveur L00-C neuve sous Visual Studio en suivant obligatoirement `Initialize → RecordOpen1 → AuthorizeOpen2 → Finalize`; exiger 9/9 mapchunks et 72/72 chunks avant open2, puis valider le snapshot cartographique séparé.
3. Exécuter T02-05 sur des sauvegardes uniques sous `.local/T02-05/` avec l’arrêt différé post-RunGame : profil laboratoire valide, reload sans écriture, refus hauteur et refus dimensions rectangulaires.
4. Exécuter la séquence client T00-06 depuis le profil authentifié Visual Studio.
5. Mettre à jour registre et checksums, pousser, puis déléguer L03-A et L11-A si leurs dépendances sont réellement closes.

## Reprise au 2026-09-07 — L02-C clôturé, voie Core ouverte

- `main` est publié sur
  `0d7a1cce329c1d68c2ea22fa77169d540d4e9fe4`; format, builds Debug/Release et
  195/195 tests sont verts dans les deux configurations.
- L02-C est `DONE`. T02-05 v4 a reçu un verdict indépendant `ACCEPT` sur la
  campagne `20260907T034524847`; T02-06 était déjà accepté. L03-A devient la
  prochaine mission admissible.
- L00-C reste `IN_PROGRESS` uniquement pour T00-06 client. Sa campagne serveur
  `55bd4bdeb0d049009610a12aa80fdf66` est `ACCEPT` : T00-04/T00-05 et le
  sous-scope serveur T00-06 passent. Le profil client F5 est bloqué par une clé
  de session invalide et attend une réauthentification utilisateur.
- L11-A n’est pas admissible tant que L00-C n’est pas `DONE`. L10-A reste
  `IN_PROGRESS` et attend le monde intégré requis par T10-01.
- Le verrou Visual Studio/MCP/jeu est libre : solution fermée, débogueur
  `Design`, zéro breakpoint et zéro processus Vintage Story.

Prochaine action : valider et pousser ce registre, puis déléguer L03-A sur ce
checkpoint. Dès que l’utilisateur confirme la reconnexion Vintage Story,
reprendre T00-06 client par F5 avant L11-A.

## Reprise au 2026-09-07 — L03-A intégré

- Le candidat L03-A a d'abord été rejeté sur quatre P2 : faux profils de
  campagne, seuil 75 % insuffisamment gardé, publication prématurée de PASS et
  coût tectonique sans budget couplé. Aucun de ces défauts n'a été intégré.
- Le candidat corrigé `6b2c8a98050bfacc454bcb322549fe53add50fc9` a ensuite
  reçu un verdict indépendant `ACCEPT` sans finding restant. Les profils réels
  `balanced` et `vast-expeditions`, 192+64 seeds, les frontières de tuiles, le
  préflight `edges × cells` et les sept cartes ont été vérifiés.
- Le code est intégré sur `main` à
  `f6330f834dda1683eebe697ac775eedab9456764`. Format, builds Debug/Release,
  ciblés 10/10 dans les deux configurations et global Release 205/205 sont
  verts sur l'intégration.
- T03-01 et T03-02 sont `PASS`; L03-A est `DONE`. La prochaine mission
  admissible est L03-B.
- L00-C reste en attente de la seule réauthentification client T00-06 et ne
  bloque pas la chaîne Core L03.

Prochaine action : valider et pousser ce checkpoint, puis déléguer L03-B sur
le hash publié exact. Reprendre T00-06 par F5 dès confirmation de connexion.

## Reprise au 2026-09-07 — provenance L03-A renforcée

- La première revue de L03-B a exposé une collision de parenté : un Atlas B
  pouvait être associé à l'identité et aux plaques A sans modifier le checksum.
  Le candidat L03-B concerné n'a pas été intégré.
- Trois correctifs L03-A scellent maintenant le contenu Atlas, l'identité
  complète et le profil dans un checksum binaire V3 invariant à la culture ; la
  chaîne de profil de déterminisme est bornée à 128 octets UTF-8 avant allocation.
- Le candidat final agent `cfaa29f27d70a19d1e905a7bab532fa6fad2fc6e`
  est `ACCEPT` indépendant, P1=0/P2=0/P3=0. L'intégration racine aboutit à
  `16254dafc745867fe508bad01b5d6d75423bf919` avec format et builds verts,
  L03-A 13/13 en Debug et Release, puis 208/208 tests globaux Release.
- L03-A reste `DONE`. L03-B doit repartir de ce hash publié, refuser les parents
  mélangés et soumettre une nouvelle campagne aveugle distincte de V01–V06.

Prochaine action : valider le registre et publier ce checkpoint, puis reprendre
le correctif L03-B avec un agent d'implémentation GPT-5.6 Terra Medium.

## Reprise au 2026-09-07 — politique Terra et campagne S L03-B rejetée

- Le checkpoint publié `c50f05eb83a47b42864893c0b9cf65e459ac08c5` fixe Terra
  Medium comme profil local par défaut, Sol Medium/xHigh comme expertise
  ciblée et Astra comme capacité future non encore exposée sur ce poste. Les
  outils de délégation permettent de demander explicitement Terra ou Sol ; le
  modèle effectivement exécuté n'est pas exposé et reste `NON_VERIFIE`.
- Après revue Terra et préflight détaché neuf, le candidat L03-B
  `ee92c24a54cb662091ed816493d5cb1fee7a2e5c` a produit un bundle S terminal
  isolé sous `.local/L03B/` : automatisation `PASS`, rapport qualitatif
  `REVIEW_REQUIRED`, commit/tree et clé/TRX/hash scellés concordants.
- La revue aveugle indépendante a validé le manifeste et les six artefacts,
  puis la levée de clé a donné **1/6** seulement. Les confusions portent sur
  S01--S05 ; seul le domaine volcanique a été reconnu. Le verdict qualitatif
  est donc `REJECT` : T03-06 et L03-B restent non qualifiés, aucun code L03-B
  n'est intégré dans `main` et le registre reste inchangé.
- Une tentative antérieure sur `78cdde8` avait échoué avant corpus, carte ou
  bundle terminal à cause de la conversion CRLF du checkout Windows. Elle est
  conservée comme reproduction non qualifiante ; `ee92c24` consomme désormais
  le blob Git brut des fixtures.

Prochaine action : analyser les cinq confusions morphologiques sur les cartes
S scellées et le code L03-B, corriger le catalogue sans affaiblir T03-06, puis
faire une nouvelle campagne aveugle avec une permutation et un worktree neufs.

## Reprise au 2026-09-07 — seconde campagne S L03-B rejetée

- Le candidat morphologique `420d4109f818d3d7cb0578280339a085fac74ec8` a reçu
  deux revues indépendantes : les bornes analytiques, les transects anti-Voronoï
  et les probes automatisées sur huit seeds ont été `ACCEPT` avant préflight.
  Aucun changement du runner ou du scellement Evidence n'a été admis.
- Un préflight détaché propre, puis une campagne S neuve et terminale, ont été
  exécutés sous `.local/L03B/` depuis ce commit. La phase aveugle a confirmé
  l'intégrité des huit artefacts et a distingué six familles sans seam, motif
  Voronoï apparent ni saturation.
- La levée de clé a néanmoins donné **1/6** : seule la plaine a été reconnue.
  Les bassins, volcans, plateaux, chaînes et vieux massifs ont été confondus.
  Le verdict T03-06 est donc à nouveau `REJECT`; L03-B demeure `BACKLOG` et
  aucun code candidat n'est intégré dans `main`.

Prochaine action : requalifier la conception morphologique, et non les seuls
tests, avant une éventuelle troisième campagne. La prochaine proposition doit
expliquer comment les primitives restent visuellement distinctes après le
mélange inter-cellules, avec revue experte préalable.

## Checkpoint 2026-09-08 — adoption 1.2

- `main` publié avant le démarrage de L05-D : `c8faebcb9ba5155adacf9600101df2b8ca8c1840`.
- Plan 1.2 intégré sans réinitialiser les 42 IDs existants ni les preuves : 19 lots, 61 tâches, 132 exigences et 132 scénarios.
- Validation documentaire : `validate_spec.py` PASS ; 9 tests du validateur et 14 tests de préparation PASS.
- L05-D est en cours : matrice de compatibilité C01–C03 vers C08–C12 et requalification des sorties hydrologie/relief.
- Risques ouverts : L04-A, L00-C et L10-A conservent leurs limites runtime historiques ; aucun statut `DONE` n’est inféré par le plan 1.2.
- Action suivante : revue indépendante puis intégration de L05-D ; L14-A et L18-A ne démarrent qu’après cette décision.
