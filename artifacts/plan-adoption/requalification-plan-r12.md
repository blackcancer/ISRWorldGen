# Plan de requalification L05-D

| Impacts examinés | Disposition | Régression / condition |
|---|---|---|
| L03-C → L14-A/B | EXTEND_AND_RETEST | L03-C 10/10 Debug/Release prouve couches/fractures immuables; exécuter T14-01..08 avant consommation C08. |
| L04-C → L18-A et L06-A | EXTEND_AND_RETEST | L04-C 7/7 Debug/Release et 23/23 amont restent preuves du budget annuel; exécuter T18-01..03 puis T05-05/06. |
| L05-C → L18-A/B/C | BLOCKED | L05-C T05-05/06 Core et L05-A/B/C Release 18/18 sont réutilisables pour C03 v1 seulement; attendre décision sidecar C12 versionné. |
| L10-A/B → schémas C08–C12 | BLOCKED | L10-A est IN_PROGRESS et les schémas sont une décision intégrateur; ne déduire aucun DONE. |
| L11-A → consommateurs natifs C08–C12 | BLOCKED | L11-A est BACKLOG: API/ownership natifs absents à requalifier. |
| L06-A, L08-A, L11-B/C, L12-B/C, L13-A/B/C | BREAKING_NEW_REVISION | Pas de preuve 1.2 disponible; si leurs snapshots impliquent géologie/bilan/seed révisés, créer un monde jetable et rejouer leurs tests. |

Les classifications remplacent les `TO_ASSESS` examinables du registre complémentaire uniquement dans cet artefact: le registre partagé reste la responsabilité de l'intégrateur.
