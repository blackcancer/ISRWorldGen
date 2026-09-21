# L04-B/L04-C — association climatique et accès aux bilans hydriques

Base lue : `de4a0db6b2912f0bf95a8b28f3a7035985a1ccc4`, branche `main`.
Mandat : poursuite sur main, après le compte rendu utilisateur de 346/346 tests de développement Debug, zéro échec/ignoré, 62,6 s de tests. Ce compte rendu est fourni dans la conversation ; son TRX et son commit exact n'ont pas été fournis ni certifiés ici.

## Périmètre et dépendances

Maintenance des composants L04-B/L04-C déjà implémentés et enregistrés DONE, pas démarrage artificiel de L18-A/L06-A/L16-B. L18-A attend L11-A ; L11-A attend la recette native L00-C. L00-C/T00-06 client et les contrôles natifs de L04-A ne sont pas clôturés par une réussite de tests de développement.

Références lues : AGENTS, socle, S04, C02, état et fiches L04-A/L18-A/L16-B/L19-D pour vérifier les dépendances. Aucun changement de contrat, de configuration, de génération des blocs, de version publique, de registre ou de sauvegarde. Les changements de code sont limités à la production climatique et ses tests. Le projet de recette portable reprend MSTest 4.0.2, déjà utilisé, et le vrai Core net10.0 ; aucun appel Vintage Story, DLL du jeu ou bibliothèque supplémentaire n'est introduit.

## Défauts et corrections

1. `WaterBudgetSolver.Solve` acceptait un `TryGetField` réussi portant un `CellId` différent de celui demandé. Un bilan numériquement équilibré pouvait ainsi utiliser la pluie d'une autre cellule. La frontière vérifie maintenant l'identité et les domaines finis/non négatifs des deux champs amont. Un champ absent n'est pas remplacé par de la sécheresse.
2. `PrecipitationSnapshot.TryGetField` parcourait la totalité de la collection pour chaque cellule : n recherches coûtaient quadratiquement. Il utilise maintenant `Array.BinarySearch` sur le tableau canonique déjà possédé par le snapshot, exposé uniquement par sa vue en lecture seule. Pas de deuxième copie/index par cellule ; comparateur `long.CompareTo`, sans soustraction susceptible de déborder.
3. La validation des ports atmosphériques et des réservoirs sortants refaisait des scans. Des index locaux à la passe les remplacent. Aucun ordre de réduction flottante n'a été changé : transferts triés, groupes et sommes restent canoniques. Les index ne sont jamais énumérés pour produire des résultats numériques.

Le profil d'algorithme et les unités restent identiques : aucun calcul des entrées conformes n'est modifié. Le témoin numérique avant/après doit confirmer les mêmes octets pour pluie, cellules de bilan et transferts. Les anciennes entrées incohérentes sont refusées ; aucune migration silencieuse des mondes n'est exécutée.

## Recette ajoutée

13 méthodes nouvelles : identité de cellule, humidité atmosphérique invalide, pluie invalide/absente, pluie nulle et ID zéro, arrêt avant lecture de transferts sur données invalides, 2 048 réservoirs, refus de débit supérieur à la recharge locale, témoin couplé, IDs Int64 extrêmes, snapshot unitaire, 8 192 lectures parallèles, 1 024 ports et refus des ports invalides. Elles sont automatiquement incluses dans WorldGen.Tests, sans changement au filtre Development.runsettings.

Le projet portable lie aussi les tests originaux L04A/B/C et L05A/B/C. Le workflow doit compiler l'ancien code puis exécuter le nouveau contre-exemple pour obtenir précisément `MISATTRIBUTED_PRECIPITATION_ACCEPTED`; un échec de compilation ne vaut pas preuve. Il compare ensuite un fingerprint de pipeline valide avant/après et exige la réussite de toutes les méthodes, sans skip. Les champs du témoin possèdent JSON, cartes SVG à palette fixe et manifeste. Les cartes de température déjà présentes restent exécutées.

Commande locale non destructive (SDK du dépôt, aucun jeu lancé) :

```powershell
dotnet test .\testsrc\WorldGen.ClimateHydrology.Tests\WorldGen.ClimateHydrology.Tests.csproj -c Debug
```

Pour l'ensemble réel avec les références du jeu, conserver le lanceur existant :

```powershell
powershell -NoProfile -File .\tools\Invoke-DevelopmentTests.ps1 -Configuration Debug
```

Le script Python de comparaison avant/après est réservé au checkout CI jetable : pas d'usage dans la copie utilisateur. Il vérifie l'absence de modifications suivies, refuse un dossier de preuve existant et restaure les deux sources octet pour octet en finally. Les jobs sont bornés et conservent leurs résultats même en échec. Aucun accès réseau d'authentification au jeu ni suppression de données utilisateur.

## État des preuves et limites

À l'écriture : CI à exécuter et consulter, pas de résultat présumé. L'environnement d'édition n'expose ni SDK local ni MCP Visual Studio ; il n'y a pas de revue indépendante disponible. Les preuves exécutées sont les checks/artefacts attachés au commit, pas une affirmation anticipée ici. Aucune promotion de tâche à DONE et aucune prétention de performance en jeu. L'amélioration de complexité découle des accès remplacés ; aucun nombre de millisecondes ou FPS n'est garanti.

Prochaine recette après revue : relancer la suite complète locale sur ce commit et conserver son TRX ; la recette native L00-C reste une exigence distincte.
