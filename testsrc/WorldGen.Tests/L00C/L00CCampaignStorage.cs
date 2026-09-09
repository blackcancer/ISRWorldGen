// Debug-harness campaign storage. Vanilla single-player scans GamePaths.Saves;
// fixtures live there while campaign evidence remains under repository .local.
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ISRWorldGen.L00C.Laboratory;

internal sealed class L00CCampaignStorage
{
    // Debug/test seam only. Production defaults to File.GetAttributes and the
    // same IsReparse guard consumes it for every trust-chain hop.
    private static Func<string, FileAttributes> attributeReader = File.GetAttributes;
    internal static IDisposable OverrideAttributeReaderForTests(Func<string, FileAttributes> reader) { var prior=attributeReader; attributeReader=reader ?? throw new ArgumentNullException(nameof(reader)); return new RestoreAttributes(prior); }
    private const string Prefix = "ISRWorldGen-L00C-";
    private const string ProvenanceName = "campaign-provenance.json";
    private const string CleanupName = "cleanup-receipt.json";
    private L00CCampaignStorage(string root, string saves, string id, string campaign)
    {
        LaboratoryRoot = root; GameSavesDirectory = saves; RunId = id; CampaignRoot = campaign;
        EvidenceDirectory = Path.Combine(campaign, "evidence"); ProvenancePath = Path.Combine(campaign, ProvenanceName); CleanupReceiptPath = Path.Combine(campaign, CleanupName);
        PrimarySavePath = Owned("activated-primary"); SecondarySavePath = Owned("activated-secondary");
    }
    internal string LaboratoryRoot { get; } internal string GameSavesDirectory { get; } internal string RunId { get; }
    internal string CampaignRoot { get; } internal string EvidenceDirectory { get; } internal string ProvenancePath { get; }
    internal string CleanupReceiptPath { get; } internal string PrimarySavePath { get; } internal string SecondarySavePath { get; }

    internal static L00CCampaignStorage Create(string laboratoryRoot, string gamePathsSaves) => CreateForRun(laboratoryRoot, gamePathsSaves, Guid.NewGuid().ToString("N"));
    // Deterministic seam for executable tests only; production never picks a run id.
    internal static L00CCampaignStorage CreateForRun(string laboratoryRoot, string gamePathsSaves, string runId)
    {
        string root = CanonicalLab(laboratoryRoot); string saves = CanonicalDirectory(gamePathsSaves, "GamePaths.Saves"); CheckId(runId);
        if (!Directory.Exists(root)) throw new InvalidOperationException("L00-C requires existing repository .local\\L00C.");
        string campaigns = Path.Combine(root, "campaigns"); Directory.CreateDirectory(campaigns); RequireNoReparse(campaigns);
        string campaign = Child(campaigns, runId);
        if (Directory.Exists(campaign) || File.Exists(campaign)) throw new InvalidOperationException("L00-C campaign collision preserves existing data.");
        var result = new L00CCampaignStorage(root, saves, runId, campaign);
        result.RequireVacant(result.PrimarySavePath); result.RequireVacant(result.SecondarySavePath);
        Directory.CreateDirectory(campaign); WriteNew(result.ProvenancePath, result.Provenance()); Directory.CreateDirectory(result.EvidenceDirectory);
        return result;
    }

    internal void PublishFixtureMarker(string role, string savePath)
    {
        RequireRole(role, savePath); if (!File.Exists(savePath)) throw new InvalidOperationException("L00-C cannot attest absent fixture.");
        string marker = MarkerPath(savePath); if (File.Exists(marker)) throw new InvalidOperationException("L00-C refuses marker overwrite.");
        WriteNew(marker, "{\"schema\":\"l00c-appdata-save-marker-v1\",\"runId\":\"" + RunId + "\",\"role\":\"" + role + "\",\"savePath\":\"" + Esc(Path.GetFullPath(savePath)) + "\",\"provenancePath\":\"" + Esc(ProvenancePath) + "\",\"sha256\":\"" + Hash(savePath) + "\"}");
    }

    // Must run immediately before the guarded ConnectToSingleplayer call. A
    // collision after campaign preparation is still a refusal, never overwrite.
    internal void RequireVacantNativeCreateTarget(string role, string savePath)
    {
        RequireRole(role, savePath); EnsureTrusted(savePath, GameSavesDirectory); RequireVacant(savePath);
    }

    // Final native return must precede this. This method only seals evidence;
    // a separate post-process cleanup is the only deleting operation.
    internal void SealForExternalCleanup()
    {
        if (File.Exists(CleanupReceiptPath)) throw new InvalidOperationException("L00-C cleanup receipt already exists.");
        if (ValidateProvenance() != Process.GetCurrentProcess().Id) throw new InvalidOperationException("L00-C sealing process identity mismatch."); RequireAttested("activated-primary", PrimarySavePath); RequireAttested("activated-secondary", SecondarySavePath);
        WriteNew(CleanupReceiptPath, "{\"schema\":\"l00c-appdata-cleanup-v1\",\"runId\":\"" + RunId + "\",\"runtimeStoppedRequired\":true,\"runtimeProcessId\":" + Process.GetCurrentProcess().Id + ",\"primarySave\":\"" + Esc(PrimarySavePath) + "\",\"primarySha256\":\"" + Hash(PrimarySavePath) + "\",\"secondarySave\":\"" + Esc(SecondarySavePath) + "\",\"secondarySha256\":\"" + Hash(SecondarySavePath) + "\"}");
    }

    // Caller supplies a process-death proof. No glob, directory delete, reuse or
    // repair is permitted: each exact pair is fully revalidated before deletion.
    internal static void CleanupAfterRuntimeStopped(string laboratoryRoot, string gamePathsSaves, string runId, int runtimeProcessId, Func<bool> runtimeStopped)
    {
        if (runtimeStopped is null || !runtimeStopped()) throw new InvalidOperationException("L00-C cleanup refuses while runtime is alive.");
        string root = CanonicalLab(laboratoryRoot); string saves = CanonicalDirectory(gamePathsSaves, "GamePaths.Saves"); CheckId(runId);
        string campaign = Child(Path.Combine(root, "campaigns"), runId); var item = new L00CCampaignStorage(root, saves, runId, campaign);
        item.EnsureCampaignTrust();
        int provenancePid=item.ValidateProvenance(); EnsureTrusted(item.CleanupReceiptPath, item.CampaignRoot); if (!File.Exists(item.CleanupReceiptPath)) throw new InvalidOperationException("L00-C cleanup evidence absent.");
        string receipt = File.ReadAllText(item.CleanupReceiptPath); item.ValidateCleanupReceipt(receipt, runtimeProcessId, provenancePid);
        item.RequireAttested("activated-primary", item.PrimarySavePath); item.RequireAttested("activated-secondary", item.SecondarySavePath);
        var parsed = L00CStrictEvidenceJson.Parse(receipt); if (!string.Equals(Hash(item.PrimarySavePath), parsed.StringValue("primarySha256"), StringComparison.Ordinal) || !string.Equals(Hash(item.SecondarySavePath), parsed.StringValue("secondarySha256"), StringComparison.Ordinal)) throw new InvalidOperationException("L00-C cleanup preserves changed fixture.");
        // Validate all first; then exact owned marker/file pairs only.
        File.Delete(MarkerPath(item.PrimarySavePath)); File.Delete(item.PrimarySavePath); File.Delete(MarkerPath(item.SecondarySavePath)); File.Delete(item.SecondarySavePath);
    }

    internal static bool IsCampaignSavePath(string laboratoryRoot, string gamePathsSaves, string savePath)
    {
        try { string root = CanonicalLab(laboratoryRoot); string saves = CanonicalDirectory(gamePathsSaves, "GamePaths.Saves"); string full = Path.GetFullPath(savePath); if (!string.Equals(Path.GetExtension(full), ".vcdbs", StringComparison.OrdinalIgnoreCase) || !DirectChild(full, saves) || IsReparse(full)) return false; string stem = Path.GetFileNameWithoutExtension(full); if (!stem.StartsWith(Prefix, StringComparison.Ordinal) || !ParseName(stem, out string id, out _)) return false; var item=new L00CCampaignStorage(root,saves,id,Child(Path.Combine(root,"campaigns"),id)); item.EnsureCampaignTrust(); return File.Exists(item.ProvenancePath); } catch { return false; }
    }

    private string Owned(string role) => Child(GameSavesDirectory, Prefix + RunId + "-" + role + ".vcdbs");
    private void RequireVacant(string save) { if (!DirectChild(save, GameSavesDirectory) || File.Exists(save) || Directory.Exists(save) || File.Exists(MarkerPath(save)) || Directory.Exists(MarkerPath(save))) throw new InvalidOperationException("L00-C refuses existing AppData fixture or marker."); }
    private void EnsureCampaignTrust() { EnsureAllAncestors(LaboratoryRoot); string campaigns=Path.Combine(LaboratoryRoot,"campaigns"); EnsureTrusted(campaigns,LaboratoryRoot); EnsureTrusted(CampaignRoot,campaigns); EnsureTrusted(ProvenancePath,CampaignRoot); if(File.Exists(CleanupReceiptPath))EnsureTrusted(CleanupReceiptPath,CampaignRoot); }
    private void RequireRole(string role, string save)
    {
        string expected = role == "activated-primary" ? PrimarySavePath : role == "activated-secondary" ? SecondarySavePath : throw new InvalidOperationException("L00-C fixture role invalid.");
        if (!string.Equals(Path.GetFullPath(save), expected, StringComparison.OrdinalIgnoreCase) || !DirectChild(expected, GameSavesDirectory) || IsReparse(expected)) throw new InvalidOperationException("L00-C fixture escaped GamePaths.Saves.");
    }
    private void RequireAttested(string role, string save)
    {
        RequireRole(role, save); string marker = MarkerPath(save);
        EnsureTrusted(save, GameSavesDirectory); EnsureTrusted(marker, GameSavesDirectory);
        if (!File.Exists(save) || !File.Exists(marker) || IsReparse(save) || IsReparse(marker)) throw new InvalidOperationException("L00-C owned fixture/marker absent or reparse-pointed.");
        var json = L00CStrictEvidenceJson.Parse(File.ReadAllText(marker)); json.Exactly("schema","runId","role","savePath","provenancePath","sha256");
        // The marker hash attests the bytes at native creation only. Worlds are
        // legitimately saved during the five reopen cycles, so creation identity
        // must not depend on mutable bytes. Seal records the final hashes, and
        // cleanup compares those final hashes immediately before deletion.
        if(json.StringValue("schema")!="l00c-appdata-save-marker-v1"||json.StringValue("runId")!=RunId||json.StringValue("role")!=role||!string.Equals(json.StringValue("savePath"),Path.GetFullPath(save),StringComparison.OrdinalIgnoreCase)||!string.Equals(json.StringValue("provenancePath"),ProvenancePath,StringComparison.OrdinalIgnoreCase)||!ValidHash(json.StringValue("sha256"))) throw new InvalidOperationException("L00-C fixture creation provenance mismatch.");
    }
    private string Provenance() => "{\"schema\":\"l00c-appdata-campaign-v1\",\"runId\":\"" + RunId + "\",\"laboratoryRoot\":\"" + Esc(LaboratoryRoot) + "\",\"gamePathsSaves\":\"" + Esc(GameSavesDirectory) + "\",\"primarySave\":\"" + Esc(PrimarySavePath) + "\",\"secondarySave\":\"" + Esc(SecondarySavePath) + "\",\"processId\":" + Process.GetCurrentProcess().Id + "}";
    internal static string MarkerPath(string save) => save + ".l00c-appdata.json";
    private static void WriteNew(string path, string value) { using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); byte[] b = Encoding.UTF8.GetBytes(value); f.Write(b, 0, b.Length); }
    private static string Hash(string path) { using SHA256 h = SHA256.Create(); using FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); var b = h.ComputeHash(f); var r = new StringBuilder(64); foreach (byte x in b) r.Append(x.ToString("X2")); return r.ToString(); }
    private static string CanonicalDirectory(string path, string label) { string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); if (!Directory.Exists(full) || IsReparse(full)) throw new InvalidOperationException("L00-C requires existing non-reparse " + label + "."); EnsureAllAncestors(full); return full; }
    private static string CanonicalLab(string path) { string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); DirectoryInfo? parent = Directory.GetParent(full); if (parent is null || !string.Equals(Path.GetFileName(full), "L00C", StringComparison.OrdinalIgnoreCase) || !string.Equals(parent.Name, ".local", StringComparison.OrdinalIgnoreCase) || IsReparse(full)) throw new InvalidOperationException("L00-C requires real non-reparse .local\\L00C."); EnsureAllAncestors(full); return full; }
    private static string Child(string parent, string name) { if (string.IsNullOrWhiteSpace(name) || name.IndexOf("..", StringComparison.Ordinal) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidOperationException("L00-C child name unsafe."); string child = Path.GetFullPath(Path.Combine(parent, name)); if (!DirectChild(child, parent)) throw new InvalidOperationException("L00-C child escaped root."); return child; }
    private static bool DirectChild(string path, string parent) => string.Equals(Path.GetFullPath(Path.GetDirectoryName(path) ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    private static bool IsReparse(string path) { try { return (attributeReader(path) & FileAttributes.ReparsePoint) != 0; } catch { return false; } }
    private static void RequireNoReparse(string path) { if (IsReparse(path)) throw new InvalidOperationException("L00-C refuses reparse campaign directory."); }
    private static void CheckId(string value) { if (value is null || value.Length != 32) throw new InvalidOperationException("L00-C run id must be lowercase hex GUID."); foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) throw new InvalidOperationException("L00-C run id must be lowercase hex GUID."); }
    private static bool ParseName(string stem, out string id, out string role) { id = string.Empty; role = string.Empty; string tail = stem.Substring(Prefix.Length); if (tail.Length < 34 || tail[32] != '-') return false; id = tail.Substring(0, 32); role = tail.Substring(33); try { CheckId(id); return role == "activated-primary" || role == "activated-secondary"; } catch { return false; } }
    private static string Esc(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private int ValidateProvenance()
    {
        EnsureTrusted(ProvenancePath, CampaignRoot); if (!File.Exists(ProvenancePath)) throw new InvalidOperationException("L00-C provenance absent.");
        var j=L00CStrictEvidenceJson.Parse(File.ReadAllText(ProvenancePath)); j.Exactly("schema","runId","laboratoryRoot","gamePathsSaves","primarySave","secondarySave","processId");
        int pid=j.Int("processId"); if(pid<=0||j.StringValue("schema")!="l00c-appdata-campaign-v1"||j.StringValue("runId")!=RunId||!string.Equals(j.StringValue("laboratoryRoot"),LaboratoryRoot,StringComparison.OrdinalIgnoreCase)||!string.Equals(j.StringValue("gamePathsSaves"),GameSavesDirectory,StringComparison.OrdinalIgnoreCase)||!string.Equals(j.StringValue("primarySave"),PrimarySavePath,StringComparison.OrdinalIgnoreCase)||!string.Equals(j.StringValue("secondarySave"),SecondarySavePath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("L00-C provenance cross-identity mismatch."); return pid;
    }
    private void ValidateCleanupReceipt(string text, int runtimeProcessId, int provenancePid)
    {
        var j=L00CStrictEvidenceJson.Parse(text); j.Exactly("schema","runId","runtimeStoppedRequired","runtimeProcessId","primarySave","primarySha256","secondarySave","secondarySha256");
        if(j.StringValue("schema")!="l00c-appdata-cleanup-v1"||j.StringValue("runId")!=RunId||!j.True("runtimeStoppedRequired")||j.Int("runtimeProcessId")!=runtimeProcessId||runtimeProcessId!=provenancePid||!string.Equals(j.StringValue("primarySave"),PrimarySavePath,StringComparison.OrdinalIgnoreCase)||!string.Equals(j.StringValue("secondarySave"),SecondarySavePath,StringComparison.OrdinalIgnoreCase)||!ValidHash(j.StringValue("primarySha256"))||!ValidHash(j.StringValue("secondarySha256")))throw new InvalidOperationException("L00-C cleanup cross-identity mismatch.");
    }
    private static bool ValidHash(string value) { if(value.Length!=64)return false; foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='A'&&c<='F')))return false; return true; }
    private static void EnsureTrusted(string path, string stopAt)
    {
        string current = Path.GetFullPath(path); string stop = Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (true) { if (IsReparse(current)) throw new InvalidOperationException("L00-C refuses reparse ancestor."); if (string.Equals(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), stop, StringComparison.OrdinalIgnoreCase)) return; DirectoryInfo? parent = Directory.GetParent(current); if (parent is null) throw new InvalidOperationException("L00-C trust chain escaped root."); current = parent.FullName; }
    }
    private static void EnsureAllAncestors(string path)
    {
        string current = Path.GetFullPath(path);
        while (true) { if (IsReparse(current)) throw new InvalidOperationException("L00-C refuses reparse ancestor."); DirectoryInfo? parent = Directory.GetParent(current); if (parent is null) return; current = parent.FullName; }
    }
    private sealed class RestoreAttributes : IDisposable { private Func<string,FileAttributes>? prior; internal RestoreAttributes(Func<string,FileAttributes> value){prior=value;} public void Dispose(){var value=prior;prior=null;if(value is not null)attributeReader=value;} }
}
