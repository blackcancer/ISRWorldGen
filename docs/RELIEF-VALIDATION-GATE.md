# Priorité active — relief brut comparé à la Terre

Consigne utilisateur du 21 septembre 2026 : reprendre d'abord les reliefs, fonds marins compris. L'acceptation appartient à l'assistant par comparaison à des reliefs terrestres réels, PAS à une approbation attendue de l'utilisateur. Aucun nouveau travail d'érosion ne peut justifier ou masquer une mauvaise base.

## Ordre de travail
1. Une même fonction d'altitude solide pour terres ET fonds marins. Aucun remplacement sous le niveau marin par la hauteur de l'eau ou une couleur constante.
2. Vues du monde entier, puis grandes fenêtres repérées. Les détails ne prouvent pas la cohérence d'ensemble.
3. Données float64 conservées et PNG 16 bits à échelle fixe, conversion documentée. Palette cartographique facultative, non substituée à la heightmap.
4. Confrontation à ETOPO 2022 et à plusieurs contextes prédéfinis : Alpes/Pô/Ligure, Andes/fosse chilienne, Norvège/plateforme/bassin marin. Décrire les différences de résolution, de projection et de compression verticale. Les données réelles sont déjà érodées ; le relief initial doit avoir une ossature crédible, pas imiter tous leurs détails d'érosion.
5. L'assistant examine effectivement les grandes cartes et leurs métriques. Un succès numérique ou une réduction du biais de grille ne vaut jamais acceptation géographique. Consigner les défauts et itérer ; ne pas déplacer les seuils pour déclarer PASS.
6. L'érosion ne reprend qu'après une acceptation explicite et étayée de la base brute. Aucun besoin de validation manuelle utilisateur n'est créé.

## Statuts
Le générateur et l'export publient des états numériques seulement. L'examen géographique est séparé : REJECTED, ACCEPTED_RAW_RELIEF_WITH_LIMITATIONS ou NOT_EVALUATED. Le dernier état reste non accepté tant que le rapport comparatif de l'assistant ne l'a pas changé. La qualification native, l'intégration au jeu, les strates et les saisons restent distinctes.

## Protection de l'existant
Le candidat RawReliefModel est explicitement sélectionné par un programme de test. Aucun ancien monde ni identifiant de sauvegarde n'est remplacé, et aucun état de lot n'est réinitialisé. Les campagnes historiques restent des régressions, pas des autorisations de poursuivre l'érosion.

Sources de référence : NOAA NCEI, ETOPO 2022, doi:10.25921/fd45-gt74 ; documentation de la diffusion 60 secondes d'arc par NOAA OceanWatch. Les réponses originales et SHA-256 sont conservés dans les artefacts ; aucun relief de référence n'est synthétisé.
