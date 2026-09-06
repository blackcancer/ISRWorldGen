# Passation d’orchestration

## État intégré au 2026-09-06

- `main` contient le cœur analytique L02-C, l’adaptateur natif T02-05 et toute la chaîne L00-C jusqu’au commit intégré `2af16c432f3e16b7ebf97b693a605d199198610f`.
- L00-A, L00-B, L01-A/B/C et L02-A/B sont `DONE`.
- L00-C reste `IN_PROGRESS` : revue statique `ACCEPT`, moteur T00-04/05/06 `NOT_RUN` sur le candidat intégré.
- L02-C reste `IN_PROGRESS` : cœur analytique et opt-in natif statique `ACCEPT`, moteur T02-05 `NOT_RUN`.
- L10-A reste `IN_PROGRESS` : abstraction acceptée, T10-01 attend encore le monde intégré final.

## Revues et régression

- L00-C auteur `agent:l00-c`, dernier SHA relu `78c94687e6105c6beeddf7bf64f700155910a71b`, aucun P1/P2.
- T02-05 natif auteur `agent:l02c-native`, SHAs relus `c058b9a790dcef49625d7791e1de34f5eaad3451` et `3bc0a7f9ceb9d7cd12b94de35c727158378f2c11`, aucun P1/P2.
- État combiné : restore et format PASS, solution Debug/Release 0 avertissement/0 erreur, 186/186 tests .NET dans chaque configuration, gates L00-C et T02-05 Debug/Release PASS.
- L’ancienne campagne L00-C sur `f118017` est un échec conservé : 9/9 mapchunks mais 0/72 chunks voxel. Elle ne vaut pas preuve du candidat courant.

## Verrou Visual Studio / jeu

- Dernier contrôle : aucune solution ouverte, debugger `Design`, aucun processus débogué.
- Prochain détenteur prévu : l’agent L00-C, uniquement après checkpoint Git propre.
- Tous les lancements serveur et client doivent passer par Visual Studio/MCP.
- Le client utilise exclusivement le premier profil `ISRWorldGen Client (authenticated user data)` sans `--dataPath`; aucune sauvegarde personnelle ne doit être ouverte.

## Prochaine action exacte

1. Valider et pousser le checkpoint combiné documenté.
2. Lancer une campagne serveur L00-C neuve sous Visual Studio en suivant obligatoirement `Initialize → RecordOpen1 → AuthorizeOpen2 → Finalize`; exiger 9/9 mapchunks et 72/72 chunks avant open2.
3. Exécuter T02-05 sur des sauvegardes uniques sous `.local/T02-05/` : profil laboratoire valide, reload, refus hauteur et refus dimensions rectangulaires.
4. Exécuter la séquence client T00-06 depuis le profil authentifié Visual Studio.
5. Mettre à jour registre et checksums, pousser, puis déléguer L03-A et L11-A si leurs dépendances sont réellement closes.
