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
- Le bootstrap serveur inscrit uniquement un callback `EnumServerRunPhase.GameReady`. Il commence par lire la sélection explicite `api.World.Config["isrworldgenProfileId"]` et la présence de l'unique enveloppe. Si les deux sont absentes, il court-circuite `Inactive` sans audit de version, capture de dimensions, écriture ni callback worldgen. Un monde L00 non marqué ne peut donc pas être arrêté par une incompatibilité du probe natif.
- La clé est déclarée par `worldconfig.json` à la racine du mod comme `String`, défaut vide, cachée de l'écran de personnalisation et modifiable uniquement pendant la création. Une clé absente, vide ou composée uniquement d'espaces est traitée comme non spécifiée. Cette déclaration est indispensable : en 1.22.7, `WorldConfig.loadWorldConfigValues` filtre `StartServerArgs.WorldConfiguration` sur les seuls `Mod.WorldConfig.WorldConfigAttributes`, puis `updateJWorldConfig` reconstruit l'objet à partir de ces déclarations ; sans elle, la clé du `serverconfig.json` disparaît avant `api.World.Config`.
- Si le monde participe, le callback lit ensuite `ICoreServerAPI.WorldManager.MapSizeX/Y/Z`, `ChunkSize`, `SaveGame.IsNew` et `SavegameIdentifier`. L'absence de sélection ne choisit jamais un profil ; seule une enveloppe préexistante `Committed` peut autoriser un reload sans nouvelle sélection.
- Les contraintes Core utilisent la seule hauteur réellement vérifiée par l'API, `SupportedHeights = [MapSizeY]`. Aucun catalogue public de hauteurs créables n'existe en 1.22.7 ; aucune whitelist d'interface privée n'est inventée. Les limites auditées sont `ChunkSize=32`, `MaxWorldSizeXZ=67_108_864`, `MaxWorldSizeY=16_384`. X/Z restent soumis au pas chunk et le coordinateur exige en plus l'égalité exacte X/Y/Z avec le profil avant et après persistance.
- Une nouvelle sélection valide est figée puis stockée sous l'unique clé privée `isrworldgen:l02c:frozen-profile:v1`. L'enveloppe bornée contient version, état authentifié `Pending`/`Committed`/`Rejected`, identifiant de sauvegarde, X/Y/Z, chunk, ruleset `vintagestory-1.22.7-effective-world-v1:1`, hash géographique, codec `isrworldgen.core.frozen-scale-profile:1`, blob `FrozenScaleProfileCodec` et checksum SHA-256. Toutes les frontières `byte[]` font des copies défensives.
- La transaction mono-clé écrit d'abord `Pending`, relit immédiatement `GetData`, exige l'égalité octet par octet, décode/recharge strictement puis recapture les contraintes. Le gate est inscrit seulement après ces preuves ; `Committed` est ensuite écrit et le profil publié sans autre opération susceptible de transformer ce commit en refus. Tout échec après la première écriture tente de remplacer la valeur par un tombstone `Rejected`. Si ce remplacement échoue, le `Pending` résiduel reste explicitement non activable au prochain démarrage.
- Au rechargement, seules les enveloppes `Committed` sont acceptées et aucune réécriture n'a lieu ; `Pending` et `Rejected` refusent systématiquement l'activation. Identité de sauvegarde, dimensions, chunk, ruleset, profil explicite éventuel, hash et blob sont revalidés strictement.
- Le callback `InitWorldGenerator` observe obligatoirement l'état `Frozen` avant le premier callback du mod et journalise `L02C_NATIVE_GATE_FROZEN`. Toute préparation rejetée journalise code, stage, détail borné/sanitisé, dimensions, chunk et ruleset, puis appelle `api.Server.ShutDown()` ; aucun throw n'est utilisé comme mécanisme d'arrêt.
- Le paquet Vintage Story embarque désormais exactement les DLL runtime `ISRWorldGen.dll` et `ISRWorldGen.Core.dll`, déclare Core dans `ISRWorldGen.deps.json`, et n'embarque pas `ISRWorldGen.Runtime.dll`.
- L'IL 1.22.7 place ce chemin dans `ServerSystemModHandler.OnLoadAssets` : `ModLoader.LoadMods` charge le `worldconfig.json`, puis `SaveGame.SetNewWorldConfig` applique `ServerConfig.WorldConfig.WorldConfiguration`, puis les phases du mod démarrent. À l'inverse, `ServerConfig.StartupCommands` n'est lu que par `ServerSystemLoadConfig.OnBeginRunGame`; `RunGame=8` vient après `GameReady=6`. Les commandes de démarrage ne peuvent donc pas servir à l'opt-in initial.

## Recette moteur jetable à exécuter par l'intégrateur
- Réserver un identifiant UTC inédit `${RUN_ID}` et créer uniquement sous `<repo>\.local\T02-05\${RUN_ID}\` les sous-répertoires `valid`, `height-refusal` et `rectangle-refusal`. Chaque sous-répertoire contient `.isrworldgen-lab`, `Saves\` et son propre `serverconfig.json`. Ne copier, ouvrir ou renommer aucune sauvegarde hors de cet arbre.
- Pour chaque cas, pointer temporairement le fichier local ignoré `<repo>\Directory.Build.local.props` vers le répertoire du cas :
```xml
<Project>
  <PropertyGroup>
    <VintageStoryPath>D:\Jeux\Vintagestory</VintageStoryPath>
    <ISRWorldGenDataPath>E:\Développement\Vintage Story\ISRWorldGen\.local\T02-05\${RUN_ID}\valid</ISRWorldGenDataPath>
  </PropertyGroup>
</Project>
```
- Chaque `serverconfig.json` est un fichier neuf. Le gabarit minimal commun ci-dessous doit rester lié à `127.0.0.1`, non annoncé et sans authentification distante. Remplacer seulement les jetons indiqués dans le tableau ; `worldWidth` et `worldLength` sont obligatoires, car les valeurs du playstyle seraient sinon susceptibles de remplacer les dimensions X/Z de tête lors de `SetNewWorldConfig`.
```json
{
  "ConfigVersion": "1.10",
  "ServerName": "ISRWorldGen T02-05 <case>",
  "Ip": "127.0.0.1",
  "Port": 45101,
  "Upnp": false,
  "AdvertiseServer": false,
  "VerifyPlayerAuth": false,
  "MapSizeX": 4096,
  "MapSizeY": 256,
  "MapSizeZ": 4096,
  "WorldConfig": {
    "SaveFileLocation": "E:\\Développement\\Vintage Story\\ISRWorldGen\\.local\\T02-05\\${RUN_ID}\\valid\\Saves\\valid.vcdbs",
    "WorldName": "ISRWorldGen T02-05 <case>",
    "AllowCreativeMode": true,
    "PlayStyle": "surviveandbuild",
    "PlayStyleLangCode": "preset-surviveandbuild",
    "WorldType": "standard",
    "WorldConfiguration": {
      "worldWidth": "4096",
      "worldLength": "4096",
      "isrworldgenProfileId": "laboratory"
    },
    "MapSizeY": 256
  },
  "StartupCommands": null
}
```

| Cas | Répertoire / sauvegarde | Port | X/Y/Z de tête et `WorldConfig.MapSizeY` | `worldWidth` / `worldLength` | Résultat requis |
|---|---|---:|---|---|---|
| valid, premier lancement | `valid\Saves\valid.vcdbs` | 45101 | 4096 / 256 / 4096 | `"4096"` / `"4096"` | `L02C_NATIVE_PROFILE_FROZEN ... profile=laboratory x=4096 y=256 z=4096 chunk=32`, puis `L02C_NATIVE_GATE_FROZEN profile=laboratory`; aucun rejet. |
| reload | exactement le même répertoire et fichier `valid.vcdbs`, après arrêt propre du premier lancement | 45101 | inchangés | inchangés | même identifiant `save` dans le log Frozen, profil rechargé, gate Frozen, aucun rejet et aucune réactivation rétroactive. |
| height-refusal | `height-refusal\Saves\height-refusal.vcdbs` | 45102 | 4096 / **320** / 4096, y compris `WorldConfig.MapSizeY=320` | `"4096"` / `"4096"` | `L02C_NATIVE_PROFILE_REJECTED code=InvalidInput stage=atlas.profile.native-height ... dimensions=4096x320x4096`, puis arrêt ; aucun `L02C_NATIVE_GATE_FROZEN`. |
| rectangle-refusal | `rectangle-refusal\Saves\rectangle-refusal.vcdbs` | 45103 | 4096 / 256 / **8192** | `"4096"` / `"8192"` | `L02C_NATIVE_PROFILE_REJECTED code=InvalidInput stage=native-profile.effective-dimensions ... dimensions=4096x256x8192`, puis arrêt ; aucun gate Frozen. |

- Ouvrir la solution correspondant au checkout intégré, choisir `ISRWorldGen Server (isolated data)` et lancer sous le débogueur. Pour le reload, arrêter proprement le serveur depuis sa console, conserver uniquement le répertoire `valid`, puis relancer le même profil. Conserver comme preuves les `Logs\server-main.log`, `Logs\server-debug.log` et `Logs\server-worldgen.log` de chaque cas, ainsi que le hash du package testé. Le moteur et Visual Studio n'ont pas été lancés dans ce sous-lot : cette recette reste `NOT_RUN` jusqu'à son exécution contrôlée.
- Après recette, supprimer uniquement l'arbre identifié `<repo>\.local\T02-05\${RUN_ID}` après avoir vérifié son chemin absolu et son marqueur `.isrworldgen-lab`. `Directory.Build.local.props` est un réglage local ignoré ; le remettre vers le data path isolé habituel. Les fichiers `.vcdbs`, `.vcdbs-shm` et `.vcdbs-wal` de cet arbre sont tous jetables.

## Tests et preuves
| Test | Commande ou capacité réellement utilisée | Statut | Rapport / preuve |
|---|---|---|---|
| Restore verrouillé | `dotnet restore ISRWorldGen.sln --locked-mode` avec `VINTAGE_STORY=D:\Jeux\Vintagestory` | PASS | 5 projets restaurés ; lock cohérent après mise à jour mécanique. |
| Format | `dotnet format ISRWorldGen.sln --no-restore --verify-no-changes --verbosity minimal` | PASS | Exit 0. |
| Build Debug / Release | `dotnet build ISRWorldGen.sln --no-restore -c Debug`, puis Release | PASS | Deux configurations : 0 avertissement, 0 erreur. |
| L02CNative ciblé | `dotnet test ... -c Debug/Release --filter FullyQualifiedName~L02CNative` | PASS | 29/29 dans chaque configuration : schéma déclaré, opt-in absent/vide sans capture stricte, injection `laboratory`, valid, reload, enveloppe/bloc interne corrompus, états Pending/Committed/Rejected, reprise après échecs post-write/tombstone, mutation sélection/monde, rectangle, hauteur, diagnostic borné, refus et copies défensives. |
| Régression .NET | Build Debug et Release préalable, puis `dotnet test ... --no-build --no-restore -c Debug/Release` | PASS | 186/186 dans chaque configuration. Dans le compte sandbox, le premier passage a seulement rencontré le garde Git `safe.directory`; le passage avec `GIT_CONFIG_COUNT=1`, `GIT_CONFIG_KEY_0=safe.directory` et la valeur limitée à ce worktree est vert dans les deux configurations. |
| API / IL / package / non-activation L00 | `Invoke-L02CNativeStaticChecks.ps1 -Configuration Debug/Release -VintageStoryPath D:\Jeux\Vintagestory` | PASS statique Debug + Release | Version/hashes DLL ci-dessus, membres XML exacts, IL de découverte/filtrage/publication et ordre des phases, désérialisation 1.22.7 du schéma et de `ServerConfig.WorldConfig.WorldConfiguration`, deux DLL runtime exactes, `worldconfig.json` racine exact, `ISRWorldGen.Runtime.dll` absent, aucune sélection dans `launchSettings.json`. |
| Oracle refus GameReady | `NativeProfileBridgeTests.ExplicitRefusalAtGameReady_ShutsDownWithoutEnvelopeOrFrozenGate` | PASS unitaire | `ShutdownCount=1`, `WriteCount=0`, aucune inscription worldgen, gate non-Frozen. |
| Oracle opt-in | `NativeProfileBridgeTests.NoSelectionAndNoEnvelope_NeverRunsStrictCaptureOrChangesL00Behavior` | PASS unitaire | `CaptureWorld` configuré pour lever ; `CaptureCount=0`, aucun Shutdown/store/gate. |
| Oracle reprise fail-closed | Tests `PostWriteRereadMismatch`, `TombstoneWriteFailure`, `NativeMutationAfterStoredReread`, `GateRegistrationFailure`, `CommitWriteFailure` | PASS unitaire | Toute reprise voit `Rejected` ou, si le tombstone ne peut être écrit, `Pending`; aucun état n'active le profil. |
| T02-05 Vintage Story réel | Visual Studio + serveur/client 1.22.7 + mondes de labo uniques | NOT_RUN / BLOCKED | Interdit dans ce sous-lot pendant le verrou détenu ailleurs ; aucun PASS moteur revendiqué. |

## Contrats et impacts
- Aucun type public Core, aucun schéma C00/C01, aucun registre/state et aucun store L10-A modifiés. Le store de cette tranche reste privé à l'adaptateur ; sa clé est distincte du marqueur L00-C `isrworldgen:l00c:marker:v1`.
- L'enveloppe conserve le `GeographyConfigHash` du profil Core mais ne prétend pas être le `frozenConfiguration` de `WorldManifest`. Le raccordement L10-A devra définir explicitement cette correspondance au lieu de réutiliser silencieusement ce blob.
- Impact packaging volontaire : l'ancien oracle `testsrc/WorldGen.Tests/L00A/Test-L00ALoad.ps1` exige exactement une DLL. L'intégrateur doit l'actualiser pour exiger exactement `ISRWorldGen.dll` et `ISRWorldGen.Core.dll`, sans accepter de DLL supplémentaire. Il n'a pas été modifié ici car hors périmètre.

## Risques et blocages
- `GameReady` est la première phase API auditée où les dimensions finales et la sauvegarde sont conjointement disponibles. L'IL de `ServerMain.Launch` place `GameReady` à `IL_057e`, le contrôle de sortie avant `WorldReady` à `IL_0593`, le déclenchement worldgen à `IL_05da` et `RunGame` à `IL_0617`; `ServerSystemSupplyChunks.InitWorldgenAndSpawnChunks` appelle `TriggerInitWorldGen` à `IL_0011`. Cela établit le chemin statique « avant génération de chunks », pas « avant création physique du fichier de sauvegarde » si ce libellé est interprété littéralement.
- Une simple entrée arbitraire dans `serverconfig.json` n'est pas une voie générique vers `api.World.Config` : seules les clés déclarées par un `worldconfig.json` de mod survivent. Le correctif rend l'opt-in initial praticable ; les `StartupCommands` restent statutairement trop tardives.
- `ISaveGame.StoreData` publie dans les données de sauvegarde en mémoire ; l'API publique n'expose ni suppression, flush, transaction ni CAS. La durabilité disque avant toute écriture de chunk ne peut donc pas être prouvée statiquement. Le protocole laisse volontairement un tombstone `Rejected` ou un `Pending` non activable au lieu de prétendre annuler physiquement l'écriture.
- Le callback moteur `TriggerInitWorldGen` journalise et absorbe les exceptions des mods en 1.22.7 ; l'arrêt sûr repose donc volontairement sur `IServerAPI.ShutDown()` (`AttemptShutdown("Shutdown through Server API", 7500)` dans l'IL audité), pas sur un throw.
- Les hauteurs observées dans l'interface cliente privée sont 128..512 par pas de 64 en normal et 128..2048 par pas de 64 en créatif ; ce ne sont pas des contraintes API officielles. La recette ne doit pas promouvoir cette observation en whitelist.
- Le ruleset est volontairement fail-closed sur l'assembly API 1.22.7.0. Toute autre version déclenche `native-profile.game-ready` puis Shutdown jusqu'à nouvel audit.

## Reprise exacte
- Les commits d'implémentation et de correction reviewer sont propres et sans push ; le SHA correctif est communiqué séparément pour éviter une référence auto-récursive dans ce fichier.
- Aucun processus jeu/Visual Studio lancé, aucun verrou MCP acquis par ce sous-agent, aucune sauvegarde personnelle utilisée.
- Intégration : relire/cherry-pick les deux commits, corriger l'oracle packaging L00-A, relancer restore/format/build/tests, puis seulement avec le verrou moteur exécuter T02-05 sur des mondes uniques dans le data path isolé. Cas minimaux : laboratoire 4096×256×4096 explicitement sélectionné puis reload identique ; laboratoire demandé sur hauteur 320 ; dimensions 4096×256×8192 ; mutation/corruption sur une sauvegarde de laboratoire jetable. Exiger les logs `L02C_NATIVE_PROFILE_FROZEN` avant `L02C_NATIVE_GATE_FROZEN`, ou `L02C_NATIVE_PROFILE_REJECTED ...` suivi de l'arrêt sans gate.
