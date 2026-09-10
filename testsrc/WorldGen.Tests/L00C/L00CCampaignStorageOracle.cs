// Deterministic executable oracle: all filesystem paths are fresh temp paths,
// never AppData and never a Vintage Story process.
#nullable enable
using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CCampaignStorageOracle
{
    internal static int Run()
    {
        string temp = Path.Combine(Path.GetTempPath(), "l00c-appdata-storage-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(temp, "repo", ".local", "L00C"); string saves = Path.Combine(temp, "fake-GamePaths-Saves");
        try
        {
            Directory.CreateDirectory(root); Directory.CreateDirectory(saves);
            byte[] oldBytes = Encoding.UTF8.GetBytes("personal-world-untouched"); string oldFile = Path.Combine(saves, "personal-world.vcdbs"); File.WriteAllBytes(oldFile, oldBytes);
            var run = L00CCampaignStorage.CreateForRun(root, saves, "22222222222222222222222222222222");
            Check(run.SaveTargets.Count==10&&run.SaveTargets.Select(target=>target.CanonicalSavePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()==10);
            for(int iteration=1;iteration<=5;iteration++)foreach(char slot in new[]{'a','b'})
            {
                string expected=Path.GetFullPath(Path.Combine(saves,"ISRWorldGen-L00C-"+run.RunId+"-iteration-"+iteration.ToString("D2")+"-"+slot+".vcdbs"));
                Check(string.Equals(run.SavePath(iteration,slot),expected,StringComparison.OrdinalIgnoreCase));
                Check(L00CCampaignStorage.IsCampaignSavePath(root,saves,expected));
            }
            Check(Bytes(oldBytes, File.ReadAllBytes(oldFile)));
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var clean=Fresh(root,saves,Guid.NewGuid().ToString("N"));clean.SealForExternalCleanup();
            Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,clean.RunId,pid,()=>false));Check(!AllAbsent(ArtifactOrder(clean)));
            L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,clean.RunId,pid,()=>true);Check(AllAbsent(ArtifactOrder(clean)));Check(Bytes(oldBytes,File.ReadAllBytes(oldFile)));
            L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,clean.RunId,pid,()=>true); // exact idempotent replay

            foreach(int boundary in new[]{0,1,2,3,10,19,20,30,39})
            {
                var x=Fresh(root,saves,Guid.NewGuid().ToString("N"));x.SealForExternalCleanup();
                using(L00CCampaignStorage.OverrideTransitionHookForTests(stage=>{if(stage=="sealed-delete-"+boundary)throw new InvalidOperationException("stop");}))Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true));
                string[] order=ArtifactOrder(x);for(int n=0;n<=boundary;n++)Check(!File.Exists(order[n]));for(int n=boundary+1;n<order.Length;n++)Check(File.Exists(order[n]));
                L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true);Check(AllAbsent(order));
            }

            foreach(int state in new[]{0,1,2,9,10,19,20,21})
            {
                foreach(bool externalWrite in new[]{false,true})
                {
                    var x=AbortAt(root,saves,Guid.NewGuid().ToString("N"),state,externalWrite);
                    L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true);Check(AllAbsent(ArtifactOrder(x)));
                }
            }

            foreach(string suffix in new[]{string.Empty,"-wal","-shm",".l00c-appdata.json"})
            {
                string id=Guid.NewGuid().ToString("N");string target=Path.Combine(saves,"ISRWorldGen-L00C-"+id+"-iteration-03-b.vcdbs")+suffix;File.WriteAllText(target,"collision");
                Expect(()=>L00CCampaignStorage.CreateForRun(root,saves,id));Check(File.ReadAllText(target)=="collision");
            }
            var late=L00CCampaignStorage.CreateForRun(root,saves,Guid.NewGuid().ToString("N"));var lateTarget=late.SaveTargets[7];File.WriteAllText(L00CCampaignStorage.WalPath(lateTarget.CanonicalSavePath),"late");Expect(()=>late.RequireVacantNativeCreateTarget(lateTarget.Role,lateTarget.CanonicalSavePath));Check(File.ReadAllText(L00CCampaignStorage.WalPath(lateTarget.CanonicalSavePath))=="late");

            var missing=Fresh(root,saves,Guid.NewGuid().ToString("N"));File.Delete(L00CCampaignStorage.MarkerPath(missing.SaveTargets[4].CanonicalSavePath));AssertPreserved(missing,()=>missing.SealForExternalCleanup());
            var corrupt=Fresh(root,saves,Guid.NewGuid().ToString("N"));File.WriteAllText(L00CCampaignStorage.MarkerPath(corrupt.SaveTargets[4].CanonicalSavePath),"{");AssertPreserved(corrupt,()=>corrupt.SealForExternalCleanup());
            var copied=Fresh(root,saves,Guid.NewGuid().ToString("N"));File.Copy(L00CCampaignStorage.MarkerPath(copied.SaveTargets[0].CanonicalSavePath),L00CCampaignStorage.MarkerPath(copied.SaveTargets[1].CanonicalSavePath),true);AssertPreserved(copied,()=>copied.SealForExternalCleanup());
            var provenance=Fresh(root,saves,Guid.NewGuid().ToString("N"));File.AppendAllText(provenance.ProvenancePath," ");AssertPreserved(provenance,()=>provenance.SealForExternalCleanup());
            var journal=Fresh(root,saves,Guid.NewGuid().ToString("N"));File.AppendAllText(Path.Combine(journal.AbortDirectory,"21-cycling.json")," ");AssertPreserved(journal,()=>journal.SealForExternalCleanup());
            var afterSeal=Fresh(root,saves,Guid.NewGuid().ToString("N"));afterSeal.SealForExternalCleanup();File.AppendAllText(afterSeal.SaveTargets[9].CanonicalSavePath,"tamper");AssertPreserved(afterSeal,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,afterSeal.RunId,pid,()=>true));
            var unknown=Fresh(root,saves,Guid.NewGuid().ToString("N"));unknown.SealForExternalCleanup();string extra=unknown.SaveTargets[0].CanonicalSavePath+"-journal";File.WriteAllText(extra,"unknown");AssertPreserved(unknown,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,unknown.RunId,pid,()=>true));Check(File.ReadAllText(extra)=="unknown");
            var badPid=Fresh(root,saves,Guid.NewGuid().ToString("N"));badPid.SealForExternalCleanup();AssertPreserved(badPid,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,badPid.RunId,pid+1,()=>true));
            Expect(()=>L00CCampaignStorage.CreateForRun(root,saves,"..\\escape"));
            RunLegacyRecoveryCases(temp);
            return 0;
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
    private static bool Bytes(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static L00CCampaignStorage Fresh(string root,string saves,string id) { var x=L00CCampaignStorage.CreateForRun(root,saves,id); foreach(L00CCampaignSaveTarget target in x.SaveTargets){x.PrepareNativeCreate(target.Role,target.CanonicalSavePath);WriteNative(target.CanonicalSavePath,target.Role);x.PublishFixtureMarker(target.Role,target.CanonicalSavePath);}x.BeginCycling();return x; }
    private static L00CCampaignStorage AbortAt(string root,string saves,string id,int state,bool rawIntentResidue)
    {
        var x=L00CCampaignStorage.CreateForRun(root,saves,id);
        if(state==0)return x;
        foreach(L00CCampaignSaveTarget target in x.SaveTargets)
        {
            if(state<target.IntentStateSequence)return x;
            x.PrepareNativeCreate(target.Role,target.CanonicalSavePath);
            if(state==target.IntentStateSequence){if(rawIntentResidue)WriteNative(target.CanonicalSavePath,"raw-"+target.Role);return x;}
            WriteNative(target.CanonicalSavePath,target.Role);x.PublishFixtureMarker(target.Role,target.CanonicalSavePath);
        }
        if(state==21)x.BeginCycling();return x;
    }
    private static void WriteNative(string save,string value) { File.WriteAllText(save,value); File.WriteAllText(L00CCampaignStorage.WalPath(save),value+"-wal"); File.WriteAllText(L00CCampaignStorage.ShmPath(save),value+"-shm"); }
    private static string[] ArtifactOrder(L00CCampaignStorage x) => x.SaveTargets.SelectMany(target=>new[]{L00CCampaignStorage.ShmPath(target.CanonicalSavePath),L00CCampaignStorage.WalPath(target.CanonicalSavePath),L00CCampaignStorage.MarkerPath(target.CanonicalSavePath),target.CanonicalSavePath}).ToArray();
    private static bool AllAbsent(string[] paths) { foreach(string path in paths) if(File.Exists(path)) return false; return true; }
    private static string MoveFirstProperty(string json) { int first=json.IndexOf(',',1), second=json.IndexOf(',',first+1); if(first<0||second<0) throw new InvalidOperationException("bad oracle json"); return "{"+json.Substring(first+1,second-first-1)+","+json.Substring(1,first-1)+json.Substring(second); }
    private static string HashText(string value) { using(var h=SHA256.Create()){var b=h.ComputeHash(Encoding.UTF8.GetBytes(value));var s=new StringBuilder(64);foreach(byte x in b)s.Append(x.ToString("X2"));return s.ToString();} }
    private static string HashFile(string path) => HashText(File.ReadAllText(path));
    private static void AssertPathsUnchanged(string[] paths,Action action) { bool[] existed=new bool[paths.Length]; byte[][] before=new byte[paths.Length][]; for(int i=0;i<paths.Length;i++){existed[i]=File.Exists(paths[i]);before[i]=existed[i]?File.ReadAllBytes(paths[i]):Array.Empty<byte>();} Expect(action); for(int i=0;i<paths.Length;i++) Check(existed[i]?File.Exists(paths[i])&&Bytes(before[i],File.ReadAllBytes(paths[i])):!File.Exists(paths[i])); }
    private static void AssertPreserved(L00CCampaignStorage x, Action action) => AssertPathsUnchanged(ArtifactOrder(x),action);
    private static void RunLegacyRecoveryCases(string temp)
    {
        const string id = L00CCampaignStorage.SupportedLegacyRecoveryRunId;
        const int pid = L00CCampaignStorage.SupportedLegacyRecoveryProcessId;
        string dummyHash = HashText("dummy");

        // Neither the generic recovery nor the dedicated compatibility path may
        // infer ownership from a pre-journal filename and provenance alone.
        var absent = Legacy(temp, "absent-authority");
        ExpectLegacyPreserved(absent, () => L00CCampaignStorage.CleanupAfterRuntimeStopped(absent.Root, absent.Saves, id, pid, () => true));
        ExpectLegacyPreserved(absent, () => L00CCampaignStorage.CleanupLegacyPreJournalAfterRuntimeStopped(absent.Root, absent.Saves, id, pid, dummyHash, dummyHash, () => true));
        ExpectLegacyPreserved(absent, () => L00CCampaignStorage.CreateLegacyPreJournalRecoveryManifest(absent.Root, absent.Saves, id, pid, absent.Primary, absent.PrimaryHash, absent.Wal, absent.WalHash, absent.Shm, absent.ShmHash, absent.ProvenanceHash, L00CCampaignStorage.LegacyRecoveryAttestation, () => false));
        ExpectLegacyPreserved(absent, () => L00CCampaignStorage.CreateLegacyPreJournalRecoveryManifest(absent.Root, absent.Saves, id, pid + 1, absent.Primary, absent.PrimaryHash, absent.Wal, absent.WalHash, absent.Shm, absent.ShmHash, absent.ProvenanceHash, L00CCampaignStorage.LegacyRecoveryAttestation, () => true));
        ExpectLegacyPreserved(absent, () => L00CCampaignStorage.CreateLegacyPreJournalRecoveryManifest(absent.Root, absent.Saves, id, pid, absent.Primary, absent.PrimaryHash, absent.Wal, absent.WalHash, absent.Shm, absent.ShmHash, absent.ProvenanceHash, "NOT-ATTESTED", () => true));
        ExpectLegacyPreserved(absent, () => L00CCampaignStorage.CreateLegacyPreJournalRecoveryManifest(absent.Root, absent.Saves, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", pid, absent.Primary, absent.PrimaryHash, absent.Wal, absent.WalHash, absent.Shm, absent.ShmHash, absent.ProvenanceHash, L00CCampaignStorage.LegacyRecoveryAttestation, () => true));
        ExpectLegacyPreserved(absent, () => L00CCampaignStorage.CreateLegacyPreJournalRecoveryManifest(absent.Root, absent.Saves, id, pid, absent.Primary, absent.PrimaryHash, Path.Combine(absent.Saves,"escape.vcdbs-wal"), absent.WalHash, absent.Shm, absent.ShmHash, absent.ProvenanceHash, L00CCampaignStorage.LegacyRecoveryAttestation, () => true));

        // Manifest creation rejects every non-legacy shape before minting any
        // deletion authority.
        var markerAtMint = Legacy(temp, "marker-at-mint"); File.WriteAllText(L00CCampaignStorage.MarkerPath(markerAtMint.Primary), "unknown-marker");
        ExpectLegacyPreserved(markerAtMint, () => Mint(markerAtMint)); Check(!File.Exists(markerAtMint.Manifest));
        var secondaryAtMint = Legacy(temp, "secondary-at-mint"); File.WriteAllText(secondaryAtMint.Secondary, "unknown-secondary");
        ExpectLegacyPreserved(secondaryAtMint, () => Mint(secondaryAtMint)); Check(!File.Exists(secondaryAtMint.Manifest));
        var abortAtMint = Legacy(temp, "abort-at-mint"); Directory.CreateDirectory(Path.Combine(abortAtMint.Campaign, "abort"));
        ExpectLegacyPreserved(abortAtMint, () => Mint(abortAtMint)); Check(!File.Exists(abortAtMint.Manifest));
        var manifestCollision = Legacy(temp, "manifest-collision"); File.WriteAllText(manifestCollision.Manifest, "do-not-overwrite");
        ExpectLegacyPreserved(manifestCollision, () => Mint(manifestCollision)); Check(File.ReadAllText(manifestCollision.Manifest) == "do-not-overwrite");
        var sealCollision = Legacy(temp, "seal-collision"); File.WriteAllText(sealCollision.Seal, "do-not-overwrite");
        ExpectLegacyPreserved(sealCollision, () => Mint(sealCollision)); Check(File.ReadAllText(sealCollision.Seal) == "do-not-overwrite");

        // Exact observed primary db+wal+shm simulation. Only those three files are removed; the
        // unrelated save, provenance, sealed authority and evidence survive.
        var valid = Legacy(temp, "valid"); Mint(valid); byte[] original = File.ReadAllBytes(valid.Primary), originalWal=File.ReadAllBytes(valid.Wal), originalShm=File.ReadAllBytes(valid.Shm);
        L00CCampaignStorage.CleanupLegacyPreJournalAfterRuntimeStopped(valid.Root, valid.Saves, id, pid, valid.ManifestHash, valid.SealHash, () => true);
        Check(!File.Exists(valid.Primary) && !File.Exists(valid.Wal) && !File.Exists(valid.Shm) && File.Exists(valid.Sentinel) && File.Exists(valid.Provenance) && File.Exists(valid.Manifest) && File.Exists(valid.Seal) && Directory.Exists(valid.Evidence) && File.Exists(valid.Intent) && File.Exists(valid.Cleaned));
        File.WriteAllBytes(valid.Primary, original); File.WriteAllBytes(valid.Wal,originalWal); File.WriteAllBytes(valid.Shm,originalShm);
        ExpectLegacyPreserved(valid, () => L00CCampaignStorage.CleanupLegacyPreJournalAfterRuntimeStopped(valid.Root, valid.Saves, id, pid, valid.ManifestHash, valid.SealHash, () => true));

        // All post-seal mismatches preserve the exact primary bytes and every
        // colliding or unexpected entry.
        var live = Legacy(temp, "live"); Mint(live); ExpectLegacyPreserved(live, () => Clean(live, () => false));
        var wrongPid = Legacy(temp, "wrong-pid"); Mint(wrongPid); ExpectLegacyPreserved(wrongPid, () => L00CCampaignStorage.CleanupLegacyPreJournalAfterRuntimeStopped(wrongPid.Root, wrongPid.Saves, id, pid + 1, wrongPid.ManifestHash, wrongPid.SealHash, () => true));
        var marker = Legacy(temp, "marker"); Mint(marker); string markerPath = L00CCampaignStorage.MarkerPath(marker.Primary); File.WriteAllText(markerPath, "late-marker"); ExpectLegacyPreserved(marker, () => Clean(marker)); Check(File.ReadAllText(markerPath) == "late-marker");
        var secondary = Legacy(temp, "secondary"); Mint(secondary); File.WriteAllText(secondary.Secondary, "late-secondary"); ExpectLegacyPreserved(secondary, () => Clean(secondary)); Check(File.ReadAllText(secondary.Secondary) == "late-secondary");
        var owned = Legacy(temp, "owned-extra"); Mint(owned); string ownedExtra = Path.Combine(owned.Saves, "ISRWorldGen-L00C-" + id + "-unknown.vcdbs"); File.WriteAllText(ownedExtra, "late-owned"); ExpectLegacyPreserved(owned, () => Clean(owned)); Check(File.ReadAllText(ownedExtra) == "late-owned");
        var campaignExtra = Legacy(temp, "campaign-extra"); Mint(campaignExtra); string extraReceipt = Path.Combine(campaignExtra.Campaign, "unknown.json"); File.WriteAllText(extraReceipt, "late-campaign"); ExpectLegacyPreserved(campaignExtra, () => Clean(campaignExtra)); Check(File.ReadAllText(extraReceipt) == "late-campaign");
        var manifestTamper = Legacy(temp, "manifest-tamper"); Mint(manifestTamper); File.AppendAllText(manifestTamper.Manifest, " "); ExpectLegacyPreserved(manifestTamper, () => Clean(manifestTamper));
        var sealTamper = Legacy(temp, "seal-tamper"); Mint(sealTamper); File.AppendAllText(sealTamper.Seal, " "); ExpectLegacyPreserved(sealTamper, () => Clean(sealTamper));
        var provenanceTamper = Legacy(temp, "provenance-tamper"); Mint(provenanceTamper); File.AppendAllText(provenanceTamper.Provenance, " "); ExpectLegacyPreserved(provenanceTamper, () => Clean(provenanceTamper));
        var bytesChanged = Legacy(temp, "bytes-changed"); Mint(bytesChanged); File.AppendAllText(bytesChanged.Primary, "changed"); ExpectLegacyPreserved(bytesChanged, () => Clean(bytesChanged));
        var walChanged = Legacy(temp, "wal-changed"); Mint(walChanged); File.AppendAllText(walChanged.Wal, "changed"); ExpectLegacyPreserved(walChanged, () => Clean(walChanged));
        var shmChanged = Legacy(temp, "shm-changed"); Mint(shmChanged); File.AppendAllText(shmChanged.Shm, "changed"); ExpectLegacyPreserved(shmChanged, () => Clean(shmChanged));
        var unknownSidecar = Legacy(temp,"unknown-sidecar"); Mint(unknownSidecar); string journal=unknownSidecar.Primary+"-journal"; File.WriteAllText(journal,"unknown"); ExpectLegacyPreserved(unknownSidecar,()=>Clean(unknownSidecar)); Check(File.ReadAllText(journal)=="unknown");
        var manifestArg = Legacy(temp, "manifest-arg"); Mint(manifestArg); ExpectLegacyPreserved(manifestArg, () => L00CCampaignStorage.CleanupLegacyPreJournalAfterRuntimeStopped(manifestArg.Root, manifestArg.Saves, id, pid, dummyHash, manifestArg.SealHash, () => true));
        var sealArg = Legacy(temp, "seal-arg"); Mint(sealArg); ExpectLegacyPreserved(sealArg, () => L00CCampaignStorage.CleanupLegacyPreJournalAfterRuntimeStopped(sealArg.Root, sealArg.Saves, id, pid, sealArg.ManifestHash, dummyHash, () => true));
        var currentJournal = Legacy(temp, "current-journal"); Mint(currentJournal); Directory.CreateDirectory(Path.Combine(currentJournal.Campaign, "abort")); ExpectLegacyPreserved(currentJournal, () => Clean(currentJournal));

        // Every trust-chain hop and the target identity shares the production
        // reparse guard.  The deterministic seam changes only one attribute.
        foreach (string part in new[]{"lab","campaigns","campaign","provenance","manifest","seal","primary","wal","shm"})
        {
            var x = Legacy(temp, "reparse-" + part); Mint(x);
            string poison = part == "lab" ? x.Root : part == "campaigns" ? Path.Combine(x.Root, "campaigns") : part == "campaign" ? x.Campaign : part == "provenance" ? x.Provenance : part == "manifest" ? x.Manifest : part == "seal" ? x.Seal : part=="wal"?x.Wal:part=="shm"?x.Shm:x.Primary;
            using (L00CCampaignStorage.OverrideAttributeReaderForTests(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(poison), StringComparison.OrdinalIgnoreCase) ? FileAttributes.ReparsePoint : File.GetAttributes(p)))
                ExpectLegacyPreserved(x, () => Clean(x));
        }

        // A stateful process-death proof is re-evaluated at every external
        // boundary.  Becoming live/reused before an effect blocks that effect;
        // already-durable earlier states remain safely resumable or inert.
        var proofManifest = Legacy(temp, "proof-manifest");
        ExpectLegacyPreserved(proofManifest, () => MintWithProof(proofManifest, StatefulProof(true, false)));
        Check(!File.Exists(proofManifest.Manifest) && !File.Exists(proofManifest.Seal));
        var proofSeal = Legacy(temp, "proof-seal");
        ExpectLegacyPreserved(proofSeal, () => MintWithProof(proofSeal, StatefulProof(true, true, false)));
        Check(File.Exists(proofSeal.Manifest) && !File.Exists(proofSeal.Seal));
        var proofIntent = Legacy(temp, "proof-intent"); Mint(proofIntent);
        ExpectLegacyPreserved(proofIntent, () => Clean(proofIntent, StatefulProof(true, false)));
        Check(!File.Exists(proofIntent.Intent));
        var proofDelete = Legacy(temp, "proof-delete"); Mint(proofDelete);
        ExpectLegacyPreserved(proofDelete, () => Clean(proofDelete, StatefulProof(true, true, false)));
        Check(File.Exists(proofDelete.Intent) && !File.Exists(proofDelete.Cleaned));
        Clean(proofDelete); Check(!File.Exists(proofDelete.Primary) && File.Exists(proofDelete.Cleaned));
        var proofCleaned = Legacy(temp, "proof-cleaned"); Mint(proofCleaned);
        Expect(() => Clean(proofCleaned, StatefulProof(true, true, true, true, true, false)));
        Check(!File.Exists(proofCleaned.Primary) && !File.Exists(proofCleaned.Wal) && !File.Exists(proofCleaned.Shm) && File.Exists(proofCleaned.Intent) && !File.Exists(proofCleaned.Cleaned));
        Clean(proofCleaned); Check(File.Exists(proofCleaned.Cleaned));

        // Both cleanup interruption boundaries are resumable only from the
        // immutable intent.  A changed intent is refused byte-for-byte.
        var afterIntent = Legacy(temp, "after-intent"); Mint(afterIntent);
        using (L00CCampaignStorage.OverrideTransitionHookForTests(stage => { if (stage == "legacy-cleanup-intent-written") throw new InvalidOperationException("simulated stop after intent"); })) ExpectLegacyPreserved(afterIntent, () => Clean(afterIntent));
        Clean(afterIntent); Check(!File.Exists(afterIntent.Primary) && File.Exists(afterIntent.Cleaned));
        var tamperedIntent = Legacy(temp, "tampered-intent"); Mint(tamperedIntent);
        using (L00CCampaignStorage.OverrideTransitionHookForTests(stage => { if (stage == "legacy-cleanup-intent-written") throw new InvalidOperationException("simulated stop after intent"); })) ExpectLegacyPreserved(tamperedIntent, () => Clean(tamperedIntent));
        File.AppendAllText(tamperedIntent.Intent, " trailing"); ExpectLegacyPreserved(tamperedIntent, () => Clean(tamperedIntent));
        for(int boundary=0;boundary<3;boundary++)
        {
            var afterDelete = Legacy(temp, "after-delete-"+boundary); Mint(afterDelete); string[] order={afterDelete.Shm,afterDelete.Wal,afterDelete.Primary};
            using (L00CCampaignStorage.OverrideTransitionHookForTests(stage => { if (stage == "legacy-delete-"+boundary) throw new InvalidOperationException("simulated stop after delete"); })) Expect(() => Clean(afterDelete));
            for(int n=0;n<=boundary;n++) Check(!File.Exists(order[n])); for(int n=boundary+1;n<order.Length;n++) Check(File.Exists(order[n]));
            Clean(afterDelete); Check(File.Exists(afterDelete.Cleaned));
        }
        var tamperedAfterDelete = Legacy(temp, "tampered-after-delete"); Mint(tamperedAfterDelete);
        using (L00CCampaignStorage.OverrideTransitionHookForTests(stage => { if (stage == "legacy-delete-2") File.AppendAllText(tamperedAfterDelete.Intent, " trailing"); })) Expect(() => Clean(tamperedAfterDelete));
        Check(!File.Exists(tamperedAfterDelete.Primary) && !File.Exists(tamperedAfterDelete.Wal) && !File.Exists(tamperedAfterDelete.Shm) && File.Exists(tamperedAfterDelete.Intent) && !File.Exists(tamperedAfterDelete.Cleaned));

        // A stop between manifest and seal leaves no cleanup authority and no
        // automatic repair path; all save bytes remain untouched.
        var halfMint = Legacy(temp, "half-mint");
        using (L00CCampaignStorage.OverrideTransitionHookForTests(stage => { if (stage == "legacy-manifest-written") throw new InvalidOperationException("simulated stop after manifest"); })) ExpectLegacyPreserved(halfMint, () => Mint(halfMint));
        Check(File.Exists(halfMint.Manifest) && !File.Exists(halfMint.Seal));
        ExpectLegacyPreserved(halfMint, () => L00CCampaignStorage.CleanupLegacyPreJournalAfterRuntimeStopped(halfMint.Root, halfMint.Saves, id, pid, HashPath(halfMint.Manifest), dummyHash, () => true));
    }
    private static LegacyFixture Legacy(string temp, string name)
    {
        string container = Path.Combine(temp, "legacy-" + name); string root = Path.Combine(container, "repo", ".local", "L00C"); string saves = Path.Combine(container, "fake-GamePaths-Saves");
        string campaign = Path.Combine(root, "campaigns", L00CCampaignStorage.SupportedLegacyRecoveryRunId); string evidence = Path.Combine(campaign, "evidence"); Directory.CreateDirectory(evidence); Directory.CreateDirectory(saves);
        string primary = Path.Combine(saves, "ISRWorldGen-L00C-" + L00CCampaignStorage.SupportedLegacyRecoveryRunId + "-activated-primary.vcdbs");
        string secondary = Path.Combine(saves, "ISRWorldGen-L00C-" + L00CCampaignStorage.SupportedLegacyRecoveryRunId + "-activated-secondary.vcdbs");
        string provenance = Path.Combine(campaign, "campaign-provenance.json");
        WriteNative(primary, "legacy-primary-db-wal-shm-" + name);
        File.WriteAllText(provenance, "{\"schema\":\"l00c-appdata-campaign-v1\",\"runId\":\"" + L00CCampaignStorage.SupportedLegacyRecoveryRunId + "\",\"laboratoryRoot\":\"" + Json(root) + "\",\"gamePathsSaves\":\"" + Json(saves) + "\",\"primarySave\":\"" + Json(primary) + "\",\"secondarySave\":\"" + Json(secondary) + "\",\"processId\":" + L00CCampaignStorage.SupportedLegacyRecoveryProcessId + "}");
        string sentinel = Path.Combine(saves, "unrelated-user-save.vcdbs"); File.WriteAllText(sentinel, "never-delete");
        return new LegacyFixture(root, saves, campaign, evidence, provenance, primary, secondary, sentinel);
    }
    private static void Mint(LegacyFixture x)
    {
        x.SealHash = MintWithProof(x, () => true);
        x.ManifestHash = HashPath(x.Manifest);
    }
    private static string MintWithProof(LegacyFixture x, Func<bool> stopped) => L00CCampaignStorage.CreateLegacyPreJournalRecoveryManifest(x.Root, x.Saves, L00CCampaignStorage.SupportedLegacyRecoveryRunId, L00CCampaignStorage.SupportedLegacyRecoveryProcessId, x.Primary, x.PrimaryHash, x.Wal, x.WalHash, x.Shm, x.ShmHash, x.ProvenanceHash, L00CCampaignStorage.LegacyRecoveryAttestation, stopped);
    private static void Clean(LegacyFixture x, Func<bool>? stopped = null) => L00CCampaignStorage.CleanupLegacyPreJournalAfterRuntimeStopped(x.Root, x.Saves, L00CCampaignStorage.SupportedLegacyRecoveryRunId, L00CCampaignStorage.SupportedLegacyRecoveryProcessId, x.ManifestHash, x.SealHash, stopped ?? (() => true));
    private static Func<bool> StatefulProof(params bool[] values)
    {
        int index = 0;
        return () => values[index < values.Length ? index++ : values.Length - 1];
    }
    private static void ExpectLegacyPreserved(LegacyFixture x, Action action)
    {
        byte[] before = File.ReadAllBytes(x.Primary), wal=File.ReadAllBytes(x.Wal), shm=File.ReadAllBytes(x.Shm), sentinel = File.ReadAllBytes(x.Sentinel); Expect(action);
        Check(File.Exists(x.Primary) && File.Exists(x.Wal) && File.Exists(x.Shm) && Bytes(before, File.ReadAllBytes(x.Primary)) && Bytes(wal,File.ReadAllBytes(x.Wal)) && Bytes(shm,File.ReadAllBytes(x.Shm)) && Bytes(sentinel, File.ReadAllBytes(x.Sentinel)));
    }
    private static string HashPath(string path) { using SHA256 h = SHA256.Create(); using FileStream f = File.OpenRead(path); byte[] b = h.ComputeHash(f); var s = new StringBuilder(64); foreach (byte x in b) s.Append(x.ToString("X2")); return s.ToString(); }
    private static string Json(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private sealed class LegacyFixture
    {
        internal LegacyFixture(string root, string saves, string campaign, string evidence, string provenance, string primary, string secondary, string sentinel)
        {
            Root=root; Saves=saves; Campaign=campaign; Evidence=evidence; Provenance=provenance; Primary=primary; Wal=L00CCampaignStorage.WalPath(primary); Shm=L00CCampaignStorage.ShmPath(primary); Secondary=secondary; Sentinel=sentinel;
            Manifest=Path.Combine(campaign,"legacy-prejournal-recovery-manifest.json"); Seal=Path.Combine(campaign,"legacy-prejournal-recovery-seal.json"); Intent=Path.Combine(campaign,"legacy-prejournal-recovery-intent.json"); Cleaned=Path.Combine(campaign,"legacy-prejournal-recovery-cleaned.json");
            PrimaryHash=HashPath(primary); WalHash=HashPath(Wal); ShmHash=HashPath(Shm); ProvenanceHash=HashPath(provenance); ManifestHash=string.Empty; SealHash=string.Empty;
        }
        internal string Root { get; } internal string Saves { get; } internal string Campaign { get; } internal string Evidence { get; } internal string Provenance { get; }
        internal string Primary { get; } internal string Wal { get; } internal string Shm { get; } internal string Secondary { get; } internal string Sentinel { get; } internal string Manifest { get; } internal string Seal { get; } internal string Intent { get; } internal string Cleaned { get; }
        internal string PrimaryHash { get; } internal string WalHash { get; } internal string ShmHash { get; } internal string ProvenanceHash { get; } internal string ManifestHash { get; set; } internal string SealHash { get; set; }
    }
    private static void Check(bool value, [CallerLineNumber] int line = 0) { if (!value) throw new InvalidOperationException("L00-C AppData storage oracle failed at line " + line + "."); }
    private static void Expect(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("L00-C oracle expected refusal."); }
}
