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
            File.WriteAllText(run.PrimarySavePath, "primary"); File.WriteAllText(run.SecondarySavePath, "secondary"); run.PublishFixtureMarker("activated-primary", run.PrimarySavePath); run.PublishFixtureMarker("activated-secondary", run.SecondarySavePath); run.SealForExternalCleanup();
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            Expect(() => L00CCampaignStorage.CleanupAfterRuntimeStopped(root, saves, run.RunId, pid, () => false)); Check(File.Exists(run.PrimarySavePath));
            File.AppendAllText(run.PrimarySavePath, "changed"); Expect(() => L00CCampaignStorage.CleanupAfterRuntimeStopped(root, saves, run.RunId, pid, () => true)); Check(File.Exists(run.PrimarySavePath));
            // Cross-run receipt tampering must preserve this run's exact files.
            var tampered = L00CCampaignStorage.CreateForRun(root, saves, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"); File.WriteAllText(tampered.PrimarySavePath, "p"); File.WriteAllText(tampered.SecondarySavePath, "s"); tampered.PublishFixtureMarker("activated-primary", tampered.PrimarySavePath); tampered.PublishFixtureMarker("activated-secondary", tampered.SecondarySavePath); tampered.SealForExternalCleanup(); File.WriteAllText(tampered.CleanupReceiptPath, File.ReadAllText(tampered.CleanupReceiptPath).Replace(tampered.RunId, oldId)); Expect(() => L00CCampaignStorage.CleanupAfterRuntimeStopped(root, saves, tampered.RunId, pid, () => true)); Check(File.Exists(tampered.PrimarySavePath));
            var pidCase = L00CCampaignStorage.CreateForRun(root,saves,"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"); File.WriteAllText(pidCase.PrimarySavePath,"p"); File.WriteAllText(pidCase.SecondarySavePath,"s"); pidCase.PublishFixtureMarker("activated-primary",pidCase.PrimarySavePath); pidCase.PublishFixtureMarker("activated-secondary",pidCase.SecondarySavePath); pidCase.SealForExternalCleanup(); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,pidCase.RunId,0,()=>true)); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,pidCase.RunId,pid+1,()=>true)); foreach(string badPid in new[]{"true","\"text\"","-1"}) { File.WriteAllText(pidCase.CleanupReceiptPath,File.ReadAllText(pidCase.CleanupReceiptPath).Replace("\"runtimeProcessId\":"+pid,"\"runtimeProcessId\":"+badPid)); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,pidCase.RunId,pid,()=>true)); }
            // Restore the sealed content/hash only through a fresh run; the
            // mutated campaign must stay preserved as forensic evidence.
            var clean = L00CCampaignStorage.CreateForRun(root, saves, "33333333333333333333333333333333"); File.WriteAllText(clean.PrimarySavePath, "p"); File.WriteAllText(clean.SecondarySavePath, "s"); clean.PublishFixtureMarker("activated-primary", clean.PrimarySavePath); clean.PublishFixtureMarker("activated-secondary", clean.SecondarySavePath); clean.SealForExternalCleanup(); L00CCampaignStorage.CleanupAfterRuntimeStopped(root, saves, clean.RunId, pid, () => true); Check(!File.Exists(clean.PrimarySavePath) && !File.Exists(clean.SecondarySavePath)); Check(Bytes(oldBytes, File.ReadAllBytes(oldFile)));
            // Legitimate saves before sealing are accepted; only post-seal bytes
            // are protected by the final cleanup receipt hashes.
            var lifecycle=Fresh(root,saves,"cccccccccccccccccccccccccccccccc"); File.AppendAllText(lifecycle.PrimarySavePath,"-legitimate-save"); File.AppendAllText(lifecycle.SecondarySavePath,"-legitimate-save"); lifecycle.SealForExternalCleanup(); L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,lifecycle.RunId,pid,()=>true); Check(!File.Exists(lifecycle.PrimarySavePath));
            var afterSeal=Fresh(root,saves,"dddddddddddddddddddddddddddddddd"); afterSeal.SealForExternalCleanup(); File.AppendAllText(afterSeal.PrimarySavePath,"-after-seal"); byte[] postSealPrimary=File.ReadAllBytes(afterSeal.PrimarySavePath), postSealSecondary=File.ReadAllBytes(afterSeal.SecondarySavePath); Expect(()=>L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,afterSeal.RunId,pid,()=>true)); Check(Bytes(postSealPrimary,File.ReadAllBytes(afterSeal.PrimarySavePath))&&Bytes(postSealSecondary,File.ReadAllBytes(afterSeal.SecondarySavePath)));
            string collision = "44444444444444444444444444444444"; string collide = Path.Combine(saves, "ISRWorldGen-L00C-" + collision + "-activated-primary.vcdbs"); File.WriteAllText(collide, "user-like-collision"); Expect(() => L00CCampaignStorage.CreateForRun(root, saves, collision)); Check(File.ReadAllText(collide) == "user-like-collision");
            // Collisions arriving after campaign preparation are checked by the
            // exact production native-create guard for both roles.
            var late = L00CCampaignStorage.CreateForRun(root, saves, "55555555555555555555555555555555"); File.WriteAllText(late.PrimarySavePath,"late-primary"); Expect(()=>late.RequireVacantNativeCreateTarget("activated-primary",late.PrimarySavePath)); Check(File.ReadAllText(late.PrimarySavePath)=="late-primary"); File.WriteAllText(late.SecondarySavePath,"late-secondary"); Expect(()=>late.RequireVacantNativeCreateTarget("activated-secondary",late.SecondarySavePath)); Check(File.ReadAllText(late.SecondarySavePath)=="late-secondary");
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
            return 0;
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
    private static bool Bytes(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static L00CCampaignStorage Fresh(string root,string saves,string id) { var x=L00CCampaignStorage.CreateForRun(root,saves,id); File.WriteAllText(x.PrimarySavePath,"primary"); File.WriteAllText(x.SecondarySavePath,"secondary"); x.PublishFixtureMarker("activated-primary",x.PrimarySavePath); x.PublishFixtureMarker("activated-secondary",x.SecondarySavePath); return x; }
    private static string HashText(string value) { using(var h=SHA256.Create()){var b=h.ComputeHash(Encoding.UTF8.GetBytes(value));var s=new StringBuilder(64);foreach(byte x in b)s.Append(x.ToString("X2"));return s.ToString();} }
    private static void AssertPreserved(L00CCampaignStorage x, Action action) { byte[] p=File.ReadAllBytes(x.PrimarySavePath),s=File.ReadAllBytes(x.SecondarySavePath); Expect(action); Check(Bytes(p,File.ReadAllBytes(x.PrimarySavePath))&&Bytes(s,File.ReadAllBytes(x.SecondarySavePath))); }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("L00-C AppData storage oracle failed."); }
    private static void Expect(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("L00-C oracle expected refusal."); }
}
