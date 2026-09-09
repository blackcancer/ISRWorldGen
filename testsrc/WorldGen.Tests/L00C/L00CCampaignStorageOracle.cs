// Deterministic executable oracle: all filesystem paths are fresh temp paths,
// never AppData and never a Vintage Story process.
#nullable enable
using System;
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
            string oldId = "11111111111111111111111111111111";
            string oldCampaign = Path.Combine(root, "campaigns", oldId); Directory.CreateDirectory(oldCampaign);
            byte[] oldBytes = Encoding.UTF8.GetBytes("prior-campaign-untouched"); string oldFile = Path.Combine(saves, "ISRWorldGen-L00C-" + oldId + "-activated-primary.vcdbs"); File.WriteAllBytes(oldFile, oldBytes);
            var run = L00CCampaignStorage.CreateForRun(root, saves, "22222222222222222222222222222222");
            Check(Bytes(oldBytes, File.ReadAllBytes(oldFile))); Check(Path.GetDirectoryName(run.PrimarySavePath) == saves); Check(L00CCampaignStorage.IsCampaignSavePath(root, saves, run.PrimarySavePath));
            Check(L00CCampaignStorage.WalPath(run.PrimarySavePath).EndsWith(".vcdbs-wal",StringComparison.Ordinal));
            Check(L00CCampaignStorage.ShmPath(run.PrimarySavePath).EndsWith(".vcdbs-shm",StringComparison.Ordinal));
            run.PrepareNativeCreate("activated-primary", run.PrimarySavePath); WriteNative(run.PrimarySavePath,"primary"); run.PublishFixtureMarker("activated-primary", run.PrimarySavePath); run.PrepareNativeCreate("activated-secondary", run.SecondarySavePath); WriteNative(run.SecondarySavePath,"secondary"); run.PublishFixtureMarker("activated-secondary", run.SecondarySavePath); run.BeginCycling(); run.SealForExternalCleanup();
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            Expect(() => L00CCampaignStorage.CleanupAfterRuntimeStopped(root, saves, run.RunId, pid, () => false)); Check(File.Exists(run.PrimarySavePath));
            File.AppendAllText(run.PrimarySavePath, "changed"); Expect(() => L00CCampaignStorage.CleanupAfterRuntimeStopped(root, saves, run.RunId, pid, () => true)); Check(File.Exists(run.PrimarySavePath));
            // Cross-run receipt tampering must preserve this run's exact files.
            var tampered = Fresh(root,saves,"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"); tampered.SealForExternalCleanup(); File.WriteAllText(tampered.CleanupReceiptPath, File.ReadAllText(tampered.CleanupReceiptPath).Replace(tampered.RunId, oldId)); Expect(() => L00CCampaignStorage.CleanupAfterRuntimeStopped(root, saves, tampered.RunId, pid, () => true)); Check(File.Exists(tampered.PrimarySavePath));
            var pidCase = Fresh(root,saves,"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"); pidCase.SealForExternalCleanup(); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,pidCase.RunId,0,()=>true)); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,pidCase.RunId,pid+1,()=>true)); foreach(string badPid in new[]{"true","\"text\"","-1"}) { File.WriteAllText(pidCase.CleanupReceiptPath,File.ReadAllText(pidCase.CleanupReceiptPath).Replace("\"runtimeProcessId\":"+pid,"\"runtimeProcessId\":"+badPid)); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,pidCase.RunId,pid,()=>true)); }
            // Restore the sealed content/hash only through a fresh run; the
            // mutated campaign must stay preserved as forensic evidence.
            var clean = Fresh(root,saves,"33333333333333333333333333333333"); clean.SealForExternalCleanup(); L00CCampaignStorage.CleanupAfterRuntimeStopped(root, saves, clean.RunId, pid, () => true); Check(!File.Exists(clean.PrimarySavePath) && !File.Exists(clean.SecondarySavePath)); Check(Bytes(oldBytes, File.ReadAllBytes(oldFile)));
            // Sealed v2 cleanup covers both roles and every db/wal/shm/marker
            // boundary. Each interrupted prefix resumes from the durable intent.
            for(int boundary=0;boundary<8;boundary++) { var x=Fresh(root,saves,Guid.NewGuid().ToString("N")); x.SealForExternalCleanup(); using(L00CCampaignStorage.OverrideTransitionHookForTests(stage=>{if(stage=="sealed-delete-"+boundary) throw new InvalidOperationException("stop");})) Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true)); string[] order=ArtifactOrder(x); for(int n=0;n<=boundary;n++) Check(!File.Exists(order[n])); for(int n=boundary+1;n<order.Length;n++) Check(File.Exists(order[n])); L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true); Check(AllAbsent(order)); }
            var sealedWalTamper=Fresh(root,saves,Guid.NewGuid().ToString("N")); sealedWalTamper.SealForExternalCleanup(); File.AppendAllText(L00CCampaignStorage.WalPath(sealedWalTamper.PrimarySavePath),"tamper"); AssertPreserved(sealedWalTamper,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,sealedWalTamper.RunId,pid,()=>true));
            var sealedUnknown=Fresh(root,saves,Guid.NewGuid().ToString("N")); sealedUnknown.SealForExternalCleanup(); string sealedJournal=sealedUnknown.PrimarySavePath+"-journal"; File.WriteAllText(sealedJournal,"unknown"); AssertPreserved(sealedUnknown,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,sealedUnknown.RunId,pid,()=>true)); Check(File.ReadAllText(sealedJournal)=="unknown");
            var sealedCleanedDirectory=Fresh(root,saves,Guid.NewGuid().ToString("N")); sealedCleanedDirectory.SealForExternalCleanup(); Directory.CreateDirectory(sealedCleanedDirectory.SealedCleanedPath); AssertPreserved(sealedCleanedDirectory,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,sealedCleanedDirectory.RunId,pid,()=>true)); Check(Directory.Exists(sealedCleanedDirectory.SealedCleanedPath));
            // Legitimate saves before sealing are accepted; only post-seal bytes
            // are protected by the final cleanup receipt hashes.
            var lifecycle=Fresh(root,saves,"cccccccccccccccccccccccccccccccc"); File.AppendAllText(lifecycle.PrimarySavePath,"-legitimate-save"); File.AppendAllText(lifecycle.SecondarySavePath,"-legitimate-save"); lifecycle.SealForExternalCleanup(); L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,lifecycle.RunId,pid,()=>true); Check(!File.Exists(lifecycle.PrimarySavePath));
            var afterSeal=Fresh(root,saves,"dddddddddddddddddddddddddddddddd"); afterSeal.SealForExternalCleanup(); File.AppendAllText(afterSeal.PrimarySavePath,"-after-seal"); byte[] postSealPrimary=File.ReadAllBytes(afterSeal.PrimarySavePath), postSealSecondary=File.ReadAllBytes(afterSeal.SecondarySavePath); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,afterSeal.RunId,pid,()=>true)); Check(Bytes(postSealPrimary,File.ReadAllBytes(afterSeal.PrimarySavePath))&&Bytes(postSealSecondary,File.ReadAllBytes(afterSeal.SecondarySavePath)));
            string collision = "44444444444444444444444444444444"; string collide = Path.Combine(saves, "ISRWorldGen-L00C-" + collision + "-activated-primary.vcdbs"); File.WriteAllText(collide, "user-like-collision"); Expect(() => L00CCampaignStorage.CreateForRun(root, saves, collision)); Check(File.ReadAllText(collide) == "user-like-collision");
            foreach(string suffix in new[]{"-wal","-shm"}) { string id=Guid.NewGuid().ToString("N"); string db=Path.Combine(saves,"ISRWorldGen-L00C-"+id+"-activated-primary.vcdbs"); string sidecar=db+suffix; File.WriteAllText(sidecar,"sidecar-collision"); Expect(()=>L00CCampaignStorage.CreateForRun(root,saves,id)); Check(File.ReadAllText(sidecar)=="sidecar-collision"); }
            // Collisions arriving after campaign preparation are checked by the
            // exact production native-create guard for both roles.
            var late = L00CCampaignStorage.CreateForRun(root, saves, "55555555555555555555555555555555"); File.WriteAllText(L00CCampaignStorage.WalPath(late.PrimarySavePath),"late-primary-wal"); Expect(()=>late.RequireVacantNativeCreateTarget("activated-primary",late.PrimarySavePath)); Check(File.ReadAllText(L00CCampaignStorage.WalPath(late.PrimarySavePath))=="late-primary-wal"); File.WriteAllText(L00CCampaignStorage.ShmPath(late.SecondarySavePath),"late-secondary-shm"); Expect(()=>late.RequireVacantNativeCreateTarget("activated-secondary",late.SecondarySavePath)); Check(File.ReadAllText(L00CCampaignStorage.ShmPath(late.SecondarySavePath))=="late-secondary-shm");
            // A collision after the first vacancy check but before native create
            // is recorded as refused, never promoted to cleanup ownership.
            var refused=L00CCampaignStorage.CreateForRun(root,saves,Guid.NewGuid().ToString("N")); refused.RequireVacantNativeCreateTarget("activated-primary",refused.PrimarySavePath); refused.PrepareNativeCreate("activated-primary",refused.PrimarySavePath); File.WriteAllText(refused.PrimarySavePath,"user-race"); Expect(()=>refused.RequireVacantNativeCreateTarget("activated-primary",refused.PrimarySavePath)); refused.RecordNativeCreateRefusal("activated-primary",refused.PrimarySavePath); AssertPrimaryPreserved(refused,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,refused.RunId,pid,()=>true));
            Check(!L00CCampaignStorage.IsCampaignSavePath(root,saves,Path.ChangeExtension(run.PrimarySavePath,".txt")));
            // Same production attribute guard, with a deterministic Debug seam:
            // lab, campaigns and run reparse points must all refuse discovery.
            foreach(string poison in new[]{root,Path.Combine(root,"campaigns"),run.CampaignRoot}) { using(L00CCampaignStorage.OverrideAttributeReaderForTests(p=>string.Equals(Path.GetFullPath(p),Path.GetFullPath(poison),StringComparison.OrdinalIgnoreCase)?FileAttributes.ReparsePoint:File.GetAttributes(p))) { Check(!L00CCampaignStorage.IsCampaignSavePath(root,saves,run.PrimarySavePath)); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,run.RunId,pid,()=>true)); } }
            // Every malformed marker variant goes through production Seal/RequireAttested.
            Expect(() => L00CCampaignStorage.CreateForRun(root, saves, "..\\escape")); Check(!L00CCampaignStorage.IsCampaignSavePath(root, saves, Path.Combine(root, "saves", "escape.vcdbs")));
            // R6 isolation: each adversarial entry starts from a fresh valid
            // campaign, changes exactly one evidence artifact, then invokes the
            // relevant production path and proves both files remain present.
            string[] markerFields = { "\"schema\":\"l00c-appdata-save-marker-v1\"", "\"sha256\":\""+HashText("primary")+"\"", "\"sha256\":\""+HashText("primary")+"\"", "\"sha256\":\""+HashText("primary")+"\"", "\"sha256\":\""+HashText("primary")+"\"", "\"sha256\":\""+HashText("primary")+"\"" };
            string[] markerMutations = { "\"schema\":true", "\"sha256\":\"BAD\"", "\"sha256\":false", "", "\"sha256\":\""+HashText("primary")+"\",\"sha256\":\""+HashText("primary")+"\"", "\"sha256\":\""+HashText("primary")+"\"} trailing" };
            for(int n=0;n<markerMutations.Length;n++) { var x=Fresh(root,saves,(600+n).ToString("x32")); string marker=File.ReadAllText(L00CCampaignStorage.MarkerPath(x.PrimarySavePath)); string changed=markerMutations[n]==""?marker.Replace(","+markerFields[n],""):marker.Replace(markerFields[n],markerMutations[n]); File.WriteAllText(L00CCampaignStorage.MarkerPath(x.PrimarySavePath),changed); AssertPreserved(x,()=>x.SealForExternalCleanup()); Check(!File.Exists(x.CleanupReceiptPath)); }
            var absentMarker=Fresh(root,saves,"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"); File.Delete(L00CCampaignStorage.MarkerPath(absentMarker.PrimarySavePath)); AssertPreserved(absentMarker,()=>absentMarker.SealForExternalCleanup()); Check(!File.Exists(absentMarker.CleanupReceiptPath));
            string[] pidAttacks = { "0", "-1", "\"text\"", "true", (pid+1).ToString() };
            for(int n=0;n<pidAttacks.Length;n++) { var x=Fresh(root,saves,(700+n).ToString("x32")); x.SealForExternalCleanup(); string receipt=File.ReadAllText(x.CleanupReceiptPath); File.WriteAllText(x.CleanupReceiptPath,receipt.Replace("\"runtimeProcessId\":"+pid,"\"runtimeProcessId\":"+pidAttacks[n])); AssertPreserved(x,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true)); }
            // One valid clean run per seam target proves the only changed input is
            // the simulated reparse attribute for lab/campaigns/run/receipt.
            foreach(string part in new[]{"lab","campaigns","run","receipt","provenance"}) { var x=Fresh(root,saves,Guid.NewGuid().ToString("N")); x.SealForExternalCleanup(); string poison=part=="lab"?root:part=="campaigns"?Path.Combine(root,"campaigns"):part=="run"?x.CampaignRoot:part=="receipt"?x.CleanupReceiptPath:x.ProvenancePath; using(L00CCampaignStorage.OverrideAttributeReaderForTests(p=>string.Equals(Path.GetFullPath(p),Path.GetFullPath(poison),StringComparison.OrdinalIgnoreCase)?FileAttributes.ReparsePoint:File.GetAttributes(p))) { AssertPreserved(x,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true)); Check(!L00CCampaignStorage.IsCampaignSavePath(root,saves,x.PrimarySavePath)); } }
            // Durable pre-seal state machine: exercise an interruption on both
            // sides of every state boundary. State 1 with a raw primary is the
            // observed early-stop shape (native write happened after its intent,
            // before marker publication).
            for(int state=0;state<=5;state++)
            {
                foreach(bool afterExternalWrite in new[]{false,true})
                {
                    var x=AbortAt(root,saves,(800+state*2+(afterExternalWrite?1:0)).ToString("x32"),state,afterExternalWrite);
                    L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true);
                    Check(!File.Exists(x.PrimarySavePath)&&!File.Exists(x.SecondarySavePath)&&!File.Exists(L00CCampaignStorage.MarkerPath(x.PrimarySavePath))&&!File.Exists(L00CCampaignStorage.MarkerPath(x.SecondarySavePath)));
                }
            }
            foreach(int nativeCount in new[]{1,2,3}) { var x=L00CCampaignStorage.CreateForRun(root,saves,Guid.NewGuid().ToString("N")); x.PrepareNativeCreate("activated-primary",x.PrimarySavePath); File.WriteAllText(x.PrimarySavePath,"db"); if(nativeCount>=2) File.WriteAllText(L00CCampaignStorage.WalPath(x.PrimarySavePath),"wal"); if(nativeCount>=3) File.WriteAllText(L00CCampaignStorage.ShmPath(x.PrimarySavePath),"shm"); L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true); Check(!File.Exists(x.PrimarySavePath)&&!File.Exists(L00CCampaignStorage.WalPath(x.PrimarySavePath))&&!File.Exists(L00CCampaignStorage.ShmPath(x.PrimarySavePath))); }
            var invalidNative=L00CCampaignStorage.CreateForRun(root,saves,Guid.NewGuid().ToString("N")); invalidNative.PrepareNativeCreate("activated-primary",invalidNative.PrimarySavePath); File.WriteAllText(invalidNative.PrimarySavePath,"db"); File.WriteAllText(L00CCampaignStorage.ShmPath(invalidNative.PrimarySavePath),"shm-without-wal"); AssertPrimaryPreserved(invalidNative,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,invalidNative.RunId,pid,()=>true));
            // Intent boundaries explicitly cover no native effect, raw save,
            // and marker publication before its durable `created` receipt.
            foreach(int state in new[]{1,3}) { var x=AbortAtWithIntentMarker(root,saves,Guid.NewGuid().ToString("N"),state); L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true); Check(!File.Exists(x.PrimarySavePath)&&!File.Exists(x.SecondarySavePath)); }
            // Every abort refusal below begins with a fresh isolated residue and
            // asserts its original bytes survive unchanged.
            foreach(string mutation in new[]{"\"processId\":0","\"processId\":true","\"runId\":\"foreign\"","\"primarySave\":\"C:\\\\foreign.vcdbs\""})
            {
                var x=AbortAt(root,saves,Guid.NewGuid().ToString("N"),3,true); byte[] p=File.ReadAllBytes(x.PrimarySavePath),s=File.ReadAllBytes(x.SecondarySavePath);
                string provenance=File.ReadAllText(x.ProvenancePath); string key=mutation.Substring(0,mutation.IndexOf(':')); int start=provenance.IndexOf(key,StringComparison.Ordinal); int end=provenance.IndexOf(',',start); if(end<0) end=provenance.IndexOf('}',start); File.WriteAllText(x.ProvenancePath,provenance.Substring(0,start)+mutation+provenance.Substring(end));
                Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true)); Check(Bytes(p,File.ReadAllBytes(x.PrimarySavePath))&&Bytes(s,File.ReadAllBytes(x.SecondarySavePath)));
            }
            var live=AbortAt(root,saves,Guid.NewGuid().ToString("N"),3,true); AssertPreserved(live,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,live.RunId,pid,()=>false));
            var wrongPid=AbortAt(root,saves,Guid.NewGuid().ToString("N"),3,true); AssertPreserved(wrongPid,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,wrongPid.RunId,pid+1,()=>true));
            var extra=AbortAt(root,saves,Guid.NewGuid().ToString("N"),3,true); string unexpected=Path.Combine(saves,"ISRWorldGen-L00C-"+extra.RunId+"-unexpected.vcdbs"); File.WriteAllText(unexpected,"do-not-delete"); AssertPreserved(extra,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,extra.RunId,pid,()=>true)); Check(File.ReadAllText(unexpected)=="do-not-delete");
            foreach(string suffix in new[]{"-journal","-wal.extra","-shm.extra"}) { var x=AbortAt(root,saves,Guid.NewGuid().ToString("N"),4,false); string unknown=x.PrimarySavePath+suffix; File.WriteAllText(unknown,"unknown-sidecar"); AssertPreserved(x,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true)); Check(File.ReadAllText(unknown)=="unknown-sidecar"); }
            // Strict abort-journal and cleanup-intent parsing is independently
            // attacked; each case must refuse before its exact file deletes.
            foreach(string badState in new[]{"{", "{\"schema\":true}", "{\"schema\":\"l00c-appdata-abort-state-v1\",\"schema\":\"l00c-appdata-abort-state-v1\"}", "{} trailing"})
            { var x=AbortAt(root,saves,Guid.NewGuid().ToString("N"),3,true); File.WriteAllText(Path.Combine(x.AbortDirectory,"03-secondary-create-intent.json"),badState); AssertPreserved(x,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true)); }
            foreach(string badIntent in new[]{"{", "{\"schema\":true}", "{} trailing"})
            { var x=AbortAt(root,saves,Guid.NewGuid().ToString("N"),3,true); File.WriteAllText(x.AbortCleanupIntentPath,badIntent); AssertPreserved(x,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true)); }
            var abortCleanedDirectory=AbortAt(root,saves,Guid.NewGuid().ToString("N"),4,false); Directory.CreateDirectory(abortCleanedDirectory.AbortCleanedPath); AssertPreserved(abortCleanedDirectory,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,abortCleanedDirectory.RunId,pid,()=>true)); Check(Directory.Exists(abortCleanedDirectory.AbortCleanedPath));
            var tamperedAbortIntent=AbortAt(root,saves,Guid.NewGuid().ToString("N"),4,false); using(L00CCampaignStorage.OverrideTransitionHookForTests(stage=>{if(stage=="abort-cleanup-intent-written") throw new InvalidOperationException("stop");})) Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,tamperedAbortIntent.RunId,pid,()=>true)); File.WriteAllText(tamperedAbortIntent.AbortCleanupIntentPath," "+File.ReadAllText(tamperedAbortIntent.AbortCleanupIntentPath)); AssertPreserved(tamperedAbortIntent,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,tamperedAbortIntent.RunId,pid,()=>true));
            var abortReparse=AbortAt(root,saves,Guid.NewGuid().ToString("N"),3,true); using(L00CCampaignStorage.OverrideAttributeReaderForTests(p=>string.Equals(Path.GetFullPath(p),Path.GetFullPath(abortReparse.AbortDirectory),StringComparison.OrdinalIgnoreCase)?FileAttributes.ReparsePoint:File.GetAttributes(p))) AssertPreserved(abortReparse,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,abortReparse.RunId,pid,()=>true));
            // A crash after the cleanup intent and first marker delete resumes
            // from captured hashes, never from a directory search.
            foreach(int removed in new[]{0,1,2,3,4,5,6,7}) { var midDelete=AbortAt(root,saves,Guid.NewGuid().ToString("N"),4,false); string intent=AbortIntent(midDelete,pid); File.WriteAllText(midDelete.AbortCleanupIntentPath,intent); string[] ordered=ArtifactOrder(midDelete); for(int n=0;n<=removed;n++) File.Delete(ordered[n]); L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,midDelete.RunId,pid,()=>true); Check(AllAbsent(ordered)); }
            var outOfOrder=AbortAt(root,saves,Guid.NewGuid().ToString("N"),4,false); File.WriteAllText(outOfOrder.AbortCleanupIntentPath,AbortIntent(outOfOrder,pid)); File.Delete(L00CCampaignStorage.WalPath(outOfOrder.SecondarySavePath)); AssertPreserved(outOfOrder,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,outOfOrder.RunId,pid,()=>true));
            // Stop after every ordered abort deletion and resume through the
            // immutable v2 intent. No later artifact may be deleted first.
            for(int boundary=0;boundary<8;boundary++) { var x=AbortAt(root,saves,Guid.NewGuid().ToString("N"),4,false); using(L00CCampaignStorage.OverrideTransitionHookForTests(stage=>{if(stage=="abort-delete-"+boundary) throw new InvalidOperationException("stop");})) Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true)); string[] order=ArtifactOrder(x); for(int n=0;n<=boundary;n++) Check(!File.Exists(order[n])); for(int n=boundary+1;n<order.Length;n++) Check(File.Exists(order[n])); L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true); Check(AllAbsent(order)); }
            for(int boundary=0;boundary<8;boundary++) { var x=AbortAt(root,saves,Guid.NewGuid().ToString("N"),4,false); using(L00CCampaignStorage.OverrideTransitionHookForTests(stage=>{if(stage=="abort-delete-"+boundary) throw new InvalidOperationException("stop");})) Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true)); string intent=File.ReadAllText(x.AbortCleanupIntentPath); File.WriteAllText(x.AbortCleanupIntentPath,boundary%2==0?"\r\n"+intent:MoveFirstProperty(intent)); AssertPathsUnchanged(ArtifactOrder(x),()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true)); }
            var resumeExtra=AbortAt(root,saves,Guid.NewGuid().ToString("N"),4,false); File.WriteAllText(resumeExtra.AbortCleanupIntentPath,AbortIntent(resumeExtra,pid)); File.WriteAllText(Path.Combine(saves,"ISRWorldGen-L00C-"+resumeExtra.RunId+"-EXTRA.vcdbs"),"extra"); AssertPreserved(resumeExtra,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,resumeExtra.RunId,pid,()=>true));
            RunLegacyRecoveryCases(temp);
            return 0;
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
    private static bool Bytes(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static L00CCampaignStorage Fresh(string root,string saves,string id) { var x=L00CCampaignStorage.CreateForRun(root,saves,id); x.PrepareNativeCreate("activated-primary",x.PrimarySavePath); WriteNative(x.PrimarySavePath,"primary"); x.PublishFixtureMarker("activated-primary",x.PrimarySavePath); x.PrepareNativeCreate("activated-secondary",x.SecondarySavePath); WriteNative(x.SecondarySavePath,"secondary"); x.PublishFixtureMarker("activated-secondary",x.SecondarySavePath); x.BeginCycling(); return x; }
    private static L00CCampaignStorage AbortAt(string root,string saves,string id,int state,bool rawIntentResidue)
    {
        var x=L00CCampaignStorage.CreateForRun(root,saves,id);
        if(state==0)return x;
        x.PrepareNativeCreate("activated-primary",x.PrimarySavePath); if(rawIntentResidue) WriteNative(x.PrimarySavePath,"abort-primary");
        if(state==1) return x;
        if(!File.Exists(x.PrimarySavePath)) WriteNative(x.PrimarySavePath,"abort-primary");
        x.PublishFixtureMarker("activated-primary",x.PrimarySavePath);
        if(state==2)return x;
        x.PrepareNativeCreate("activated-secondary",x.SecondarySavePath); if(rawIntentResidue) WriteNative(x.SecondarySavePath,"abort-secondary");
        if(state==3) return x;
        if(!File.Exists(x.SecondarySavePath)) WriteNative(x.SecondarySavePath,"abort-secondary");
        x.PublishFixtureMarker("activated-secondary",x.SecondarySavePath);
        if(state==4)return x;
        x.BeginCycling(); return x;
    }
    private static void WriteNative(string save,string value) { File.WriteAllText(save,value); File.WriteAllText(L00CCampaignStorage.WalPath(save),value+"-wal"); File.WriteAllText(L00CCampaignStorage.ShmPath(save),value+"-shm"); }
    private static string[] ArtifactOrder(L00CCampaignStorage x) => new[]{L00CCampaignStorage.ShmPath(x.PrimarySavePath),L00CCampaignStorage.WalPath(x.PrimarySavePath),L00CCampaignStorage.MarkerPath(x.PrimarySavePath),x.PrimarySavePath,L00CCampaignStorage.ShmPath(x.SecondarySavePath),L00CCampaignStorage.WalPath(x.SecondarySavePath),L00CCampaignStorage.MarkerPath(x.SecondarySavePath),x.SecondarySavePath};
    private static bool AllAbsent(string[] paths) { foreach(string path in paths) if(File.Exists(path)) return false; return true; }
    private static string MoveFirstProperty(string json) { int first=json.IndexOf(',',1), second=json.IndexOf(',',first+1); if(first<0||second<0) throw new InvalidOperationException("bad oracle json"); return "{"+json.Substring(first+1,second-first-1)+","+json.Substring(1,first-1)+json.Substring(second); }
    private static string HashText(string value) { using(var h=SHA256.Create()){var b=h.ComputeHash(Encoding.UTF8.GetBytes(value));var s=new StringBuilder(64);foreach(byte x in b)s.Append(x.ToString("X2"));return s.ToString();} }
    private static string HashFile(string path) => HashText(File.ReadAllText(path));
    private static void AssertPathsUnchanged(string[] paths,Action action) { bool[] existed=new bool[paths.Length]; byte[][] before=new byte[paths.Length][]; for(int i=0;i<paths.Length;i++){existed[i]=File.Exists(paths[i]);before[i]=existed[i]?File.ReadAllBytes(paths[i]):Array.Empty<byte>();} Expect(action); for(int i=0;i<paths.Length;i++) Check(existed[i]?File.Exists(paths[i])&&Bytes(before[i],File.ReadAllBytes(paths[i])):!File.Exists(paths[i])); }
    private static void AssertPreserved(L00CCampaignStorage x, Action action) => AssertPathsUnchanged(ArtifactOrder(x),action);
    private static void AssertPrimaryPreserved(L00CCampaignStorage x, Action action) => AssertPathsUnchanged(new[]{L00CCampaignStorage.ShmPath(x.PrimarySavePath),L00CCampaignStorage.WalPath(x.PrimarySavePath),L00CCampaignStorage.MarkerPath(x.PrimarySavePath),x.PrimarySavePath},action);
    private static L00CCampaignStorage AbortAtWithIntentMarker(string root,string saves,string id,int state) { var x=AbortAt(root,saves,id,state,true); string save=state==1?x.PrimarySavePath:x.SecondarySavePath; string role=state==1?"activated-primary":"activated-secondary"; WriteMarker(x,role,save); return x; }
    private static void WriteMarker(L00CCampaignStorage x,string role,string save) => File.WriteAllText(L00CCampaignStorage.MarkerPath(save),"{\"schema\":\"l00c-appdata-save-marker-v1\",\"runId\":\""+x.RunId+"\",\"role\":\""+role+"\",\"savePath\":\""+save.Replace("\\","\\\\")+"\",\"provenancePath\":\""+x.ProvenancePath.Replace("\\","\\\\")+"\",\"sha256\":\""+HashFile(save)+"\"}");
    private static string AbortIntent(L00CCampaignStorage x,int pid) => "{\"schema\":\"l00c-appdata-abort-cleanup-intent-v2\",\"runId\":\""+x.RunId+"\",\"runtimeProcessId\":"+pid+",\"state\":\"secondary-created\",\"primaryShmSha256\":\""+HashFile(L00CCampaignStorage.ShmPath(x.PrimarySavePath))+"\",\"primaryWalSha256\":\""+HashFile(L00CCampaignStorage.WalPath(x.PrimarySavePath))+"\",\"primaryMarkerSha256\":\""+HashFile(L00CCampaignStorage.MarkerPath(x.PrimarySavePath))+"\",\"primarySaveSha256\":\""+HashFile(x.PrimarySavePath)+"\",\"secondaryShmSha256\":\""+HashFile(L00CCampaignStorage.ShmPath(x.SecondarySavePath))+"\",\"secondaryWalSha256\":\""+HashFile(L00CCampaignStorage.WalPath(x.SecondarySavePath))+"\",\"secondaryMarkerSha256\":\""+HashFile(L00CCampaignStorage.MarkerPath(x.SecondarySavePath))+"\",\"secondarySaveSha256\":\""+HashFile(x.SecondarySavePath)+"\"}";
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
