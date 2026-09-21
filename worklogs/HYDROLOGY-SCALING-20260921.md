# L05-A/L05-B — drainage et accumulation extensibles

Base : `69d42c7ad1aea6708af8400025b1e59363fdbb60`, branche `main`.
Mandat utilisateur : poursuite autonome sur main. Maintenance du coeur hydrologique
existant ; aucun changement du DAG, des contrats, des versions, des sauvegardes ou
des états de lots. Les prérequis natifs de L11-A/L18-A restent non satisfaits.

## Changements métier

- Adjacence : recherche binaire dans le tableau canonique déjà possédé par chaque
  DrainageCell, sans dupliquer son stockage. La vue publique reste en lecture seule.
- Composantes sans exutoire : un tri (altitude, ID) et un curseur monotone remplacent
  la recherche globale répétée. La règle de sélection du terminal sec ne change pas.
- Dépressions et plateaux : une traversée canonique remplace les Min répétés des
  ensembles ; une appartenance indexée remplace les scans de la liste du bassin.
- Accumulation : les tableaux de budgets et de routage sont appariés par index
  seulement après preuve de l'égalité exacte de leurs IDs triés. L'existence des
  receveurs est vérifiée dans un ensemble d'IDs construit une seule fois.

Aucune modification des priorités Priority-Flood, des exutoires, de la hiérarchie,
du relief physique, des sommes flottantes, de la classification ou des transferts.
Les index ne commandent pas l'ordre des réductions. Aucun appel Vintage Story ni
nouvelle dépendance ; les types et signatures publics sont conservés.

## Tests

11 méthodes dans `DrainageScalingTests` : composantes disjointes, degré élevé et
IDs extrêmes, propriété/read-only des voisins, multiples cuvettes, plateau large,
minima alternés, rivière de 16 384 cellules, erreurs d'association des budgets,
permutations et calculs concurrents, oracle indépendant et comparaison mesurée.
L'oracle de minimax utilise la relaxation exhaustive, pas Priority-Flood, sur les
64 graphes non orientés de quatre sommets avec trois politiques de terminaux.

Le témoin compare 18 graphes : six formes, tailles 256/1 024/4 096. Les payloads
complets (topologie et débit) sont sérialisés et hachés. Les médianes de trois
mesures sont enregistrées sans seuil de temps fragile. Ces graphes sont abstraits :
leurs IDs ne sont pas une rasterisation X/Z et ne requièrent pas de carte spatiale.
La recette climatique existante continue de produire et vérifier ses cartes.

Le workflow existant Windows/Linux x Debug/Release conserve toute la régression
L04/L05 et ajoute la comparaison avec les deux sources métier de la base épinglée.
Un échec de build, un test ignoré ou un hash différent est un échec de recette.
Le script est réservé à une copie CI explicitement jetable, refuse les fichiers
suivis modifiés et les sorties préexistantes, et restaure les sources en finally.
Une interruption brutale du job abandonne seulement ce checkout éphémère ; il ne
faut pas l'utiliser dans le dépôt de développement ou sur des données de jeu.

## État à la soumission

CI : à exécuter et vérifier, aucun PASS anticipé. Le conteneur de travail n'a ni
SDK .NET, ni résolution réseau Git, ni MCP Visual Studio accessible ; les lectures
et écritures passent par le connecteur GitHub, les tests C# par Actions.
Revue indépendante : NOT_RUN, aucune identité de relecteur inventée. Pas de
validation globale de lot. Performances en jeu, solution avec DLL natives et
recette T00-06 : NOT_RUN pour ce changement.

Commande locale ordinaire, sans remplacement de sources :

```powershell
dotnet test .\testsrc\WorldGen.ClimateHydrology.Tests\WorldGen.ClimateHydrology.Tests.csproj -c Release
```

Ne pas lancer `tools/test_drainage_scaling.py --disposable-checkout` dans la copie
utilisateur : ce drapeau est destiné uniquement au runner CI jetable.
