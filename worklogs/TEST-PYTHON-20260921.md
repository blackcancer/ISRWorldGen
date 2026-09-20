# Correction des prerequis Python des tests L05-D

Base : dfcac47b05ce1affe0da07ce4e28e894ebec3daf, branche main demande par l'utilisateur.
Le journal utilisateur annonce 344/346 tests reussis et une compilation reussie;
les deux echecs sont des lancements Python 9009, pas des assertions de generation.

## Changements

- Precontrole Python 3.10+ executable, detection python/python3/py -3 et choix
  explicite PythonPath. L'interpreteur retourne par sys.executable est verifie
  directement et transmis par ISR_TEST_PYTHON, restaure a la sortie du lanceur.
- Les tests L05-D utilisent ce chemin sans dependance a un alias Store. Leurs
  quatre methodes et assertions sont conservees. Sorties lues conjointement en
  UTF-8 et processus borne a 30 secondes avec terminaison de l'arbre si necessaire.
- Projet de regression portable et pipeline Windows/Linux Debug/Release. Ni
  dependance NuGet nouvelle (MSTest 4.0.2 existant), ni appel a l'API du jeu,
  modification de solution de production, filtre supplementaire ou faux PASS.

## Portee et transitions

Les probes n'executent qu'un code JSON de version avec un delai borne, capturent
les erreurs et desactivent l'installation automatique pour leurs enfants. Les
scenarios de CI utilisent des repertoires temporaires propres aux jobs heberges;
le faux interpreteur ne touche que son marqueur de fixture et retourne 9009.
Le vrai prepare_plan_update travaille sur les copies jetables deja definies par
AdoptionFixture. L'integrite du registre actif et du filtre est controlee apres
execution. Aucun compte, sauvegarde, profil F5 ou API native n'est touche.

La session d'edition ne possede ni SDK dotnet, ni pwsh, ni MCP Visual Studio.
Les preuves executables sont a lire dans la CI associee au commit; aucun resultat
n'est declare d'avance. Compilation complete locale, 346 tests sur la machine
utilisateur, jeu et campagne certifiee restent NOT_RUN ici. Relecture de l'auteur
seulement; aucune revue independante n'est simulee et aucun lot ne passe DONE.
