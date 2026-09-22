# Raccordement de la rupture materielle a la chronologie oceanique

Base main : 1c95640a86ba2441545c1507b46ff42292a13817. Origine du candidat : archive conversation ISRWorldGen_Rift_Necking_Candidat.zip, jamais executee en C# avant cette reprise.

Le candidat RiftNecking et ses 18 controles sont importes sans changement de loi. Trois classes autonomes de la branche ocean-evidence au commit 316ca3e477035d9ee179d6dde76d0c85a397375f sont reprises par leurs blobs exacts : SpreadingKinematics ae5aa2255181005d1fdaf17b85b97c4cdfda2364, SpreadingTimeline 3ed5986b5ff34ffe4f71e742996ff7e680a5cb7e, OceanBirthMap a544444ad77b956856be40b2e4935a8ed098944d. Aucun scenario de dorsale imposee, historique global alternatif ou loi thermique de cette branche n'est fusionne.

RiftSpreadingAdapter raccorde uniquement les sources issues de la rupture aux deux flancs materiels. La nouvelle suite d'integration utilise des trajectoires directes independantes de l'inversion : interieur, quatre orientations, trois asymetries, debuts/fins exacts, raccord temporel actif, extinction suivie de translation, pauses, points hors couverture, aire creee, refus et concurrence. Une erreur reste un FAIL et est preservee avec ses coordonnees, sans interrompre la collecte des autres cas.

## Preparation des transitions
Publication sur une nouvelle branche codex/rift-necking-20260922 a partir d'une base relue. L'arbre est uniquement additif ; aucun fichier existant, registre, sauvegarde, SDK, package ou adaptateur Vintage Story n'est remplace. Des blobs sans reference peuvent rester apres interruption ; ils sont sans effet sur main. La reference de branche n'est creee qu'apres confirmation du commit. Un push main ne sera envisage qu'apres tests et verification de l'ascendance, jamais par force. Une execution partielle conserve INCOMPLETE et/ou FAILED, pas COMPLETE ; les dossiers CI ne sont pas reutilises et les journaux/source sont publies meme en cas d'echec. Une erreur ne se corrige pas en relancant le meme candidat sans diagnostic.

Preflight local : XML des deux projets, YAML et syntaxe Python verifiees ; les hashes des fichiers de dependance correspondent aux blobs sources. C# et CI : NOT_RUN a ce commit, a executer avant conclusion. Revue d'un second agent et qualification native/MCP : NOT_RUN. Le Core ne reference pas le jeu, aucun symbole API Vintage Story nouveau.

## Portee geologique
Coupe ouverte en deformation plane, resistances relatives et critere de rupture fournis. Conservation de chaque origine continentale ; l'ocean nouveau est un apport distinct. Apres rupture les fragments translatent sans nouvel etirement. Ce n'est ni une localisation de rifts depuis le monde, ni une loi de subduction, ni une nouvelle heightmap. Le modele de carte entier de 1 000 000 unites n'est pas recadre/modifie. Aucun monde ou lot n'est accepte ; erosion suspendue.
