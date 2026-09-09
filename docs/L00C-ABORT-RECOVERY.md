# L00-C — reprise bornée après interruption

Cette recette ne vise que les deux fichiers temporaires nommés exactement par
une campagne L00-C dans `GamePaths.Saves`. Elle ne recherche jamais des
sauvegardes par motif et ne supprime ni dossier, ni sauvegarde utilisateur, ni
bootstrap.

## Machine d'états durable

Chaque transition est un reçu JSON strict, créé avec `CreateNew` **avant** son
effet externe. Les reçus sont ordonnés et immuables dans
`campaigns/<runId>/abort/` :

| Etat durable | Reçu avant effet | Résidu accepté après interruption | Reprise |
|---|---|---|---|
| `prepared` | `00-prepared.json` | aucun fichier | vérifie puis ne supprime rien |
| `primary-create-intent` | `01-primary-create-intent.json` | primaire absent, ou primaire (+ marqueur) | supprime seulement le primaire attesté présent |
| `primary-created` | `02-primary-created.json` | primaire + marqueur | supprime cette paire exacte |
| `secondary-create-intent` | `03-secondary-create-intent.json` | primaire + marqueur, secondaire absent ou (+ marqueur) | supprime les éléments exacts présents |
| `secondary-created` | `04-secondary-created.json` | deux paires | supprime les deux paires exactes |
| `cycling` | `05-cycling.json` | deux paires | supprime les deux paires exactes |
| `sealed` | reçu de nettoyage existant | contrat de nettoyage scellé existant | inchangé |
| `cleaned` | reçu d'intention puis `abort-cleaned.json` | aucun fichier attendu | idempotence refusée, preuve conservée |

Une interruption à chaque frontière est donc représentée par le dernier reçu
écrit, jamais déduite de l'absence d'un fichier. Un reçu d'intention de
nettoyage capture les hashes des fichiers encore présents avant toute
suppression; une reprise après interruption du nettoyage refuse tout octet qui
ne correspond plus à cette capture.

## Refus obligatoires

La reprise exige : PID de lancement identique à celui de la provenance et
confirmé arrêté par l'appelant; chaîne canonique sans point de réparation
(`.local/L00C`, `campaigns`, campagne, reçus); provenance et journal stricts;
et exactement le sous-ensemble de fichiers autorisé par le dernier état. Une
extension, un chemin, un rôle, un hash, un reçu, un PID, une entrée additionnelle
de la campagne ou un point de réparation non conforme provoque un refus et ne
modifie aucun octet de `GamePaths.Saves`.

L'oracle exécutable couvre les six frontières pré-scellage, les deux résidus de
création pour chacune, l'arrêt pendant le nettoyage, le cas primaire seul
observé, la paire complète, et chaque classe de falsification/refus. Il utilise
exclusivement des répertoires temporaires.
