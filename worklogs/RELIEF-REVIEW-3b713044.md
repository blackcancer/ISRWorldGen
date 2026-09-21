# Revue exécutée du relief brut — 3b713044

**Code examiné : `3b71304495facec48cecb0b78cc0785fc1101eb7`.**
**Référence avant correction : `5d4256ad7f666d5f3bc38fea6164aa33c72c4130`.**
**Décision géographique de l'assistant : REJECT. L'érosion de ce candidat n'est pas autorisée.**

Cette revue porte sur ces snapshots précis. Elle ne vaut ni examen d'un éventuel commit ultérieur, ni modification du registre de lots. Aucune approbation de l'utilisateur n'est attendue pour la remplacer.

## Exécutions réellement observées

Run GitHub Actions **35592023604**, workflow Raw relief and Earth references, quatre jobs terminés avec succès : Windows/Linux × balanced/regional-262k, tous en Release. Le programme C# du Core a calculé trois seeds fixes par contexte. Les contrôles AdvancedRidgeChecks et MassifCompositionChecks s'ajoutent aux contrôles géométriques antérieurs ; aucun de leurs succès ne signifie réalisme géographique.

Les artefacts Linux téléchargés et contrôlés sont :

- regional-262k : ID **10634673959**, SHA-256 ZIP `54c6ed8055676d378fd7f8b531d4e46c2756c0f4453ab6855f9f56531a3bb2b4` ;
- balanced : ID **10633954866**, SHA-256 ZIP `f5aa729bed5cf7d7b023ce415f8ad334c41d61c33c23407202e38dbde0101638`.

Les six champs comportent chacun **1024 × 1024 vraies mesures**, soit **6 291 456 échantillons**. Les deux tailles sont des mondes différents, pas deux zooms du même monde. Chaque comparaison avant/après conserve son profil, ses coordonnées et sa seed.

## Vérification indépendante de l'encodage

Les SHA-256 des champs float64 little-endian correspondent aux manifestes. Les PNG ont été ouverts avec **Pillow**, indépendant de l'encodeur PNG standard Python du dépôt. Les dimensions, l'en-tête PNG 16 bits en niveaux de gris, les empreintes et chaque valeur quantifiée ont été comparés aux champs exacts.

Décodage inchangé : **Y = code × 383 / 65535**. L'erreur maximale observée sur les six cartes est **0.0029221026320271903 bloc**, inférieure à la borne 383/131070. Les fonds marins ont plusieurs milliers de valeurs quantifiées distinctes ; ils ne sont remplacés ni par Y=168 ni par un masque de surface d'eau. Les aperçus sont en 8 bits, les PNG numériques en 16 bits et les valeurs exactes restent en float64.

| Contexte entier | Seed | Médiane terrestre au-dessus de la mer avant → après, blocs | Terres à au moins 64 blocs au-dessus de la mer avant → après |
|---|---:|---:|---:|
| 131072², pas 128 | -437287116 | 14.79 → 17.92 | 1.59 % → 3.16 % |
| 131072², pas 128 | 73 | 37.54 → 42.62 | 18.41 % → 19.01 % |
| 131072², pas 128 | 20260906 | 27.18 → 34.74 | 6.77 % → 7.27 % |
| 262144², pas 256 | -437287116 | 13.29 → 17.03 | 4.01 % → 4.18 % |
| 262144², pas 256 | 73 | 22.51 → 29.32 | 10.64 % → 11.14 % |
| 262144², pas 256 | 20260906 | 21.59 → 27.91 | 9.16 % → 8.86 % |

Le seuil de 64 blocs est un repère de diagnostic, **pas** une définition d'une montagne terrestre. Les maximums diminuent dans cette correction : la hauteur est moins concentrée sur les seuls axes de crête. Aucun seuil n'est ajusté pour annoncer un PASS géographique.

## Comparaison terrestre effectuée

Trois extraits NOAA NCEI **ETOPO 2022 v1 60 arc-second Ice Surface, EGM2008**, DOI **10.25921/fd45-gt74**, ont été contrôlés : Alpes–Pô–Ligurien, Andes–fosse chilienne et Norvège–plateau continental. Les sources JSON compressées ont été décompressées et vérifiées par SHA-256 ; les valeurs et l'ordre spatial ont été confrontés aux float64 et aux PNG16 de référence.

La référence contient un relief actuel déjà érodé, pas un socle vierge d'érosion. Ses mètres ne sont pas assimilés aux blocs du jeu. Les mesures relatives du script de comparaison restent exploratoires ; aucune note universelle de réalisme n'en est déduite. Les représentations de référence peuvent employer un aspect métrique approché au centre de l'emprise, explicitement distinct d'une reprojection SIG exacte.

## Lecture morphologique des six vues entières

Progrès : les massifs occupent davantage de surface autour des crêtes. Les ramifications non appariées et les terminaisons décroissantes de b5719689 restent présentes ; la composition v7 diminue leur domination sur tout le relief. Les plaines ne reçoivent pas de montagnes par simple texture hors support géologique. Ces constats concernent le candidat du Core, pas une matérialisation native.

Refus maintenu :

1. Les marges continentales et plusieurs îles périphériques gardent des formes trop systématiques issues des enveloppes de croûte. Le problème reste visible dans les emprises entières ; une jolie crête ne le résout pas.
2. Les grands fonds possèdent des valeurs variables, mais encore trop peu d'ensembles morphologiques identifiables par comparaison aux références observées. Fidélité numérique et crédibilité du fond marin sont deux critères différents.
3. L'articulation des massifs, plateaux et plaines doit encore être évaluée dans des fenêtres métriques raccordées au même monde, sans confondre les deux profils actuels avec un zoom.

La prochaine modification doit encore concerner **le relief brut**, notamment la géométrie de croûte/marges et la structure des bassins océaniques. L'érosion ne doit pas masquer ces défauts de construction initiale.

## Limites et conservation

Les six cartes de cette campagne ne contiennent aucune érosion, eau de surface ou retouche d'image. Les autres suites de non-régression peuvent continuer à tester leurs anciens sous-systèmes : cela ne vaut pas reprise du développement de l'érosion du candidat.

Aucun appel Vintage Story ou dépendance supplémentaire ; aucune partie personnelle, aucun registre ni format de sauvegarde modifié. Jeu, MCP Visual Studio, compilation complète avec les DLL natives et revue indépendante du code : **NOT_RUN** pour cette reprise. La revue géographique est celle de l'assistant, non une revue d'un second agent.

La galerie autonome contient les six comparaisons avant/après, leurs téléchargements PNG16 et les trois références. Sa syntaxe JavaScript a été vérifiée avec `node --check`. Le mode grand résultat ne masque pas les références ; aucun champ numérique n'est modifié par les contrôles d'affichage.

Archive finale assemblée hors Git : `ISRWorldGen_Reliefs_3b713044.zip`, **103976513 octets**, SHA-256 `62973ddee255aaf802c0080c2620e2e6330e34509aece87615f213e47ca85856`. Elle contient les six champs, PNG, comparaisons visuelles, références, galerie et vérifications. Les fichiers volumineux ne sont pas ajoutés à l'historique du dépôt.
