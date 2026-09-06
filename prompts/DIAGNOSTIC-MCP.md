# Diagnostic ciblé par Visual Studio/MCP
Traite le défaut désigné par TestId, seed, profil, coordonnées, commit et rapport. Lis seulement la tâche concernée, son contrat et la procédure MCP. Prends le verrou, découvre les outils réels si nécessaire, vérifie assembly/PDB et sauvegarde jetable, puis reproduis l’arrêt au point de transformation suspecté.

Compare successivement entrée canonique, snapshot, plan de colonne, écritures et maps natives. Ne corrige pas une couche amont sans montrer où l’invariant est cassé. Après correction, ajoute le cas de régression et reproduis sans pause debug. Les performances sont mesurées debugger détaché. Libère le verrou et conserve une preuve sans secret.
