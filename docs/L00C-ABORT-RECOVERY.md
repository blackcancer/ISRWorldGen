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

## Compatibilité unique du résidu pré-journal `a7290d12...`

Cette voie ne complète pas et ne relâche pas la machine d'états ci-dessus. Elle
est compilée uniquement dans le harness Debug, invoquée par deux scripts
d'intégrateur distincts, et verrouillée dans le code sur :

- `runId=a7290d12e6f54247bae27b71e2e571cf` ;
- `runtimeProcessId=74920` ;
- la forme historique exacte `primary-raw-only` : primaire `.vcdbs` présent,
  sans marqueur, secondaire, marqueur secondaire, journal `abort/`, reçu de
  nettoyage courant, nom possédé additionnel ou entrée de campagne inconnue.

`CleanupAfterRuntimeStopped` ne consulte jamais cette compatibilité. Sans
journal courant, il refuse toujours. Seul
`CleanupLegacyPreJournalAfterRuntimeStopped`, inaccessible depuis le lanceur
générique, peut consommer l'autorité manuelle décrite ci-dessous.

### Etats durables et frontières d'autorité

| Etat | Fichiers durables | Effet autorisé |
|---|---|---|
| historique non attesté | provenance + evidence + primaire brut | aucun ; les deux nettoyeurs refusent |
| manifeste écrit, non scellé | `legacy-prejournal-recovery-manifest.json` seul | aucun ; collision immuable à traiter par l'intégrateur |
| autorité scellée | manifeste + `legacy-prejournal-recovery-seal.json` | aucun tant que leurs deux SHA-256 ne sont pas fournis au lanceur dédié |
| intention de nettoyage | + `legacy-prejournal-recovery-intent.json` | suppression du seul primaire canonique portant encore le SHA-256 attesté |
| primaire supprimé | intention présente, primaire absent | écriture du reçu final seulement ; reprise déterministe autorisée avec les mêmes hashes |
| terminé | + `legacy-prejournal-recovery-cleaned.json` | aucun ; tout replay refuse, même si un fichier réapparaît au chemin historique |

Le manifeste enregistre les chemins canoniques de la racine laboratoire, de
`GamePaths.Saves`, de la campagne, de la provenance, du primaire et du
secondaire attendu absent ; les SHA-256 de la provenance et du primaire ; le
PID ; la forme historique ; et l'attestation littérale de l'intégrateur. Le
seal, créé avec `CreateNew`, lie le SHA-256 exact du manifeste, le PID, le
primaire et son SHA-256. L'intention et le reçu final sont également créés avec
`CreateNew`, `WriteThrough` et `Flush(true)`. Aucun de ces reçus n'est supprimé
par le mécanisme.

Avant de créer l'autorité, puis avant l'intention et de nouveau au plus près de
la suppression, l'implémentation revalide les ancêtres et points de réparation,
les types fichier/répertoire, l'ensemble exact des entrées connues, la
provenance stricte, les identités croisées, les deux hashes scellés fournis par
l'opérateur et les octets du primaire. Un marqueur, un secondaire, un nom
possédé additionnel, une entrée de campagne inattendue, une falsification, un
PID vivant/réutilisé ou différent, un hash changé ou un replay refuse avant
suppression et conserve les octets présents.

La preuve d'arrêt est réévaluée à l'entrée puis immédiatement avant chaque
écriture durable (`manifest`, `seal`, intention, reçu final) et avant la
suppression. Si le PID est réutilisé entre deux frontières, l'effet suivant ne
se produit pas : un manifeste non scellé reste inerte, une intention reste
rejouable sans perte, et une suppression déjà autorisée reste finalisable sans
nouvelle suppression.

Après la suppression et avant le reçu final, l'autorité scellée, l'intention
octet-exacte, la forme résiduelle et les chaînes de confiance sont encore
validées. Une falsification sur cette frontière laisse donc le primaire déjà
supprimé mais interdit tout faux reçu `cleaned` ; la reprise reste refusée tant
que l'intention ne correspond pas exactement à celle créée avant suppression.

### Procédure opérateur, après intégration et revue

1. Ne pas lancer Vintage Story. Confirmer manuellement que le PID `74920` est
   arrêté et n'a pas été réutilisé, puis relever depuis la provenance les
   chemins canoniques exacts de `.local/L00C`, de `GamePaths.Saves`, de la
   campagne et du primaire.
2. Vérifier visuellement la forme `primary-raw-only`, calculer séparément les
   SHA-256 du primaire et de `campaign-provenance.json`, et conserver ce relevé.
3. Exécuter une seule fois
   `New-L00CLegacyPreJournalRecoveryManifest.ps1` avec ces chemins/hashes,
   `RunId`, `RuntimeProcessId`, la DLL Debug revue et l'attestation exacte
   `I-ATTEST-L00C-A7290D12-PRIMARY-RAW-ONLY`. Ce script ne supprime rien. Il
   retourne les SHA-256 du manifeste et du seal.
4. Comparer le JSON scellé au relevé avant toute suppression. Puis exécuter
   explicitement `Invoke-L00CLegacyPreJournalRecovery.ps1` avec les deux hashes
   retournés et la même DLL Debug. Ce script supprime uniquement le primaire
   exact ; il ne supprime ni campagne, ni preuve, ni autre sauvegarde.
5. Conserver manifeste, seal, intention et reçu final. Si l'opération est
   interrompue après l'intention, relancer seulement la même commande avec les
   mêmes hashes. Si le manifeste existe sans seal, ne rien écraser ni supprimer
   automatiquement : remonter l'état à l'intégrateur. Un reçu final existant est
   terminal et tout replay doit rester refusé.

Le gate `Test-L00CCampaignStorage.ps1` simule cette forme dans des répertoires
temporaires. Il vérifie la préservation octet-à-octet pour les refus, les deux
frontières d'interruption du nettoyage, le manifeste non scellé, la suppression
du seul fichier autorisé et le refus de replay. Il ne lit ni ne modifie le vrai
AppData.
