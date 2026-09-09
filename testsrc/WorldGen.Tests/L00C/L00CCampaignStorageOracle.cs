// Deterministic executable oracle: all filesystem paths are fresh temp paths,
// never AppData and never a Vintage Story process.
#nullable enable
using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

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
            run.PrepareNativeCreate("activated-primary", run.PrimarySavePath); File.WriteAllText(run.PrimarySavePath, "primary"); run.PublishFixtureMarker("activated-primary", run.PrimarySavePath); run.PrepareNativeCreate("activated-secondary", run.SecondarySavePath); File.WriteAllText(run.SecondarySavePath, "secondary"); run.PublishFixtureMarker("activated-secondary", run.SecondarySavePath); run.BeginCycling(); run.SealForExternalCleanup();
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            Expect(() => L00CCampaignStorage.CleanupAfterRuntimeStopped(root, saves, run.RunId, pid, () => false)); Check(File.Exists(run.PrimarySavePath));
            File.AppendAllText(run.PrimarySavePath, "changed"); Expect(() => L00CCampaignStorage.CleanupAfterRuntimeStopped(root, saves, run.RunId, pid, () => true)); Check(File.Exists(run.PrimarySavePath));
            // Cross-run receipt tampering must preserve this run's exact files.
            var tampered = Fresh(root,saves,"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"); tampered.SealForExternalCleanup(); File.WriteAllText(tampered.CleanupReceiptPath, File.ReadAllText(tampered.CleanupReceiptPath).Replace(tampered.RunId, oldId)); Expect(() => L00CCampaignStorage.CleanupAfterRuntimeStopped(root, saves, tampered.RunId, pid, () => true)); Check(File.Exists(tampered.PrimarySavePath));
            var pidCase = Fresh(root,saves,"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"); pidCase.SealForExternalCleanup(); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,pidCase.RunId,0,()=>true)); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,pidCase.RunId,pid+1,()=>true)); foreach(string badPid in new[]{"true","\"text\"","-1"}) { File.WriteAllText(pidCase.CleanupReceiptPath,File.ReadAllText(pidCase.CleanupReceiptPath).Replace("\"runtimeProcessId\":"+pid,"\"runtimeProcessId\":"+badPid)); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,pidCase.RunId,pid,()=>true)); }
            // Restore the sealed content/hash only through a fresh run; the
            // mutated campaign must stay preserved as forensic evidence.
            var clean = Fresh(root,saves,"33333333333333333333333333333333"); clean.SealForExternalCleanup(); L00CCampaignStorage.CleanupAfterRuntimeStopped(root, saves, clean.RunId, pid, () => true); Check(!File.Exists(clean.PrimarySavePath) && !File.Exists(clean.SecondarySavePath)); Check(Bytes(oldBytes, File.ReadAllBytes(oldFile)));
            // Legitimate saves before sealing are accepted; only post-seal bytes
            // are protected by the final cleanup receipt hashes.
            var lifecycle=Fresh(root,saves,"cccccccccccccccccccccccccccccccc"); File.AppendAllText(lifecycle.PrimarySavePath,"-legitimate-save"); File.AppendAllText(lifecycle.SecondarySavePath,"-legitimate-save"); lifecycle.SealForExternalCleanup(); L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,lifecycle.RunId,pid,()=>true); Check(!File.Exists(lifecycle.PrimarySavePath));
            var afterSeal=Fresh(root,saves,"dddddddddddddddddddddddddddddddd"); afterSeal.SealForExternalCleanup(); File.AppendAllText(afterSeal.PrimarySavePath,"-after-seal"); byte[] postSealPrimary=File.ReadAllBytes(afterSeal.PrimarySavePath), postSealSecondary=File.ReadAllBytes(afterSeal.SecondarySavePath); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,afterSeal.RunId,pid,()=>true)); Check(Bytes(postSealPrimary,File.ReadAllBytes(afterSeal.PrimarySavePath))&&Bytes(postSealSecondary,File.ReadAllBytes(afterSeal.SecondarySavePath)));
            string collision = "44444444444444444444444444444444"; string collide = Path.Combine(saves, "ISRWorldGen-L00C-" + collision + "-activated-primary.vcdbs"); File.WriteAllText(collide, "user-like-collision"); Expect(() => L00CCampaignStorage.CreateForRun(root, saves, collision)); Check(File.ReadAllText(collide) == "user-like-collision");
            // Collisions arriving after campaign preparation are checked by the
            // exact production native-create guard for both roles.
            var late = L00CCampaignStorage.CreateForRun(root, saves, "55555555555555555555555555555555"); File.WriteAllText(late.PrimarySavePath,"late-primary"); Expect(()=>late.RequireVacantNativeCreateTarget("activated-primary",late.PrimarySavePath)); Check(File.ReadAllText(late.PrimarySavePath)=="late-primary"); File.WriteAllText(late.SecondarySavePath,"late-secondary"); Expect(()=>late.RequireVacantNativeCreateTarget("activated-secondary",late.SecondarySavePath)); Check(File.ReadAllText(late.SecondarySavePath)=="late-secondary");
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
            // Strict abort-journal and cleanup-intent parsing is independently
            // attacked; each case must refuse before its exact file deletes.
            foreach(string badState in new[]{"{", "{\"schema\":true}", "{\"schema\":\"l00c-appdata-abort-state-v1\",\"schema\":\"l00c-appdata-abort-state-v1\"}", "{} trailing"})
            { var x=AbortAt(root,saves,Guid.NewGuid().ToString("N"),3,true); File.WriteAllText(Path.Combine(x.AbortDirectory,"03-secondary-create-intent.json"),badState); AssertPreserved(x,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true)); }
            foreach(string badIntent in new[]{"{", "{\"schema\":true}", "{} trailing"})
            { var x=AbortAt(root,saves,Guid.NewGuid().ToString("N"),3,true); File.WriteAllText(x.AbortCleanupIntentPath,badIntent); AssertPreserved(x,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,x.RunId,pid,()=>true)); }
            var abortReparse=AbortAt(root,saves,Guid.NewGuid().ToString("N"),3,true); using(L00CCampaignStorage.OverrideAttributeReaderForTests(p=>string.Equals(Path.GetFullPath(p),Path.GetFullPath(abortReparse.AbortDirectory),StringComparison.OrdinalIgnoreCase)?FileAttributes.ReparsePoint:File.GetAttributes(p))) AssertPreserved(abortReparse,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,abortReparse.RunId,pid,()=>true));
            // A crash after the cleanup intent and first marker delete resumes
            // from captured hashes, never from a directory search.
            foreach(int removed in new[]{0,1,2,3}) { var midDelete=AbortAt(root,saves,Guid.NewGuid().ToString("N"),4,false); string intent=AbortIntent(midDelete,pid); File.WriteAllText(midDelete.AbortCleanupIntentPath,intent); string[] ordered={L00CCampaignStorage.MarkerPath(midDelete.PrimarySavePath),midDelete.PrimarySavePath,L00CCampaignStorage.MarkerPath(midDelete.SecondarySavePath),midDelete.SecondarySavePath}; for(int n=0;n<=removed;n++) File.Delete(ordered[n]); L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,midDelete.RunId,pid,()=>true); Check(!File.Exists(midDelete.PrimarySavePath)&&!File.Exists(midDelete.SecondarySavePath)); }
            var outOfOrder=AbortAt(root,saves,Guid.NewGuid().ToString("N"),4,false); File.WriteAllText(outOfOrder.AbortCleanupIntentPath,AbortIntent(outOfOrder,pid)); File.Delete(L00CCampaignStorage.MarkerPath(outOfOrder.SecondarySavePath)); AssertPreserved(outOfOrder,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,outOfOrder.RunId,pid,()=>true));
            var resumeExtra=AbortAt(root,saves,Guid.NewGuid().ToString("N"),4,false); File.WriteAllText(resumeExtra.AbortCleanupIntentPath,AbortIntent(resumeExtra,pid)); File.WriteAllText(Path.Combine(saves,"ISRWorldGen-L00C-"+resumeExtra.RunId+"-EXTRA.vcdbs"),"extra"); AssertPreserved(resumeExtra,()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,resumeExtra.RunId,pid,()=>true));
            return 0;
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
    private static bool Bytes(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static L00CCampaignStorage Fresh(string root,string saves,string id) { var x=L00CCampaignStorage.CreateForRun(root,saves,id); x.PrepareNativeCreate("activated-primary",x.PrimarySavePath); File.WriteAllText(x.PrimarySavePath,"primary"); x.PublishFixtureMarker("activated-primary",x.PrimarySavePath); x.PrepareNativeCreate("activated-secondary",x.SecondarySavePath); File.WriteAllText(x.SecondarySavePath,"secondary"); x.PublishFixtureMarker("activated-secondary",x.SecondarySavePath); x.BeginCycling(); return x; }
    private static L00CCampaignStorage AbortAt(string root,string saves,string id,int state,bool rawIntentResidue)
    {
        var x=L00CCampaignStorage.CreateForRun(root,saves,id);
        if(state==0)return x;
        x.PrepareNativeCreate("activated-primary",x.PrimarySavePath); if(rawIntentResidue) File.WriteAllText(x.PrimarySavePath,"abort-primary");
        if(state==1) return x;
        if(!File.Exists(x.PrimarySavePath)) File.WriteAllText(x.PrimarySavePath,"abort-primary");
        x.PublishFixtureMarker("activated-primary",x.PrimarySavePath);
        if(state==2)return x;
        x.PrepareNativeCreate("activated-secondary",x.SecondarySavePath); if(rawIntentResidue) File.WriteAllText(x.SecondarySavePath,"abort-secondary");
        if(state==3) return x;
        if(!File.Exists(x.SecondarySavePath)) File.WriteAllText(x.SecondarySavePath,"abort-secondary");
        x.PublishFixtureMarker("activated-secondary",x.SecondarySavePath);
        if(state==4)return x;
        x.BeginCycling(); return x;
    }
    private static string HashText(string value) { using(var h=SHA256.Create()){var b=h.ComputeHash(Encoding.UTF8.GetBytes(value));var s=new StringBuilder(64);foreach(byte x in b)s.Append(x.ToString("X2"));return s.ToString();} }
    private static string HashFile(string path) => HashText(File.ReadAllText(path));
    private static void AssertPreserved(L00CCampaignStorage x, Action action) { byte[] p=File.ReadAllBytes(x.PrimarySavePath),s=File.ReadAllBytes(x.SecondarySavePath); Expect(action); Check(Bytes(p,File.ReadAllBytes(x.PrimarySavePath))&&Bytes(s,File.ReadAllBytes(x.SecondarySavePath))); }
    private static void AssertPrimaryPreserved(L00CCampaignStorage x, Action action) { byte[] p=File.ReadAllBytes(x.PrimarySavePath); Expect(action); Check(Bytes(p,File.ReadAllBytes(x.PrimarySavePath))); }
    private static L00CCampaignStorage AbortAtWithIntentMarker(string root,string saves,string id,int state) { var x=AbortAt(root,saves,id,state,true); string save=state==1?x.PrimarySavePath:x.SecondarySavePath; string role=state==1?"activated-primary":"activated-secondary"; WriteMarker(x,role,save); return x; }
    private static void WriteMarker(L00CCampaignStorage x,string role,string save) => File.WriteAllText(L00CCampaignStorage.MarkerPath(save),"{\"schema\":\"l00c-appdata-save-marker-v1\",\"runId\":\""+x.RunId+"\",\"role\":\""+role+"\",\"savePath\":\""+save.Replace("\\","\\\\")+"\",\"provenancePath\":\""+x.ProvenancePath.Replace("\\","\\\\")+"\",\"sha256\":\""+HashFile(save)+"\"}");
    private static string AbortIntent(L00CCampaignStorage x,int pid) => "{\"schema\":\"l00c-appdata-abort-cleanup-intent-v1\",\"runId\":\""+x.RunId+"\",\"runtimeProcessId\":"+pid+",\"state\":\"secondary-created\",\"primarySaveSha256\":\""+HashFile(x.PrimarySavePath)+"\",\"primaryMarkerSha256\":\""+HashFile(L00CCampaignStorage.MarkerPath(x.PrimarySavePath))+"\",\"secondarySaveSha256\":\""+HashFile(x.SecondarySavePath)+"\",\"secondaryMarkerSha256\":\""+HashFile(L00CCampaignStorage.MarkerPath(x.SecondarySavePath))+"\"}";
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("L00-C AppData storage oracle failed."); }
    private static void Expect(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("L00-C oracle expected refusal."); }
}
