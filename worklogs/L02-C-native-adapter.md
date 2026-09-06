# Passation — L02-C / T02-05 adaptateur natif
**Statut proposé : REVIEW / BLOCKED pour la recette moteur.** Ne pas déclarer DONE avant T02-05 dans Vintage Story réel.

## Identité
- Tâche : intégration statique du probe natif L02-C / T02-05.
- Propriétaire : sous-agent `audit_l02c_native` ; branche `agent/l02c-native` ; worktree `E:\Développement\Vintage Story\ISRWorldGen-worktrees\l02c-native`.
- Base exacte : `25114a9fd95ad0f2b4278e6cf1b0d663eb127cc7`.
- Commit d'implémentation : `e370db7c03aee9dc44c17ab13a259b81a89eb332`.
- Contrats consultés et inchangés : C00 1.0, C01 1.0 ; aucun raccordement à L10-A.
- Installation inspectée statiquement : Vintage Story 1.22.7, `VintagestoryAPI.dll` assembly `1.22.7.0`, SHA-256 `034283E7E9D98EAE45EE63005576FD89BADC3C995B531CC4C3FE46F3EB2D3296` ; `VintagestoryLib.dll` SHA-256 `E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0`.
- Aucun lancement Visual Studio, client ou serveur ; aucune sauvegarde ouverte ou modifiée.

## Résultat
- Le bootstrap serveur inscrit uniquement un callback `EnumServerRunPhase.GameReady`. Ce callback lit `ICoreServerAPI.WorldManager.MapSizeX/Y/Z`, `ChunkSize`, `SaveGame.IsNew`, `SavegameIdentifier` et la sélection explicite `api.World.Config["isrworldgenProfileId"]`.
- L'absence de cette clé ne choisit aucun profil : le probe devient `Inactive`, n'écrit rien, ne s'inscrit à aucun callback worldgen et ne modifie pas les profils L00 existants.
- Les contraintes Core utilisent la seule hauteur réellement vérifiée par l'API, `SupportedHeights = [MapSizeY]`. Aucun catalogue public de hauteurs créables n'existe en 1.22.7 ; aucune whitelist d'interface privée n'est inventée. Les limites auditées sont `ChunkSize=32`, `MaxWorldSizeXZ=67_108_864`, `MaxWorldSizeY=16_384`. X/Z restent soumis au pas chunk et le coordinateur exige en plus l'égalité exacte X/Y/Z avec le profil avant et après persistance.
- Une nouvelle sélection valide est figée, enveloppée puis stockée sous l'unique clé privée `isrworldgen:l02c:frozen-profile:v1`. L'enveloppe bornée contient version, identifiant de sauvegarde, X/Y/Z, chunk, ruleset `vintagestory-1.22.7-effective-world-v1:1`, hash géographique, codec `isrworldgen.core.frozen-scale-profile:1`, blob `FrozenScaleProfileCodec` et checksum SHA-256. Toutes les frontières `byte[]` font des copies défensives.
- Publication en mémoire uniquement après `StoreData`, relecture immédiate `GetData`, égalité octet par octet, décodage de l'enveloppe et `FrozenScaleProfileCodec.Reload`. Au rechargement, aucune réécriture ; identité de sauvegarde, dimensions, chunk, ruleset, profil explicite éventuel, hash et blob sont revalidés strictement.
- Le callback `InitWorldGenerator` n'est inscrit qu'après l'état `Frozen`. Il observe ce même état avant le premier callback du mod et journalise `L02C_NATIVE_GATE_FROZEN`. Toute préparation rejetée journalise code/stage puis appelle `api.Server.ShutDown()` ; aucun throw n'est utilisé comme mécanisme d'arrêt.
- Le paquet Vintage Story embarque désormais exactement les DLL runtime `ISRWorldGen.dll` et `ISRWorldGen.Core.dll`, déclare Core dans `ISRWorldGen.deps.json`, et n'embarque pas `ISRWorldGen.Runtime.dll`.

## Tests et preuves
| Test | Commande ou capacité réellement utilisée | Statut | Rapport / preuve |
|---|---|---|---|
| Restore verrouillé | `dotnet restore ISRWorldGen.sln --locked-mode` avec `VINTAGE_STORY=D:\Jeux\Vintagestory` | PASS | 5 projets restaurés ; lock cohérent après mise à jour mécanique. |
| Format | `dotnet format ISRWorldGen.sln --no-restore --verify-no-changes --verbosity minimal` | PASS | Exit 0. |
| Build Debug / Release | `dotnet build ISRWorldGen.sln --no-restore -c Debug`, puis Release | PASS | Deux configurations : 0 avertissement, 0 erreur. |
| L02CNative ciblé | `dotnet test ... -c Debug/Release --filter FullyQualifiedName~L02CNative` | PASS | 22/22 dans chaque configuration : valid, reload, enveloppe/bloc interne corrompus, mutation sélection/monde avant et après store, rectangle, hauteur effective non supportée, refus et copies défensives. |
| Régression .NET | Build Debug et Release préalable, puis `dotnet test ... --no-build --no-restore -c Debug/Release` | PASS | 179/179 dans chaque configuration. Une première orchestration Debug seule a rencontré 8 échecs L01-C parce que ces tests exigent le binaire Tools Release ; après build Release préalable, 179/179 vert dans les deux configurations. |
| API / package / non-activation L00 | `Invoke-L02CNativeStaticChecks.ps1 -Configuration Debug/Release -VintageStoryPath D:\Jeux\Vintagestory` | PASS statique | Version/hashes DLL ci-dessus, membres XML exacts, fragments de raccordement, deux DLL runtime exactes, `ISRWorldGen.Runtime.dll` absent, aucune sélection dans `launchSettings.json`. |
| Oracle refus GameReady | `NativeProfileBridgeTests.ExplicitRefusalAtGameReady_ShutsDownWithoutEnvelopeOrFrozenGate` | PASS unitaire | `ShutdownCount=1`, `WriteCount=0`, aucune inscription worldgen, gate non-Frozen. |
| T02-05 Vintage Story réel | Visual Studio + serveur/client 1.22.7 + mondes de labo uniques | NOT_RUN / BLOCKED | Interdit dans ce sous-lot pendant le verrou détenu ailleurs ; aucun PASS moteur revendiqué. |

## Contrats et impacts
- Aucun type public Core, aucun schéma C00/C01, aucun registre/state et aucun store L10-A modifiés. Le store de cette tranche reste privé à l'adaptateur ; sa clé est distincte du marqueur L00-C `isrworldgen:l00c:marker:v1`.
- L'enveloppe conserve le `GeographyConfigHash` du profil Core mais ne prétend pas être le `frozenConfiguration` de `WorldManifest`. Le raccordement L10-A devra définir explicitement cette correspondance au lieu de réutiliser silencieusement ce blob.
- Impact packaging volontaire : l'ancien oracle `testsrc/WorldGen.Tests/L00A/Test-L00ALoad.ps1` exige exactement une DLL. L'intégrateur doit l'actualiser pour exiger exactement `ISRWorldGen.dll` et `ISRWorldGen.Core.dll`, sans accepter de DLL supplémentaire. Il n'a pas été modifié ici car hors périmètre.

## Risques et blocages
- `GameReady` est la première phase API auditée où les dimensions finales et la sauvegarde sont conjointement disponibles. L'IL de `ServerMain.Launch` place `GameReady` à `IL_057e`, le contrôle de sortie avant `WorldReady` à `IL_0593`, le déclenchement worldgen à `IL_05da` et `RunGame` à `IL_0617`; `ServerSystemSupplyChunks.InitWorldgenAndSpawnChunks` appelle `TriggerInitWorldGen` à `IL_0011`. Cela établit le chemin statique « avant génération de chunks », pas « avant création physique du fichier de sauvegarde » si ce libellé est interprété littéralement.
- `ISaveGame.StoreData` publie dans les données de sauvegarde en mémoire ; l'API publique n'expose ni flush, transaction ni CAS. La durabilité disque avant toute écriture de chunk ne peut donc pas être prouvée statiquement. La publication applicative, elle, attend bien StoreData + relecture mémoire stricte.
- Le callback moteur `TriggerInitWorldGen` journalise et absorbe les exceptions des mods en 1.22.7 ; l'arrêt sûr repose donc volontairement sur `IServerAPI.ShutDown()` (`AttemptShutdown("Shutdown through Server API", 7500)` dans l'IL audité), pas sur un throw.
- Les hauteurs observées dans l'interface cliente privée sont 128..512 par pas de 64 en normal et 128..2048 par pas de 64 en créatif ; ce ne sont pas des contraintes API officielles. La recette ne doit pas promouvoir cette observation en whitelist.
- Le ruleset est volontairement fail-closed sur l'assembly API 1.22.7.0. Toute autre version déclenche `native-profile.game-ready` puis Shutdown jusqu'à nouvel audit.

## Reprise exacte
- Le commit d'implémentation est propre et sans push ; cette passation est ajoutée dans un second commit.
- Aucun processus jeu/Visual Studio lancé, aucun verrou MCP acquis par ce sous-agent, aucune sauvegarde personnelle utilisée.
- Intégration : relire/cherry-pick les deux commits, corriger l'oracle packaging L00-A, relancer restore/format/build/tests, puis seulement avec le verrou moteur exécuter T02-05 sur des mondes uniques dans le data path isolé. Cas minimaux : laboratoire 4096×256×4096 explicitement sélectionné puis reload identique ; laboratoire demandé sur hauteur 320 ; dimensions 4096×256×8192 ; mutation/corruption sur une sauvegarde de laboratoire jetable. Exiger les logs `L02C_NATIVE_PROFILE_FROZEN` avant `L02C_NATIVE_GATE_FROZEN`, ou `L02C_NATIVE_PROFILE_REJECTED ...` suivi de l'arrêt sans gate.
