# ADR-001 — Cible locale et structure du bootstrap
Statut : ACCEPTED. Date : 6 septembre 2026 ; auteur et intégrateur : Codex ; tâche source : préparation de L00-A.

## Contexte vérifié
Le client et le serveur installés dans `D:\Jeux\Vintagestory` déclarent la version produit 1.22.7 et chargent `Microsoft.NETCore.App` 10.0. Les assemblages utilisés par le probe sont ceux de cette installation. Visual Studio Community 2026 18.9.2 et le SDK .NET 10.0.400 sont installés.

Le template officiel Anego Studios `VintageStory.Mod.Templates` 1.0.10 est installé localement. Il cible encore `net7.0`. Sa compilation non modifiée contre le `VintagestoryAPI.dll` local échoue avec `CS1705`, car l’assemblage API 1.22.7 dépend de `System.Runtime` 10.0. Aucun code de génération n’était présent dans le dépôt au checkpoint `0527b054e169382f71fa1cd04e393f23d698efed`.

## Décision
La solution se nomme `ISRWorldGen.sln`, le nom public du mod est `ISRWorldGen` et son identifiant est `isrworldgen`. Le premier projet est `src/WorldGen.VintageStory/WorldGen.VintageStory.csproj`, avec assembly `ISRWorldGen`, cible `net10.0` et point d’entrée minimal issu du cycle de vie du template installé.

La référence runtime initiale est limitée à `VintagestoryAPI.dll`. Aucune référence Survival, Essentials, Harmony ou bibliothèque supplémentaire n’est ajoutée sans besoin démontré. Le chemin local du jeu et le datapath de laboratoire sont fournis par `Directory.Build.local.props`, ignoré par Git ; un exemple sans chemin utilisateur est versionné.

Le lancement client/serveur reprend les profils du template, avec `--dataPath` vers `.local/VintagestoryData` afin de ne pas utiliser les sauvegardes personnelles. Les sorties de build restent dans le répertoire `bin` du projet et sont ignorées.

## Conséquences
Cette décision prépare R00-01 et R00-02, C06, C07, L00-A et T00-01/T00-02. Elle ne valide ni le chargement réel du mod, ni le débogage MCP, ni les hooks de génération. Les futurs projets cœur/runtime/outillage seront ajoutés à la solution par l’intégrateur lorsque leur première tâche deviendra admissible.

La cible `net10.0` lie cette phase à la famille locale Vintage Story 1.22.7. Une mise à jour du jeu ou du template impose de refaire le probe de versions, la compilation et les tests en jeu. Aucun effet de sauvegarde ou de seed n’existe à ce stade, car le bootstrap ne génère ni ne persiste de monde.

## Validation et retour arrière
Validation minimale : build Debug et Release sans avertissement, sortie sans DLL commerciale copiée, solution ouverte et construite par Visual Studio/MCP, puis chargement réel isolé dans L00-A. Les preuves en jeu restent `NOT_RUN` tant qu’elles ne sont pas exécutées.

Le retour arrière consiste à revenir au checkpoint documentaire public `0527b054e169382f71fa1cd04e393f23d698efed`. Le datapath `.local` est isolé et n’est jamais utilisé comme cible d’une suppression automatique ; les sauvegardes personnelles restent hors périmètre.
