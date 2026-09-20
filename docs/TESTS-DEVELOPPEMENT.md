# Tests locaux et campagne de preuves L03-B

## Deux causes distinctes

Les quatre journaux fournis révèlent un lancement sans prérequis : trois tests
échouent avant le démarrage de `pwsh`, et le producteur de preuves refuse l'absence
de `ISR_L03B_EVIDENCE_COMMIT`. Ce ne sont pas des assertions géographiques échouées.

Le producteur `T0305AndT0306PublishAtomicBlindReviewEvidence` n'est pas un test
ordinaire « Run All ». Il appartient à une campagne Release sur un worktree propre,
détaché, avec commit/tree/blob et hashes des DLL vérifiés, nonce et publication
atomique. Ne pas renseigner un SHA fictif, désactiver la provenance ou l'ignorer
avec un attribut `[Ignore]`.

## Installer le prérequis réel

PowerShell 7 est distinct de Windows PowerShell 5.1. Depuis un terminal Windows :

```powershell
winget install --id Microsoft.PowerShell --source winget
```

Fermer et rouvrir Visual Studio après installation pour renouveler son PATH.
Vérifier `pwsh -NoProfile -Command '$PSVersionTable.PSVersion'`.
Une installation ZIP/hors PATH peut être désignée par un chemin absolu ; aucun
script de ce correctif n'installe de logiciel et aucun repli vers 5.1 n'est permis.

## Exécution locale de développement

Depuis la racine du dépôt :

```powershell
powershell -NoProfile -File .\tools\Invoke-DevelopmentTests.ps1 -PreflightOnly
powershell -NoProfile -File .\tools\Invoke-DevelopmentTests.ps1 -Configuration Debug
```

Le bootstrap reconnaît PowerShell 7 sur le PATH, dans son emplacement MSI habituel
ou comme outil .NET, puis le rend disponible aux processus enfants sans modifier
les variables utilisateur/machine. Un chemin personnalisé peut être fourni avec
`-PowerShellPath 'C:\outils\PowerShell\pwsh.exe'`. Un chemin explicite incorrect
est refusé, et n'est pas remplacé silencieusement.

Dans l'Explorateur de tests Visual Studio, sélectionner explicitement
`tests/Development.runsettings` via **Test > Configurer les paramètres d'exécution
> Sélectionner un fichier de paramètres** (libellé selon la langue de l'IDE).
Ce profil exclut exactement le producteur de campagne précité. Les trois tests
PowerShell et tous les autres tests restent sélectionnés. Rien n'est sélectionné
automatiquement à l'insu d'une campagne CI ou du lanceur certifié.

**Une réussite de ce profil n'est pas une qualification de T03-05/T03-06 : leur
publication et la revue humaine restent NOT_RUN dans cette exécution.** La
commande `dotnet test` sans ce profil conserve le comportement strict précédent.

## Rejouer seulement le protocole sans DLL du jeu

```powershell
pwsh -NoProfile -File .\tools\Invoke-DevelopmentTests.ps1 -Scope L03BProtocol -Configuration Release
```

Ce projet portable compile les sources L03B originales, référence le vrai Core et
emploie la même version MSTest que le projet principal. Il exécute les scripts
PowerShell réels sur leurs fixtures, pas une imitation Python. Il ne qualifie ni
l'adaptateur natif, ni l'API du jeu, ni la solution complète. Il n'est pas ajouté à
la solution de production. La matrice CI Windows/Linux Debug/Release vérifie les
TRX et exige que les trois tests signalés soient exécutés et réussis.

## Campagne certifiée : procédure distincte et inchangée

Le lanceur existant est :

```powershell
pwsh -NoProfile -File .\testsrc\WorldGen.Tests\L03B\Run-L03BEvidenceS.ps1 -PreflightOnly
# Seulement après validation du précontrôle et des règles de revue indépendantes :
pwsh -NoProfile -File .\testsrc\WorldGen.Tests\L03B\Run-L03BEvidenceS.ps1
```

Ces commandes sont à exécuter dans le worktree de preuve déjà préparé selon le
protocole L03-B, **pas dans la copie `main` utilisée pour développer**. Le lanceur
exige volontairement un HEAD détaché et entièrement propre, les références du
jeu, puis calcule les variables de provenance. Son précontrôle effectue aussi
restore/build sous verrou ; ce n'est pas une lecture sans effet. Ne pas détacher
ou nettoyer de force un travail en cours. Conserver les dossiers de preuves et
les contrôles d'accès/revue avant révélation déjà prescrits par le dépôt.

La campagne ne doit pas utiliser le projet portable. Les validations négatives CI
montrent seulement qu'un appel direct non configuré continue à échouer avant de
publier une preuve ; elles ne deviennent pas une nouvelle preuve T03-05/T03-06.

## Sources

- Microsoft : https://learn.microsoft.com/en-us/powershell/scripting/install/install-powershell-on-windows
- VSTest/runsettings : https://learn.microsoft.com/en-us/visualstudio/test/configure-unit-tests-by-using-a-dot-runsettings-file
- Contrat local : `testsrc/WorldGen.Tests/L03B/Run-L03BEvidenceS.ps1` et `EvidenceArtifactTests.cs`.
