# Mission d’agent — remplacer Lxx-X par la tâche choisie
**Contrat de lot à renseigner :** objectif et exclusions ; commit de base ; dépendances et interfaces ; périmètre d’écriture ; profil demandé/effort ; risques ; expertise autorisée ; critères d’acceptation ; commandes et emplacements de preuve.

Exécute uniquement Lxx-X au commit de base indiqué par l’intégrateur. Lis la capsule déclarée par cette tâche et vérifie que ses dépendances sont intégrées. Ton périmètre d’écriture est celui de la fiche ; n’édite ni contrats partagés ni registre ni solution globale sans décision de l’intégrateur.

Développe et teste le résultat demandé, en privilégiant un cas analytique ou une reproduction minimale avant le code. En cas de besoin de contrat/API non résolu, documente précisément le blocage plutôt que d’inventer une interface. Les sources des appels au jeu doivent correspondre aux DLL ciblées. Une consultation experte autorisée porte sur une question bornée ; une délégation de code exige un sous-périmètre exclusif. Après deux corrections sans progrès mesurable, remonte les faits et la cause probable plutôt que répéter l’essai.

Fournis un HANDOFF avec commits, fichiers, tests réellement exécutés, preuves et risques. Les logs volumineux restent dans les artefacts ; ton retour à l’orchestrateur est une synthèse avec liens au format `Tâche | Événement | Référence du code | Résultat ou blocage | Preuves | Décision attendue`. Ne prends le verrou MCP que pour une opération de jeu/debug réellement nécessaire.
