# L19 — Données environnementales publiques pour les futurs mods

**Plan 1.4 · six sous-lots V1 · aucun comportement animalier.**

A/B/C conservent leur rôle documentaire. D/E/F ajoutent le support technique nécessaire. C dépend désormais de E pour décrire le service réellement livré. D n’attend pas C : cela évite de retarder inutilement l’implémentation derrière sa propre documentation finale.

| Mission | Objet | Dépendances |
|---|---|---|
| [L19-A](L19-A.md) | Inventorier les données et qualifier leur couverture | L05-D |
| [L19-B](L19-B.md) | Définir le dictionnaire, le contrat public et les responsabilités | L19-A |
| [L19-C](L19-C.md) | Revoir et transmettre le dossier sur le service réellement livré | L19-B, L11-C, L12-C, L19-E |
| [L19-D](L19-D.md) | Implémenter la collecte, les reçus et la persistance environnementale | L19-B, L10-B, L11-B |
| [L19-E](L19-E.md) | Implémenter l’API publique, l’index et le diagnostic bornés | L19-D, L02-B |
| [L19-F](L19-F.md) | Qualifier le socle avec un consommateur externe et le monde réel | L19-C, L19-E, L10-C, L09-C |

Ordre local : A → B → D → E → C → F, avec les prérequis techniques externes indiqués. L13-A exige F en plus de ses dépendances historiques ; C reste aussi un prérequis transitif/direct conservé pour compatibilité. G4 inclut les six missions L19 et G5 reprend les preuves sur la baseline finale. Aucun chemin vers L20 n’est ajouté.

L19-A a déjà un inventaire exploitable dans le dépôt observé ; le réviser au lieu de le remplacer par un gabarit. L19-B est en pause dans le commit observé. Les statuts ne sont pas attribués par ce README.
