# Prompt de démarrage à transmettre à Codex

Nous développons le mod de génération décrit dans ce dépôt, en C#, avec Visual Studio Community 2026. Le template Vintage Story est déjà installé et tu disposeras d’un MCP Visual Studio pour déboguer le processus réel.

Commence uniquement par **L00-A**. Lis `AGENTS.md`, `docs/02-SOCLE.md`, `tasks/L00/L00-A.md` et les lectures obligatoires déclarées par cette fiche ; ne charge pas tout le cahier des charges. Tu peux utiliser `tools/Get-TaskContext.ps1 -TaskId L00-A` pour fabriquer la capsule, ou lire exactement ces fichiers.

Inspecte la solution existante, l’installation du jeu et les références. N’invente ni version .NET, ni API, ni nom d’outil MCP. Établis la cible locale et le build template avant de modifier la génération. Conserve les sauvegardes personnelles intactes ; utilise un espace de test isolé. Un AGENTS.md déjà présent dans le dépôt doit être fusionné consciemment, pas écrasé.

Remets une passation selon `templates/HANDOFF.md`, avec ce qui a été vérifié, les commandes/preuves, les décisions de version et les blocages. Ne lance pas L00-B ni le reste du développement sans mission explicite de l’orchestrateur. Un résultat non exécuté reste NOT_RUN.
