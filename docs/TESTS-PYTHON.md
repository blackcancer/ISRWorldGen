# Python requis pour les tests de preparation L05-D

## Diagnostic

La compilation du mod reussit. Les deux tests `T05U_01_*` executent
`tools/prepare_plan_update.py`, qui demande Python **3.10+**, bibliotheque
standard uniquement. Un alias Microsoft Store `python.exe` peut exister sans
interpreteur utilisable et retourner 9009. Les erreurs de schema attendues ne
peuvent alors pas etre produites : le script n'a pas demarre.

## Correction du lanceur

`Invoke-DevelopmentTests.ps1` verifie maintenant un vrai processus Python pour
les scopes `Development` et `L05DProtocol`, avant `dotnet test` et sa compilation.
La detection essaie les applications `python`, `python3`, puis `py -3`, rejette
les commandes qui ne retournent pas le JSON attendu, et exige Python 3.10+.
Le `sys.executable` retourne est confirme directement, puis passe aux tests via
la variable de processus `ISR_TEST_PYTHON`. Un parametre `-PythonPath` explicite
prime sur cette variable; une valeur explicite incorrecte ne declenche aucun
repli. Aucune installation ni variable utilisateur/machine n'est modifiee.
Les options d'installation automatique des lanceurs Python sont desactivees
pour les processus de verification et de test. Le scope `L03BProtocol`, qui
n'execute pas Python, ne recoit pas de nouvelle dependance.

```powershell
git pull --ff-only origin main
powershell -NoProfile -File .\tools\Invoke-DevelopmentTests.ps1 -PreflightOnly
powershell -NoProfile -File .\tools\Invoke-DevelopmentTests.ps1 -Configuration Debug
```

Pour un Python deja installe mais hors PATH, donner son executable reel :

```powershell
powershell -NoProfile -File .\tools\Invoke-DevelopmentTests.ps1 `
    -Configuration Debug -PythonPath 'C:\chemin\reel\python.exe'
```

Le chemin ci-dessus est un exemple, pas une installation supposee. Si aucun
interpreteur n'est installe, installer Python depuis python.org ou le gestionnaire
habituel, puis relancer le precontrole. Le correctif ne remplace pas cette
installation. Le diagnostic `PYTHON_PREREQUISITE_MISSING` bloque avant les tests;
il n'est ni PASS, ni un test ignore.

## Visual Studio et preuve ciblee

Les tests L05-D lisent `ISR_TEST_PYTHON` quand elle existe. Pour les lancer dans
l'Explorateur de tests avec un Python hors PATH, demarrer Visual Studio depuis
un environnement qui contient cette variable, ou employer le lanceur ci-dessus.
Sans variable, le comportement `python` historique reste disponible, avec un
diagnostic explicite pour 9009. Ne pas renseigner des variables de provenance
L03-B pour resoudre un probleme d'interpreteur.

```powershell
powershell -NoProfile -File .\tools\Invoke-DevelopmentTests.ps1 `
    -Scope L05DProtocol -Configuration Release -PythonPath 'C:\chemin\reel\python.exe'
```

Le projet portable lie les quatre tests L05-D originaux et execute le vrai outil
Python sur des fixtures jetables. Il ne modifie pas le registre actif, n'exclut
aucun test, ne remplace pas la compilation complete avec le jeu, et ne qualifie
pas la campagne de revue aveugle T03-05/T03-06. Le profil Development.runsettings
reste strictement inchange.

## Regression

La CI Windows/Linux Debug/Release utilise un executable de test qui retourne
9009, place devant Python sur PATH. Elle verifie son rejet, le refus d'un PATH
sans interpreteur, celui d'un chemin explicite invalide ou non-Python, et le
passage d'un executable hors PATH dont le chemin contient espace et accent.
Les quatre tests originaux doivent etre Passed, aucun ne doit etre ignore.
Le bootstrap Windows PowerShell 5.1 vers PowerShell 7 conserve PythonPath.
Les statuts effectivement obtenus sont dans les TRX et result.json du run CI.

Sources : documentation du projet `tools/prepare_plan_update.py`, Python
https://docs.python.org/3/using/windows.html et Microsoft
https://learn.microsoft.com/en-us/windows/python/beginners .
