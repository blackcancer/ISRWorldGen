# Responsabilités L19-B — frontière environnementale et futur consommateur

**Statut : proposition documentaire r1.5 sur baseline `4754e2e`.** Ce texte couvre R19-04 à R19-06 et prépare R-AQ-13; il ne crée aucun registre, contrat C#, index, service ni comportement runtime.

| Sujet | ISRWorldGen / C13-C13-AQ proposé | Futur consommateur | Limite ferme |
|---|---|---|---|
| Catalogue historique | Produire puis publier, après D/E, les projections `Planned`/`GeneratedBaseline`, identité, révision, provenance et coverage. | Consommer uniquement des capacités réellement annoncées. | Une date de lecture ne crée pas `ObservedAt`. |
| Eau, fond et verticalité | Décrire `WaterBodyId`/`WaterCompartmentId`, géométries, intervalle vertical, milieu, fond/support et connexions sourcés. | Vérifier eau présente, profondeur, gel, qualité, risque et accès de l'espèce. | Pas de potabilité, de navigation ou de volume vivant garanti. |
| Communautés et candidats | Exposer seulement catégories fixes, reçus, rejets et propriétés génériques traçables. | Définir espèces, besoins, ration, innocuité, perception, territoires et reproduction. | Pas de population, faim, stock ou score d'habitat dans L19. |
| Monde modifié et saisons | Conserver le caractère historique et la révision de génération. | Observer/invalider localement après récolte, abattage, construction, eau modifiée ou gel/dégel. | Aucun rescan mondial par animal, ni régénération pour rétablir un catalogue. |
| Chunks non chargés | D/E liront seulement les métadonnées persistées qualifiées et bornées; aucune requête ne charge/génère un chunk. | Gérer l'inconnu, la réessaye bornée et les observations dans les chunks nécessaires. | `LegacyMissing`, `PartialCoverage` et cache absent ne sont ni zéro ni absence géographique. |

## Décision de gel demandée à l'intégrateur

Approuver séparément les sémantiques C13 et C13-AQ, le packaging d'un unique assembly de contrat partagé, la découverte optionnelle du fournisseur et les versions/capacités. D/E doivent ensuite prouver les probes de chargement, persistance et requêtes; F doit qualifier un consommateur externe. L19-B ne modifie ni ces contrats ni les projets.
