# Procédure Visual Studio Community 2026 et MCP
## Préparation et limites
Le MCP Visual Studio est fourni dans l’environnement de développement de Codex, conformément au besoin utilisateur. Cette livraison n’en connaît pas le fournisseur, le schéma ni les noms des outils et ne prétend pas l’avoir invoqué. La première session inventorie ses capacités avant d’écrire une commande. Un MCP d’IDE n’implique pas automatiquement le contrôle du personnage, la capture de la fenêtre de jeu ou la possibilité d’exécuter toutes les commandes de test.

Créer un profil de données de jeu **jetable**, séparé des parties personnelles. Relever dans la version locale la syntaxe de lancement du client/serveur et le mécanisme de datapath/deploy du template. Ne pas inventer des arguments de lancement. Ne pas copier les comptes, tokens ou toutes les sauvegardes pour déboguer.

## Capacités à vérifier
| Capacité recherchée | Vérification et preuve |
|---|---|
| Solution et build | Solution correcte, configuration active, erreurs de compilation et sortie persistée |
| Lancement ou attache | PID/processus attendu, build/assembly correspondant au commit |
| Breakpoints | Arrêt effectif sur ligne C# du mod et symboles chargés |
| Inspection | Pile, variables locales/coordonnées et exception lisibles |
| Tests | Découverte et exécution des projets, statut et rapport exporté si l’outil le permet |
| Arrêt/reprise | Déconnexion/fermeture propre sans laisser le jeu pausé ni verrou occupé |

Si une capacité test est absente, les tests peuvent utiliser un runner CLI validé ; cela ne remplace pas la preuve demandée du débogage par MCP. Si l’attache/breakpoint manque, L00-B reste BLOCKED. Le rapport indique l’outil manquant et l’opération précise à rendre possible, sans déclarer l’ensemble du projet impossible.

## Session reproductible
Prendre le verrou de session. Vérifier branche, commit, solution, configuration et sauvegarde cible. Compiler le mod, résoudre le déploiement d’une seule copie, puis lancer/attacher le processus correct. Vérifier l’empreinte de la DLL chargée et son PDB. Arrêter sur StartServerSide, puis sur le callback demandé avec une seed/coordonnée reproductible. Inspecter la pile, les variables et les invariants, reprendre l’exécution et conserver les preuves expurgées. Fermer ou détacher proprement et libérer le verrou.

Un diagnostic suit la chaîne **entrée canonique → snapshot → plan de colonne → écritures natives → cartes natives → état après ticks**. Cette séparation évite de corriger le solveur alors que le problème est un mauvais ID de fluide ou une carte de hauteur obsolète.

Pour les tests de concurrence, les pauses debugger peuvent masquer ou provoquer des timeouts : reproduire aussi sans breakpoint avec traces structurées. Pour les performances, compiler Release et **détacher complètement le debugger**. La session de mesure enregistre le matériel, les options et le protocole froid/chaud.

## Tests et rapports
Le runner .NET doit être verrouillé avec les dépendances de test. Avec VSTest effectivement sélectionné, un exemple de commande à adapter au chemin créé par L01-C est :
```powershell
dotnet test .\testsrc\WorldGen.Core.Tests\WorldGen.Core.Tests.csproj -c Release --logger "trx;LogFilePrefix=worldgen-core"
```
Cette commande est un exemple pour un projet à créer, pas un fichier déjà livré. Une configuration utilisant Microsoft Testing Platform doit adopter les options propres à ce runner. Source DEV-03. Les rapports sont distincts par sous-lot, configuration et exécution afin de ne pas s’écraser.

## Autorisations
Ni la demande de déboguer, ni le verrou MCP n’autorisent publication de release, push distant, suppression de branches, purge de partie personnelle ou installation de dépendances arbitraires. Les commandes de régénération doivent vérifier un marqueur de sauvegarde de test et requérir une action explicite. L’orchestrateur conserve la décision de fusion.
