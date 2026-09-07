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
