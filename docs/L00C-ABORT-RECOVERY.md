# L00-C — reprise bornée après interruption

Cette recette ne vise que les artefacts exacts d'une campagne L00-C sous le
`GamePaths.Saves` fourni. Elle ne parcourt jamais AppData pour découvrir une
cible et ne supprime aucun dossier.

## Ensemble SQLite natif

Pour chaque rôle, les seuls noms natifs reconnus sont :

- base : `<save>.vcdbs` ;
- WAL : `<save>.vcdbs-wal` ;
- mémoire partagée : `<save>.vcdbs-shm` ;
- marqueur L00-C : `<save>.vcdbs.l00c-appdata.json`.

Avant une suppression, seuls `db`, `db+wal` et `db+wal+shm` sont des ensembles
natifs complets. Un WAL sans base, ou un SHM sans base et WAL, refuse. Un nom
voisin (`-journal`, `-wal.extra`, autre rôle ou suffixe) reste inconnu et fait
refuser la reprise sans suppression. La vacance précédant la création native
contrôle les quatre chemins exacts.

L'ordre de suppression est immuable : `shm`, `wal`, marqueur, `db` pour le
primaire, puis le même ordre pour le secondaire. L'intention durable capture
le SHA-256 de chacun des huit chemins (chaîne vide seulement si absent). Une
reprise accepte uniquement un préfixe déjà supprimé dans cet ordre.

## Machine d'états courante

| État durable | Résidu admis avant l'intention de nettoyage |
|---|---|
| `prepared` | aucun artefact |
| `primary-create-intent` | primaire absent, ou ensemble natif primaire valide, marqueur éventuellement publié |
| `primary-created` | ensemble natif primaire valide + marqueur |
| `secondary-create-intent` | primaire attesté ; secondaire absent ou ensemble natif valide, marqueur éventuellement publié |
| `secondary-created` / `cycling` | deux ensembles natifs valides + deux marqueurs |
| `sealed` | reçu final v2 avec chemins et hashes des deux rôles |

La reprise d'abort écrit durablement `abort-cleanup-intent.json`, puis revalide
journal, provenance, campagne, noms possédés, identités, hashes et preuve de PID
arrêté immédiatement avant chaque suppression. Après la dernière frontière,
elle revalide encore tout avant `abort-cleaned.json`. L'intention doit rester
octet pour octet dans sa sérialisation canonique : même un espace ou un ordre de
propriétés différent refuse, y compris après un préfixe déjà supprimé. Tous les
noms de reçus connus ont un type imposé ; un dossier placé sur un nom de reçu
final refuse avant toute suppression.

Le nettoyage scellé suit la même discipline avec
`sealed-cleanup-intent.json` et `sealed-cleaned.json`. Le reçu de scellement v2
porte les chemins canoniques et hashes finaux de la base, du WAL, du SHM et du
marqueur de chaque rôle. Un hash modifié, une disparition hors ordre, un point
de réparation, une entrée de campagne inconnue, un PID vivant/réutilisé ou un
replay refuse. Le lanceur réinterroge réellement le PID à chaque preuve ; il ne
met jamais en cache un booléen `true`.

Sans reçu scellé, le nettoyage générique exige toujours le journal `abort/`.
Il n'existe aucun fallback par nom de fichier ou provenance seule.

## Compatibilité unique du résidu pré-journal `a7290d12...`

Cette voie distincte est verrouillée dans le harness Debug sur :

- `runId=a7290d12e6f54247bae27b71e2e571cf` ;
- `runtimeProcessId=74920` ;
- l'attestation littérale
  `I-ATTEST-L00C-A7290D12-PRIMARY-DB-WAL-SHM-ONLY` ;
- exactement le primaire `.vcdbs`, `.vcdbs-wal` et `.vcdbs-shm`, sans marqueur,
  secondaire (ni ses sidecars), journal courant, reçu courant, nom possédé
  supplémentaire ou entrée de campagne inconnue.

Le manifeste v2 exige de l'intégrateur les trois chemins canoniques et trois
SHA-256, plus le SHA-256 de la provenance. Il ne découvre aucun candidat. Son
seal v2 lie octet pour octet le manifeste, le PID, les trois chemins et hashes.
Le nettoyage dédié exige les hashes externes du manifeste et du seal, puis crée
une intention durable. Il supprime uniquement `primary-shm`, `primary-wal`,
`primary-db`, dans cet ordre. Avant chaque effet, puis avant le reçu final, il
revalide l'autorité complète, l'intention exacte, l'ensemble de noms, les
hashes, les ancêtres et la preuve d'arrêt. Seul un préfixe supprimé est
reprenable. Le reçu `legacy-prejournal-recovery-cleaned.json` rend tout replay
terminal, même si les trois octets historiques sont recréés.

`CleanupAfterRuntimeStopped` ne consulte jamais cette compatibilité. Seul
`CleanupLegacyPreJournalAfterRuntimeStopped`, appelé par le script dédié, peut
consommer cette autorité unique.

## Procédure opérateur

1. Ne pas lancer Vintage Story. Vérifier que le PID `74920` est arrêté et non
   réutilisé. Relever les chemins canoniques de `.local/L00C`,
   `GamePaths.Saves`, de la campagne, de la base primaire, de son WAL et de son
   SHM.
2. Confirmer que les tailles observées sont respectivement 4096, 57712 et 32768
   octets, que les trois noms sont exactement ceux ci-dessus, et qu'aucun autre
   nom possédé n'existe. Calculer séparément leurs trois SHA-256 et celui de
   `campaign-provenance.json`.
3. Exécuter une seule fois
   `New-L00CLegacyPreJournalRecoveryManifest.ps1` avec ces quatre chemins/hashes,
   le runId, le PID, la DLL Debug revue et l'attestation littérale. Ce script ne
   supprime rien ; conserver les SHA-256 du manifeste et du seal retournés.
4. Comparer le JSON scellé au relevé. Exécuter explicitement
   `Invoke-L00CLegacyPreJournalRecovery.ps1` avec les deux hashes et la même DLL.
   Il ne supprime que les trois artefacts primaires exacts et conserve campagne,
   preuves et sauvegardes étrangères.
5. En cas d'interruption après l'intention, relancer uniquement la même commande
   avec les mêmes hashes. Un manifeste sans seal, un hash différent ou un reçu
   final existant exige un arrêt et une inspection ; ne rien écraser.

L'oracle `Test-L00CCampaignStorage.ps1` travaille exclusivement dans des
répertoires temporaires. Il couvre les formes `db`, `db+wal`, `db+wal+shm`, les
deux rôles, chaque frontière de suppression abort/scellée/legacy, les collisions
exactes, noms voisins, chemins échappés, points de réparation, falsifications,
PID vivant et replay. Il ne lit ni ne modifie le vrai AppData.
