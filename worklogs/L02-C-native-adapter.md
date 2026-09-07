# Passation — L02-C / T02-05 adaptateur natif
**Statut proposé : REVIEW / BLOCKED pour la recette moteur.** Ne pas déclarer DONE avant T02-05 dans Vintage Story réel.

> Historique préservé : la recette ci-dessous décrit le candidat initial. L'addendum final la complète et remplace explicitement son ancien oracle reload, sans réécrire les constats antérieurs.

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

## Addendum — instrumentation de preuve runtime (base 71ab379)

### Identité et portée

- Correctif préparé par `audit_l02c_native` sur `agent/l02c-runtime-evidence`, worktree `E:\Développement\Vintage Story\ISRWorldGen-worktrees\l02c-runtime-evidence`, base exacte `71ab379534da18f1e2c6903f899fe3527a26e092`.
- C00/C01 et L10-A restent inchangés ; aucun registre/state, csproj ou lock modifié. Aucun Visual Studio, jeu ou sauvegarde n'a été ouvert pendant ce correctif.
- La campagne antérieure sur `71ab379` a motivé l'instrumentation, mais ne qualifie pas les nouveaux binaires. Le runtime du candidat instrumenté reste `NOT_RUN` jusqu'à revue, intégration puis nouvelle campagne.

### Preuve produite par le chemin réel

- `NativeProfileCoordinator` attache à chaque préparation une preuve immuable : `source=new|reload`, `persistencewrites`, `envelopebytes`, `envelopesha256` et `gatecallbackregistered`. Les longueur/hash viennent des octets réellement encodés ou lus ; aucune constante de test ne les alimente.
- `persistencewrites` compte les appels `IFrozenProfileStore.Write` émis, incrémentés avant l'appel. Une tentative qui lève est donc comptée. Un tombstone relu fournit son vrai hash ; si son readback est inconnu, la preuve reste `envelopebytes=0 envelopesha256=none`.
- Le bridge ajoute l'observation mémoire réelle `gatestate`, `gatecangenerate` et `publishedprofile`. Sur new valide : deux écritures Pending+Committed et callback gate inscrit. Sur reload : octets Committed lus, zéro écriture et aucun nouveau callback gate.
- Les logs Frozen/Rejected n'exposent plus `SavegameIdentifier`, chemin ou token. Les messages d'exception ne sont pas recopiés ; les détails path-like/sensibles sont remplacés et bornés.

### Correction normative de l'oracle reload

La ligne reload historique ci-dessus qui exigeait un `L02C_NATIVE_GATE_FROZEN` est obsolète. Au reload, T02-05 exige à `GameReady`, avant `WorldReady` :

- `source=reload persistencewrites=0 gatestate=Frozen gatecangenerate=true gatecallbackregistered=false publishedprofile=true` ;
- mêmes `envelopebytes` et `envelopesha256` que new ;
- profil publié identique, aucune réécriture/mutation et aucun rejet.

`PrepareExistingWorld` ne reçoit pas le callback `beforeCommit` réservé au nouveau monde ; l'absence de `L02C_NATIVE_GATE_FROZEN` au reload est donc attendue. New doit toujours inscrire ce callback et son marqueur doit précéder le premier `L00B_COLUMN_CALLBACK`.

### Oracle reproductible avant/après F5

Le script `testsrc/WorldGen.Tests/L02CNative/Invoke-L02CNativeRuntimeEvidence.ps1` ne lance ni Visual Studio ni le jeu.

1. `-Phase Snapshot`, après build intégré mais avant F5, vérifie HEAD et le serveur 1.22.7 (ProductVersion `1.22.7`, SHA-256 `3AD6294240B9B55D3E0DB3CD323D90C31EC8474EAE6E4E16B58FE76507CB9D0D`). Il copie avec `FileMode.CreateNew` exactement `ISRWorldGen.dll`, `ISRWorldGen.Core.dll` et leurs deux PDB, puis crée le manifeste sans remplacement.
2. `-Phase Validate` revalide commit/serveur/package/snapshot, parse les quatre logs, compare new↔reload, vérifie l'ordre `GameReady < Frozen < WorldReady`, gate/colonne pour new, refus/Shutdown, puis crée `runtime-evidence.json` avec `FileMode.CreateNew`.
3. Le JSON de campagne fournit PID, breakpoint et callstack. Le script corrèle le PID avec `L00B_DEBUG_PROBE_READY`, mais restitue toujours `Provenance=campaign-supplied-unverified-by-oracle` : il n'invente aucune provenance Visual Studio.

Avant la prochaine campagne :

```powershell
$oracle = '<repo>\testsrc\WorldGen.Tests\L02CNative\Invoke-L02CNativeRuntimeEvidence.ps1'
& $oracle -Phase Snapshot -EvidenceRoot '<repo>\.local\T02-05\<RUN_ID>\evidence' `
  -RepositoryRoot '<repo>' -VintageStoryPath 'D:\Jeux\Vintagestory' -Configuration Debug `
  -ExpectedCommit '<SHA40-intégré>'
```

La validation reçoit ensuite quatre `server-main.log` distincts et un JSON `cases.new|reload|height|rectangle`, chaque cas portant `pid`, `breakpoint`, `callstack[]` et éventuellement `debuggerClaim`. L'opérateur reste responsable de la provenance Visual Studio.

### État des preuves de l'addendum

| Preuve | Statut | Résultat |
|---|---|---|
| Témoin tests-first | PASS comme témoin | Les tests ne compilaient pas avant l'ajout de la preuve structurée et des signatures host. |
| L02CNative Debug/Release | PASS | 38/38 dans chaque configuration : new/reload/refus, post-write/tombstone, bornes, hash, copies et diagnostics non sensibles. |
| Auto-test oracle Debug/Release | PASS | Snapshot CreateNew, hash new/reload, mutation rejetée, remplacement rejeté, provenance non inventée. |
| API/IL/package Debug/Release | PASS statique | API 1.22.7, injection GameReady, deux DLL runtime, profils L00 non activés, moteur `NOT_RUN`. |
| Build Debug/Release | PASS | 0 avertissement, 0 erreur dans chaque configuration. |
| Régression .NET Debug/Release | PASS | 195/195 dans chaque configuration après construction préalable des deux variantes de `WorldGen.Tools` exigée par le harnais L01-C. |
| Format solution | PASS | `dotnet format ISRWorldGen.sln --no-restore --verify-no-changes --verbosity minimal`, exit 0. |
| T02-05 candidat instrumenté | NOT_RUN | Revue du commit requise avant reprise du verrou Visual Studio/MCP/jeu. |

`ISaveGame.StoreData` n'expose toujours ni flush, suppression, transaction ni CAS. Le compteur prouve les appels émis par le coordinateur, pas leur durabilité disque ; la campagne doit encore confirmer arrêt/sauvegarde close et enveloppe SQLite avant reload.

## Erratum re-review — identité binaire et provenance Visual Studio fermées

Cet erratum complète l'addendum sans effacer l'historique. La phrase précédente autorisant un `debuggerClaim` optionnel et qualifiant la provenance de `campaign-supplied-unverified-by-oracle` est désormais obsolète : un tel dossier ne peut plus produire `PASS`.

### Snapshot v2 avant lancement

`Invoke-L02CNativeRuntimeEvidence.ps1 -Phase Snapshot` crée toujours les fichiers avec `FileMode.CreateNew`, et scelle maintenant :

- `Commit` et `ExpectedAssemblyInformationalVersion=1.0.0+<HEAD>` ; pour les deux DLL, `FileVersionInfo.ProductVersion` et l'attribut PE `AssemblyInformationalVersionAttribute` doivent être égaux à cette valeur ;
- SHA-256, taille, versions et empreinte normalisée du chemin package de chaque artefact ;
- pour chaque paire DLL/PDB, nom PDB CodeView, GUID, age et stamp PE, puis GUID/stamp du content ID du Portable PDB. La paire est rejetée si ces identités ne correspondent pas ; la simple coexistence des fichiers ne suffit plus ;
- l'identité inchangée du serveur 1.22.7 déjà auditée.

La validation recontrôle d'abord les deux paires dans le snapshot et dans le package courant. Chaque log doit ensuite contenir exactement un `L00A_BOOTSTRAP` Server dont PID, nom `ISRWorldGen.dll` et SHA-256 correspondent à la session et au snapshot. Le module observé au breakpoint doit fournir l'empreinte du chemin exact, ProductVersion, SHA DLL/PDB et identité CodeView attendus.

### JSON durable de campagne v2

Le fichier de campagne a un schéma fermé, sans note libre :

- racine exacte : `Schema`, `TestedCommit`, `SnapshotManifestSha256`, `VisualStudioProfile`, `DebuggerTransport`, `Provenance`, `Cases` ;
- valeurs imposées : `Schema=isrworldgen.t02-05.visual-studio-campaign.v2`, profil `ISRWorldGen Server (isolated data)`, transport `visual-studio-debugger`, provenance `visual-studio-debugger-session-verified` ;
- quatre cas exacts `new|reload|height|rectangle`, chacun avec `SessionId`, `ServerPid`, `StartedUtc`, `BreakpointUtc`, `CompletedUtc`, `LogSha256`, `BreakpointId`, `CallstackFrames`, `CallstackSha256`, `Module` ;
- `Module` a exactement `FileName`, `PathSha256`, `Sha256`, `ProductVersion`, `PdbFileName`, `PdbSha256`, `CodeViewGuid`, `CodeViewAge`, `CodeViewStamp` ; toutes ces valeurs doivent correspondre au snapshot ;
- breakpoint et callstack utilisent uniquement les enums fermés du script : chemin Frozen à `GameReady` pour new/reload, chemin `Shutdown` après rejet pour height/rectangle. Leur empreinte est recalculée, et l'heure du breakpoint doit être corrélée au marqueur runtime horodaté du même log ;
- le rapport ne recopie ni frames, chemin, texte opérateur, token, save path ni détails libres : uniquement enums validés, PID, timestamps canoniques, identifiants bornés, empreintes et booléens dérivés.

Provenance absente/non vérifiée, champ supplémentaire, frame arbitraire, log périmé ou muté, mismatch bootstrap/hash/version/chemin, paire PDB invalide et callback colonne absent sont tous des refus explicites de l'oracle.

### Oracle runtime corrigé

- `new` exige l'ordre réel `GameReady < Frozen < WorldReady < GATE_FROZEN < premier L00B_COLUMN_CALLBACK < RunGame`; il exige aussi `L00C_INACTIVE` entre `WorldReady` et `RunGame`, puis témoin L00-C, armement/feu de l'arrêt différé, Shutdown API, sauvegarde et fermeture. L'ordre relatif entre les deux callbacks d'initialisation L00-C/L02-C n'est pas surcontraint ; plusieurs callbacks colonne restent permis.
- `reload` n'exige toujours pas le callback de coordinateur absent par conception et ne présume pas si un callback colonne hors scénario apparaît. Il exige cependant `Frozen`, `PublishedProfile`, `Gate.CanGenerate`, même enveloppe/hash, zéro écriture avant `WorldReady`, puis `L00C_INACTIVE`, un cycle `RunGame` complet et un arrêt/sauvegarde doux.
- `height` et `rectangle` exigent le rejet structuré et le Shutdown API sans `WorldReady`, `RunGame`, gate, callback colonne ISR ni sauvegarde géographique publiée.

Ces contrôles qualifient le harnais statique. Ils ne remplacent pas la campagne Visual Studio : le candidat corrigé demeure `NOT_RUN` tant que les quatre sessions réelles n'ont pas fourni ce dossier v2.

Validation hors moteur de cet erratum : oracle et ses 13 négatifs `PASS` en Debug/Release ; contrôle API/IL/package `PASS` en Debug/Release ; 38/38 tests L02CNative et 195/195 tests globaux `PASS` dans les deux configurations ; build 0 avertissement/0 erreur et format solution `PASS`. La ProductVersion doit être reconstruite et ces contrôles répétés après le commit final, puisque le SHA informatif change avec HEAD.
