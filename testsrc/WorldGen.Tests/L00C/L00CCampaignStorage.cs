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
    private static Action<string>? transitionHook;
    internal static IDisposable OverrideTransitionHookForTests(Action<string> hook) { var prior=transitionHook; transitionHook=hook ?? throw new ArgumentNullException(nameof(hook)); return new RestoreTransitionHook(prior); }
    private const string Prefix = "ISRWorldGen-L00C-";
    private const string ProvenanceName = "campaign-provenance.json";
    private const string CleanupName = "cleanup-receipt.json";
    private const string AbortDirectoryName = "abort";
    private const string AbortCleanupIntentName = "abort-cleanup-intent.json";
    private const string AbortCleanedName = "abort-cleaned.json";
    private const string LegacyRecoveryManifestName = "legacy-prejournal-recovery-manifest.json";
    private const string LegacyRecoverySealName = "legacy-prejournal-recovery-seal.json";
    private const string LegacyRecoveryIntentName = "legacy-prejournal-recovery-intent.json";
    private const string LegacyRecoveryCleanedName = "legacy-prejournal-recovery-cleaned.json";
    internal const string SupportedLegacyRecoveryRunId = "a7290d12e6f54247bae27b71e2e571cf";
    internal const int SupportedLegacyRecoveryProcessId = 74920;
    internal const string LegacyRecoveryAttestation = "I-ATTEST-L00C-A7290D12-PRIMARY-RAW-ONLY";
    private L00CCampaignStorage(string root, string saves, string id, string campaign)
    {
        LaboratoryRoot = root; GameSavesDirectory = saves; RunId = id; CampaignRoot = campaign;
        EvidenceDirectory = Path.Combine(campaign, "evidence"); ProvenancePath = Path.Combine(campaign, ProvenanceName); CleanupReceiptPath = Path.Combine(campaign, CleanupName);
        AbortDirectory = Path.Combine(campaign, AbortDirectoryName); AbortCleanupIntentPath = Path.Combine(campaign, AbortCleanupIntentName); AbortCleanedPath = Path.Combine(campaign, AbortCleanedName);
        LegacyRecoveryManifestPath = Path.Combine(campaign, LegacyRecoveryManifestName); LegacyRecoverySealPath = Path.Combine(campaign, LegacyRecoverySealName);
        LegacyRecoveryIntentPath = Path.Combine(campaign, LegacyRecoveryIntentName); LegacyRecoveryCleanedPath = Path.Combine(campaign, LegacyRecoveryCleanedName);
        PrimarySavePath = Owned("activated-primary"); SecondarySavePath = Owned("activated-secondary");
    }
    internal string LaboratoryRoot { get; } internal string GameSavesDirectory { get; } internal string RunId { get; }
    internal string CampaignRoot { get; } internal string EvidenceDirectory { get; } internal string ProvenancePath { get; }
    internal string CleanupReceiptPath { get; } internal string PrimarySavePath { get; } internal string SecondarySavePath { get; }
    internal string AbortDirectory { get; } internal string AbortCleanupIntentPath { get; } internal string AbortCleanedPath { get; }
    internal string LegacyRecoveryManifestPath { get; } internal string LegacyRecoverySealPath { get; }
    internal string LegacyRecoveryIntentPath { get; } internal string LegacyRecoveryCleanedPath { get; }

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
        Directory.CreateDirectory(campaign); WriteNew(result.ProvenancePath, result.Provenance()); Directory.CreateDirectory(result.AbortDirectory); result.WriteAbortState(0, "prepared"); Directory.CreateDirectory(result.EvidenceDirectory);
        return result;
    }

    internal void PublishFixtureMarker(string role, string savePath)
    {
        RequireRole(role, savePath); if (!File.Exists(savePath)) throw new InvalidOperationException("L00-C cannot attest absent fixture.");
        string marker = MarkerPath(savePath); if (File.Exists(marker)) throw new InvalidOperationException("L00-C refuses marker overwrite.");
        WriteNew(marker, "{\"schema\":\"l00c-appdata-save-marker-v1\",\"runId\":\"" + RunId + "\",\"role\":\"" + role + "\",\"savePath\":\"" + Esc(Path.GetFullPath(savePath)) + "\",\"provenancePath\":\"" + Esc(ProvenancePath) + "\",\"sha256\":\"" + Hash(savePath) + "\"}");
        WriteAbortState(role == "activated-primary" ? 2 : 4, role == "activated-primary" ? "primary-created" : "secondary-created");
    }

    // This receipt is deliberately persisted before the native create call.
    // Thus a client crash between intent and its first filesystem write still
    // has a bounded, ownership-attested cleanup route.
    internal void PrepareNativeCreate(string role, string savePath)
    {
        RequireRole(role, savePath);
        WriteAbortState(role == "activated-primary" ? 1 : 3, role == "activated-primary" ? "primary-create-intent" : "secondary-create-intent");
    }

    internal void BeginCycling() => WriteAbortState(5, "cycling");

    // A vacancy refusal after an intent is not proof that native creation ran.
    // Preserve its exact target for manual inspection; it is never an abort
    // cleanup candidate.
    internal void RecordNativeCreateRefusal(string role, string savePath)
    {
        RequireRole(role, savePath);
        string refusal = Child(CampaignRoot, "abort-create-refused.json");
        if (!File.Exists(refusal)) WriteNew(refusal, "{\"schema\":\"l00c-appdata-create-refused-v1\",\"runId\":\"" + RunId + "\",\"role\":\"" + role + "\",\"savePath\":\"" + Esc(savePath) + "\",\"runtimeProcessId\":" + Process.GetCurrentProcess().Id + "}");
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
        if (ReadAbortState() != 5) throw new InvalidOperationException("L00-C sealing requires the durable cycling state.");
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
        int provenancePid=item.ValidateProvenance();
        if (File.Exists(item.CleanupReceiptPath)) { item.CleanupSealed(runtimeProcessId, provenancePid); return; }
        item.CleanupAborted(runtimeProcessId, provenancePid);
    }

    // One compatibility authority for the single residue left by the integrated
    // pre-journal build.  The integrator must supply the already-inspected exact
    // hashes and the literal attestation; this method never discovers candidates.
    internal static string CreateLegacyPreJournalRecoveryManifest(
        string laboratoryRoot,
        string gamePathsSaves,
        string runId,
        int runtimeProcessId,
        string primarySavePath,
        string primarySaveSha256,
        string provenanceSha256,
        string integratorAttestation,
        Func<bool> runtimeStopped)
    {
        RequireRuntimeStopped(runtimeStopped, "legacy recovery manifest");
        RequireSupportedLegacyAuthority(runId, runtimeProcessId, integratorAttestation);
        string root = CanonicalLab(laboratoryRoot); string saves = CanonicalDirectory(gamePathsSaves, "GamePaths.Saves");
        var item = new L00CCampaignStorage(root, saves, runId, Child(Path.Combine(root, "campaigns"), runId));
        if (!string.Equals(Path.GetFullPath(primarySavePath), item.PrimarySavePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("L00-C legacy primary path is not the pinned campaign target.");
        item.ValidateLegacyManifestCreationShape(runtimeProcessId, primarySaveSha256, provenanceSha256);
        string manifest = item.LegacyManifest(primarySaveSha256, provenanceSha256, runtimeProcessId, integratorAttestation);
        RequireRuntimeStopped(runtimeStopped, "legacy manifest write");
        WriteNewDurable(item.LegacyRecoveryManifestPath, manifest);
        transitionHook?.Invoke("legacy-manifest-written");
        string manifestHash = Hash(item.LegacyRecoveryManifestPath);
        RequireRuntimeStopped(runtimeStopped, "legacy seal write");
        WriteNewDurable(item.LegacyRecoverySealPath, item.LegacySeal(manifestHash, primarySaveSha256, runtimeProcessId));
        transitionHook?.Invoke("legacy-seal-written");
        return Hash(item.LegacyRecoverySealPath);
    }

    // This is intentionally not called by CleanupAfterRuntimeStopped.  An
    // operator must select the dedicated legacy launcher and provide both
    // sealed hashes.  A generic missing journal remains an unconditional refusal.
    internal static void CleanupLegacyPreJournalAfterRuntimeStopped(
        string laboratoryRoot,
        string gamePathsSaves,
        string runId,
        int runtimeProcessId,
        string manifestSha256,
        string sealSha256,
        Func<bool> runtimeStopped)
    {
        RequireRuntimeStopped(runtimeStopped, "legacy cleanup");
        RequireSupportedLegacyAuthority(runId, runtimeProcessId, LegacyRecoveryAttestation);
        string root = CanonicalLab(laboratoryRoot); string saves = CanonicalDirectory(gamePathsSaves, "GamePaths.Saves");
        var item = new L00CCampaignStorage(root, saves, runId, Child(Path.Combine(root, "campaigns"), runId));
        item.ValidateLegacyAuthority(runtimeProcessId, manifestSha256, sealSha256);
        bool resuming = File.Exists(item.LegacyRecoveryIntentPath);
        item.ValidateLegacyResidue(resuming);
        if (File.Exists(item.LegacyRecoveryCleanedPath)) throw new InvalidOperationException("L00-C legacy recovery was already completed; replay refused.");
        if (!resuming)
        {
            RequireRuntimeStopped(runtimeStopped, "legacy cleanup intent write");
            WriteNewDurable(item.LegacyRecoveryIntentPath, item.LegacyIntent(manifestSha256, sealSha256, runtimeProcessId));
            transitionHook?.Invoke("legacy-cleanup-intent-written");
        }
        item.ValidateLegacyAuthority(runtimeProcessId, manifestSha256, sealSha256);
        item.ValidateLegacyIntent(runtimeProcessId, manifestSha256, sealSha256);
        item.ValidateLegacyResidue(true);
        if (File.Exists(item.PrimarySavePath))
        {
            RequireRuntimeStopped(runtimeStopped, "legacy primary deletion");
            File.Delete(item.PrimarySavePath);
            if (File.Exists(item.PrimarySavePath)) throw new IOException("L00-C legacy primary deletion did not complete.");
            transitionHook?.Invoke("legacy-primary-deleted");
        }
        // Deletion is itself an interruption boundary. Re-establish the full
        // authority, receipt and residue invariants before certifying completion;
        // a post-delete mutation must strand the intent for inspection, not mint
        // a misleading cleaned receipt.
        item.ValidateLegacyAuthority(runtimeProcessId, manifestSha256, sealSha256);
        item.ValidateLegacyIntent(runtimeProcessId, manifestSha256, sealSha256);
        item.ValidateLegacyResidue(true);
        string intentHash = Hash(item.LegacyRecoveryIntentPath);
        RequireRuntimeStopped(runtimeStopped, "legacy cleaned receipt write");
        WriteNewDurable(item.LegacyRecoveryCleanedPath, item.LegacyCleaned(manifestSha256, sealSha256, intentHash, runtimeProcessId));
        transitionHook?.Invoke("legacy-cleaned-written");
    }

    // Existing sealed contract, kept deliberately separate from abort recovery.
    private void CleanupSealed(int runtimeProcessId, int provenancePid)
    {
        EnsureTrusted(CleanupReceiptPath, CampaignRoot); string receipt = File.ReadAllText(CleanupReceiptPath); ValidateCleanupReceipt(receipt, runtimeProcessId, provenancePid);
        RequireAttested("activated-primary", PrimarySavePath); RequireAttested("activated-secondary", SecondarySavePath);
        var parsed = L00CStrictEvidenceJson.Parse(receipt); if (!string.Equals(Hash(PrimarySavePath), parsed.StringValue("primarySha256"), StringComparison.Ordinal) || !string.Equals(Hash(SecondarySavePath), parsed.StringValue("secondarySha256"), StringComparison.Ordinal)) throw new InvalidOperationException("L00-C cleanup preserves changed fixture.");
        File.Delete(MarkerPath(PrimarySavePath)); File.Delete(PrimarySavePath); File.Delete(MarkerPath(SecondarySavePath)); File.Delete(SecondarySavePath);
    }

    private void CleanupAborted(int runtimeProcessId, int provenancePid)
    {
        if (runtimeProcessId <= 0 || runtimeProcessId != provenancePid) throw new InvalidOperationException("L00-C abort cleanup process identity mismatch.");
        if(File.Exists(Child(CampaignRoot,"abort-create-refused.json"))) throw new InvalidOperationException("L00-C abort cleanup preserves a refused native-create target.");
        ValidateCampaignEntries(); int state = ReadAbortState(); bool resumingDeletion = File.Exists(AbortCleanupIntentPath);
        ValidateAbortResidue(state, resumingDeletion);
        if (File.Exists(AbortCleanedPath)) throw new InvalidOperationException("L00-C abort cleanup was already completed.");
        if (!resumingDeletion)
            WriteNew(AbortCleanupIntentPath, AbortCleanupIntent(state, runtimeProcessId));
        ValidateAbortCleanupIntent(state, runtimeProcessId, resumingDeletion);
        DeleteIfPresent(MarkerPath(PrimarySavePath)); DeleteIfPresent(PrimarySavePath); DeleteIfPresent(MarkerPath(SecondarySavePath)); DeleteIfPresent(SecondarySavePath);
        WriteNew(AbortCleanedPath, "{\"schema\":\"l00c-appdata-abort-cleaned-v1\",\"runId\":\"" + RunId + "\",\"runtimeProcessId\":" + runtimeProcessId + ",\"state\":\"" + AbortStateName(state) + "\"}");
    }

    internal static bool IsCampaignSavePath(string laboratoryRoot, string gamePathsSaves, string savePath)
    {
        try { string root = CanonicalLab(laboratoryRoot); string saves = CanonicalDirectory(gamePathsSaves, "GamePaths.Saves"); string full = Path.GetFullPath(savePath); if (!string.Equals(Path.GetExtension(full), ".vcdbs", StringComparison.OrdinalIgnoreCase) || !DirectChild(full, saves) || IsReparse(full)) return false; string stem = Path.GetFileNameWithoutExtension(full); if (!stem.StartsWith(Prefix, StringComparison.Ordinal) || !ParseName(stem, out string id, out _)) return false; var item=new L00CCampaignStorage(root,saves,id,Child(Path.Combine(root,"campaigns"),id)); item.EnsureCampaignTrust(); return File.Exists(item.ProvenancePath); } catch { return false; }
    }

    private string Owned(string role) => Child(GameSavesDirectory, Prefix + RunId + "-" + role + ".vcdbs");
    private void RequireVacant(string save) { if (!DirectChild(save, GameSavesDirectory) || File.Exists(save) || Directory.Exists(save) || File.Exists(MarkerPath(save)) || Directory.Exists(MarkerPath(save))) throw new InvalidOperationException("L00-C refuses existing AppData fixture or marker."); }
    private void EnsureCampaignTrust() { EnsureAllAncestors(LaboratoryRoot); string campaigns=Path.Combine(LaboratoryRoot,"campaigns"); EnsureTrusted(campaigns,LaboratoryRoot); EnsureTrusted(CampaignRoot,campaigns); EnsureTrusted(ProvenancePath,CampaignRoot); if(Directory.Exists(AbortDirectory))EnsureTrusted(AbortDirectory,CampaignRoot); if(File.Exists(CleanupReceiptPath))EnsureTrusted(CleanupReceiptPath,CampaignRoot); if(File.Exists(AbortCleanupIntentPath))EnsureTrusted(AbortCleanupIntentPath,CampaignRoot); if(File.Exists(AbortCleanedPath))EnsureTrusted(AbortCleanedPath,CampaignRoot); }
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
    private void WriteAbortState(int sequence, string state)
    {
        EnsureCampaignTrust();
        if (sequence < 0 || sequence > 5 || state != AbortStateName(sequence)) throw new InvalidOperationException("L00-C abort state invalid.");
        for (int prior = 0; prior < sequence; prior++) ValidateAbortState(prior);
        string path = AbortStatePath(sequence);
        if (File.Exists(path)) { ValidateAbortState(sequence); return; }
        if (sequence > 0 && !File.Exists(AbortStatePath(sequence - 1))) throw new InvalidOperationException("L00-C abort state transition is not contiguous.");
        WriteNew(path, "{\"schema\":\"l00c-appdata-abort-state-v1\",\"runId\":\"" + RunId + "\",\"state\":\"" + state + "\",\"sequence\":" + sequence + ",\"runtimeProcessId\":" + Process.GetCurrentProcess().Id + ",\"primarySave\":\"" + Esc(PrimarySavePath) + "\",\"secondarySave\":\"" + Esc(SecondarySavePath) + "\"}");
    }
    private int ReadAbortState()
    {
        EnsureTrusted(AbortDirectory, CampaignRoot); if (!Directory.Exists(AbortDirectory)) throw new InvalidOperationException("L00-C abort journal absent.");
        int highest = -1; bool gap = false;
        for (int n = 0; n <= 5; n++) { string path = AbortStatePath(n); if (File.Exists(path)) { if(gap) throw new InvalidOperationException("L00-C abort journal has a gap."); ValidateAbortState(n); highest = n; } else if(highest >= 0) gap = true; }
        if (highest < 0) throw new InvalidOperationException("L00-C abort journal contains no prepared state.");
        foreach (string entry in Directory.EnumerateFileSystemEntries(AbortDirectory))
        {
            string name = Path.GetFileName(entry); bool known = false;
            for (int n=0;n<=highest;n++) if (string.Equals(name, Path.GetFileName(AbortStatePath(n)), StringComparison.Ordinal)) known=true;
            if (!known) throw new InvalidOperationException("L00-C abort journal contains an unexpected entry.");
        }
        return highest;
    }
    private void ValidateAbortState(int sequence)
    {
        string path=AbortStatePath(sequence); EnsureTrusted(path, AbortDirectory); if(!File.Exists(path)) throw new InvalidOperationException("L00-C abort state absent.");
        var j=L00CStrictEvidenceJson.Parse(File.ReadAllText(path)); j.Exactly("schema","runId","state","sequence","runtimeProcessId","primarySave","secondarySave");
        if(j.StringValue("schema")!="l00c-appdata-abort-state-v1"||j.StringValue("runId")!=RunId||j.StringValue("state")!=AbortStateName(sequence)||j.Int("sequence")!=sequence||j.Int("runtimeProcessId")!=ValidateProvenance()||!string.Equals(j.StringValue("primarySave"),PrimarySavePath,StringComparison.OrdinalIgnoreCase)||!string.Equals(j.StringValue("secondarySave"),SecondarySavePath,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("L00-C abort state cross-identity mismatch.");
    }
    private string AbortStatePath(int sequence) => Child(AbortDirectory, sequence.ToString("D2") + "-" + AbortStateName(sequence) + ".json");
    private static string AbortStateName(int sequence) => sequence switch { 0 => "prepared", 1 => "primary-create-intent", 2 => "primary-created", 3 => "secondary-create-intent", 4 => "secondary-created", 5 => "cycling", _ => throw new InvalidOperationException("L00-C abort state sequence invalid.") };
    private void ValidateAbortResidue(int state, bool allowAlreadyDeleted)
    {
        EnsureTrusted(GameSavesDirectory, GameSavesDirectory);
        bool primaryRequired=state>=2, secondaryRequired=state>=4;
        ValidateAbortPair("activated-primary",PrimarySavePath,primaryRequired,state==1,allowAlreadyDeleted);
        ValidateAbortPair("activated-secondary",SecondarySavePath,secondaryRequired,state==3,allowAlreadyDeleted);
        foreach(string entry in Directory.EnumerateFileSystemEntries(GameSavesDirectory))
        { string n=Path.GetFileName(entry); if(!n.StartsWith(Prefix+RunId+"-",StringComparison.OrdinalIgnoreCase))continue; string full=Path.GetFullPath(entry); if(!string.Equals(full,PrimarySavePath,StringComparison.OrdinalIgnoreCase)&&!string.Equals(full,SecondarySavePath,StringComparison.OrdinalIgnoreCase)&&!string.Equals(full,MarkerPath(PrimarySavePath),StringComparison.OrdinalIgnoreCase)&&!string.Equals(full,MarkerPath(SecondarySavePath),StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("L00-C abort cleanup refuses unexpected owned-name residue."); }
    }
    private void ValidateAbortPair(string role,string save,bool required,bool optional,bool allowAlreadyDeleted)
    {
        string marker=MarkerPath(save); bool saveExists=File.Exists(save), markerExists=File.Exists(marker);
        if(IsReparse(save)||IsReparse(marker)||Directory.Exists(save)||Directory.Exists(marker)||(!optional&&!required&&(saveExists||markerExists))||(markerExists&&!saveExists)||(required&&!allowAlreadyDeleted&&saveExists!=markerExists)||(required&&!allowAlreadyDeleted&&(!saveExists||!markerExists))) throw new InvalidOperationException("L00-C abort residue does not match durable state: role="+role+", required="+required+", optional="+optional+", resuming="+allowAlreadyDeleted+", save="+saveExists+", marker="+markerExists+".");
        if(saveExists) { RequireRole(role,save); EnsureTrusted(save,GameSavesDirectory); }
        if(markerExists) RequireAttested(role,save);
    }
    private string AbortCleanupIntent(int state,int pid) => "{\"schema\":\"l00c-appdata-abort-cleanup-intent-v1\",\"runId\":\""+RunId+"\",\"runtimeProcessId\":"+pid+",\"state\":\""+AbortStateName(state)+"\",\"primarySaveSha256\":\""+HashIfPresent(PrimarySavePath)+"\",\"primaryMarkerSha256\":\""+HashIfPresent(MarkerPath(PrimarySavePath))+"\",\"secondarySaveSha256\":\""+HashIfPresent(SecondarySavePath)+"\",\"secondaryMarkerSha256\":\""+HashIfPresent(MarkerPath(SecondarySavePath))+"\"}";
    private void ValidateAbortCleanupIntent(int state,int pid,bool resumingDeletion)
    {
        EnsureTrusted(AbortCleanupIntentPath,CampaignRoot); var j=L00CStrictEvidenceJson.Parse(File.ReadAllText(AbortCleanupIntentPath)); j.Exactly("schema","runId","runtimeProcessId","state","primarySaveSha256","primaryMarkerSha256","secondarySaveSha256","secondaryMarkerSha256");
        if(j.StringValue("schema")!="l00c-appdata-abort-cleanup-intent-v1"||j.StringValue("runId")!=RunId||j.Int("runtimeProcessId")!=pid||j.StringValue("state")!=AbortStateName(state))throw new InvalidOperationException("L00-C abort cleanup intent mismatch.");
        ValidateCapturedHashes(new[]{MarkerPath(PrimarySavePath),PrimarySavePath,MarkerPath(SecondarySavePath),SecondarySavePath},new[]{j.StringValue("primaryMarkerSha256"),j.StringValue("primarySaveSha256"),j.StringValue("secondaryMarkerSha256"),j.StringValue("secondarySaveSha256")},resumingDeletion);
    }
    private static string HashIfPresent(string path) => File.Exists(path) ? Hash(path) : string.Empty;
    private static void ValidateCapturedHashes(string[] paths,string[] captured,bool resumingDeletion)
    { bool seenExisting=false; for(int n=0;n<paths.Length;n++) { if(captured[n].Length!=0&&!ValidHash(captured[n]))throw new InvalidOperationException("L00-C abort cleanup hash invalid."); bool exists=File.Exists(paths[n]); if(exists) { seenExisting=true; if(captured[n].Length==0||!string.Equals(Hash(paths[n]),captured[n],StringComparison.Ordinal))throw new InvalidOperationException("L00-C abort cleanup preserves changed residue."); } else if(captured[n].Length!=0&&(!resumingDeletion||seenExisting)) throw new InvalidOperationException("L00-C abort cleanup residue disappeared out of order."); } }
    private static void DeleteIfPresent(string path) { if(File.Exists(path)) File.Delete(path); }
    private void ValidateCampaignEntries()
    {
        foreach(string entry in Directory.EnumerateFileSystemEntries(CampaignRoot))
        {
            string name=Path.GetFileName(entry);
            bool known=string.Equals(name,ProvenanceName,StringComparison.Ordinal)||string.Equals(name,"evidence",StringComparison.Ordinal)||string.Equals(name,AbortDirectoryName,StringComparison.Ordinal)||string.Equals(name,CleanupName,StringComparison.Ordinal)||string.Equals(name,AbortCleanupIntentName,StringComparison.Ordinal)||string.Equals(name,AbortCleanedName,StringComparison.Ordinal)||string.Equals(name,"abort-create-refused.json",StringComparison.Ordinal);
            if(!known||IsReparse(entry)) throw new InvalidOperationException("L00-C campaign root contains an unexpected or reparse entry.");
        }
    }
    private void ValidateLegacyManifestCreationShape(int runtimeProcessId, string primarySaveSha256, string provenanceSha256)
    {
        ValidateHashArgument(primarySaveSha256, "primary"); ValidateHashArgument(provenanceSha256, "provenance");
        EnsureLegacyTrust(false);
        ValidateLegacyCampaignEntries(false, false);
        RejectCurrentRecoveryArtifacts();
        int provenancePid = ValidateProvenance();
        if (provenancePid != runtimeProcessId || !string.Equals(Hash(ProvenancePath), provenanceSha256, StringComparison.Ordinal)) throw new InvalidOperationException("L00-C legacy provenance identity mismatch.");
        ValidateLegacyOwnedNames();
        if (!File.Exists(PrimarySavePath) || Directory.Exists(PrimarySavePath) || IsReparse(PrimarySavePath)) throw new InvalidOperationException("L00-C legacy primary raw residue is absent or unsafe.");
        EnsureTrusted(PrimarySavePath, GameSavesDirectory);
        if (!string.Equals(Hash(PrimarySavePath), primarySaveSha256, StringComparison.Ordinal)) throw new InvalidOperationException("L00-C legacy primary bytes do not match the manual attestation.");
        RequireLegacyAbsent(MarkerPath(PrimarySavePath), "primary marker");
        RequireLegacyAbsent(SecondarySavePath, "secondary save");
        RequireLegacyAbsent(MarkerPath(SecondarySavePath), "secondary marker");
        RequireLegacyAbsent(LegacyRecoveryManifestPath, "legacy manifest");
        RequireLegacyAbsent(LegacyRecoverySealPath, "legacy seal");
        RequireLegacyAbsent(LegacyRecoveryIntentPath, "legacy cleanup intent");
        RequireLegacyAbsent(LegacyRecoveryCleanedPath, "legacy cleaned receipt");
    }
    private void ValidateLegacyAuthority(int runtimeProcessId, string manifestSha256, string sealSha256)
    {
        ValidateHashArgument(manifestSha256, "manifest"); ValidateHashArgument(sealSha256, "seal");
        EnsureLegacyTrust(true);
        bool hasIntent = File.Exists(LegacyRecoveryIntentPath), hasCleaned = File.Exists(LegacyRecoveryCleanedPath);
        ValidateLegacyCampaignEntries(hasIntent, hasCleaned);
        RejectCurrentRecoveryArtifacts();
        if (!File.Exists(LegacyRecoveryManifestPath) || !File.Exists(LegacyRecoverySealPath)) throw new InvalidOperationException("L00-C legacy recovery authority is absent.");
        if (!string.Equals(Hash(LegacyRecoveryManifestPath), manifestSha256, StringComparison.Ordinal) || !string.Equals(Hash(LegacyRecoverySealPath), sealSha256, StringComparison.Ordinal)) throw new InvalidOperationException("L00-C legacy sealed authority hash mismatch.");
        var manifest = L00CStrictEvidenceJson.Parse(File.ReadAllText(LegacyRecoveryManifestPath));
        manifest.Exactly("schema","runId","legacyShape","laboratoryRoot","gamePathsSaves","campaignRoot","provenancePath","provenanceSha256","runtimeProcessId","primarySave","primarySha256","primaryMarkerState","secondarySave","secondaryState","secondaryMarkerState","abortJournalState","integratorAttestation");
        if (manifest.StringValue("schema") != "l00c-legacy-prejournal-recovery-manifest-v1" ||
            manifest.StringValue("runId") != RunId || manifest.StringValue("legacyShape") != "primary-raw-only" ||
            !SamePath(manifest.StringValue("laboratoryRoot"), LaboratoryRoot) || !SamePath(manifest.StringValue("gamePathsSaves"), GameSavesDirectory) ||
            !SamePath(manifest.StringValue("campaignRoot"), CampaignRoot) || !SamePath(manifest.StringValue("provenancePath"), ProvenancePath) ||
            manifest.Int("runtimeProcessId") != runtimeProcessId || !SamePath(manifest.StringValue("primarySave"), PrimarySavePath) ||
            !ValidHash(manifest.StringValue("provenanceSha256")) || !ValidHash(manifest.StringValue("primarySha256")) ||
            manifest.StringValue("primaryMarkerState") != "absent" || !SamePath(manifest.StringValue("secondarySave"), SecondarySavePath) ||
            manifest.StringValue("secondaryState") != "absent" || manifest.StringValue("secondaryMarkerState") != "absent" ||
            manifest.StringValue("abortJournalState") != "absent" || manifest.StringValue("integratorAttestation") != LegacyRecoveryAttestation)
            throw new InvalidOperationException("L00-C legacy manifest identity mismatch.");
        if (ValidateProvenance() != runtimeProcessId || !string.Equals(Hash(ProvenancePath), manifest.StringValue("provenanceSha256"), StringComparison.Ordinal)) throw new InvalidOperationException("L00-C legacy provenance changed after sealing.");
        var seal = L00CStrictEvidenceJson.Parse(File.ReadAllText(LegacyRecoverySealPath));
        seal.Exactly("schema","runId","manifestPath","manifestSha256","runtimeProcessId","primarySave","primarySha256");
        if (seal.StringValue("schema") != "l00c-legacy-prejournal-recovery-seal-v1" || seal.StringValue("runId") != RunId ||
            !SamePath(seal.StringValue("manifestPath"), LegacyRecoveryManifestPath) || seal.StringValue("manifestSha256") != manifestSha256 ||
            seal.Int("runtimeProcessId") != runtimeProcessId || !SamePath(seal.StringValue("primarySave"), PrimarySavePath) ||
            seal.StringValue("primarySha256") != manifest.StringValue("primarySha256"))
            throw new InvalidOperationException("L00-C legacy seal identity mismatch.");
    }
    private void ValidateLegacyIntent(int runtimeProcessId, string manifestSha256, string sealSha256)
    {
        EnsureTrusted(LegacyRecoveryIntentPath, CampaignRoot);
        if (!File.Exists(LegacyRecoveryIntentPath) || Directory.Exists(LegacyRecoveryIntentPath)) throw new InvalidOperationException("L00-C legacy cleanup intent is absent or unsafe.");
        string intentText = File.ReadAllText(LegacyRecoveryIntentPath);
        var intent = L00CStrictEvidenceJson.Parse(intentText);
        intent.Exactly("schema","runId","runtimeProcessId","manifestSha256","sealSha256","primarySave","primarySha256");
        var manifest = L00CStrictEvidenceJson.Parse(File.ReadAllText(LegacyRecoveryManifestPath));
        if (intent.StringValue("schema") != "l00c-legacy-prejournal-recovery-intent-v1" || intent.StringValue("runId") != RunId ||
            intent.Int("runtimeProcessId") != runtimeProcessId || intent.StringValue("manifestSha256") != manifestSha256 || intent.StringValue("sealSha256") != sealSha256 ||
            !SamePath(intent.StringValue("primarySave"), PrimarySavePath) || intent.StringValue("primarySha256") != manifest.StringValue("primarySha256") ||
            !string.Equals(intentText, LegacyIntent(manifestSha256, sealSha256, runtimeProcessId), StringComparison.Ordinal))
            throw new InvalidOperationException("L00-C legacy cleanup intent identity mismatch.");
    }
    private void ValidateLegacyResidue(bool allowDeletedAfterIntent)
    {
        ValidateLegacyOwnedNames();
        RequireLegacyAbsent(MarkerPath(PrimarySavePath), "primary marker");
        RequireLegacyAbsent(SecondarySavePath, "secondary save");
        RequireLegacyAbsent(MarkerPath(SecondarySavePath), "secondary marker");
        bool exists = File.Exists(PrimarySavePath);
        if (!exists && !allowDeletedAfterIntent) throw new InvalidOperationException("L00-C legacy primary disappeared before cleanup intent.");
        if (Directory.Exists(PrimarySavePath) || IsReparse(PrimarySavePath)) throw new InvalidOperationException("L00-C legacy primary identity is unsafe.");
        if (exists)
        {
            EnsureTrusted(PrimarySavePath, GameSavesDirectory);
            var manifest = L00CStrictEvidenceJson.Parse(File.ReadAllText(LegacyRecoveryManifestPath));
            if (!string.Equals(Hash(PrimarySavePath), manifest.StringValue("primarySha256"), StringComparison.Ordinal)) throw new InvalidOperationException("L00-C legacy primary bytes changed after sealing.");
        }
    }
    private void EnsureLegacyTrust(bool requireSealedAuthority)
    {
        EnsureAllAncestors(LaboratoryRoot);
        string campaigns = Path.Combine(LaboratoryRoot, "campaigns");
        EnsureTrusted(campaigns, LaboratoryRoot); EnsureTrusted(CampaignRoot, campaigns); EnsureTrusted(ProvenancePath, CampaignRoot); EnsureTrusted(EvidenceDirectory, CampaignRoot);
        if (requireSealedAuthority || File.Exists(LegacyRecoveryManifestPath)) EnsureTrusted(LegacyRecoveryManifestPath, CampaignRoot);
        if (requireSealedAuthority || File.Exists(LegacyRecoverySealPath)) EnsureTrusted(LegacyRecoverySealPath, CampaignRoot);
        if (File.Exists(LegacyRecoveryIntentPath)) EnsureTrusted(LegacyRecoveryIntentPath, CampaignRoot);
        if (File.Exists(LegacyRecoveryCleanedPath)) EnsureTrusted(LegacyRecoveryCleanedPath, CampaignRoot);
    }
    private void ValidateLegacyCampaignEntries(bool allowIntent, bool allowCleaned)
    {
        if (!Directory.Exists(CampaignRoot) || IsReparse(CampaignRoot) || !File.Exists(ProvenancePath) || Directory.Exists(ProvenancePath) || !Directory.Exists(EvidenceDirectory) || File.Exists(EvidenceDirectory) || IsReparse(ProvenancePath) || IsReparse(EvidenceDirectory)) throw new InvalidOperationException("L00-C legacy campaign shape is not provenance plus evidence.");
        foreach (string entry in Directory.EnumerateFileSystemEntries(CampaignRoot))
        {
            string name = Path.GetFileName(entry);
            bool known = string.Equals(name, ProvenanceName, StringComparison.Ordinal) || string.Equals(name, "evidence", StringComparison.Ordinal) ||
                string.Equals(name, LegacyRecoveryManifestName, StringComparison.Ordinal) || string.Equals(name, LegacyRecoverySealName, StringComparison.Ordinal) ||
                (allowIntent && string.Equals(name, LegacyRecoveryIntentName, StringComparison.Ordinal)) || (allowCleaned && string.Equals(name, LegacyRecoveryCleanedName, StringComparison.Ordinal));
            if (!known || IsReparse(entry)) throw new InvalidOperationException("L00-C legacy campaign contains an unexpected or reparse entry.");
            if ((string.Equals(name, LegacyRecoveryManifestName, StringComparison.Ordinal) || string.Equals(name, LegacyRecoverySealName, StringComparison.Ordinal) || string.Equals(name, LegacyRecoveryIntentName, StringComparison.Ordinal) || string.Equals(name, LegacyRecoveryCleanedName, StringComparison.Ordinal)) && Directory.Exists(entry)) throw new InvalidOperationException("L00-C legacy receipt path is not a file.");
        }
    }
    private void RejectCurrentRecoveryArtifacts()
    {
        RequireLegacyAbsent(AbortDirectory, "current abort journal"); RequireLegacyAbsent(CleanupReceiptPath, "sealed cleanup receipt");
        RequireLegacyAbsent(AbortCleanupIntentPath, "current abort cleanup intent"); RequireLegacyAbsent(AbortCleanedPath, "current abort cleaned receipt");
        RequireLegacyAbsent(Child(CampaignRoot, "abort-create-refused.json"), "native-create refusal receipt");
    }
    private void ValidateLegacyOwnedNames()
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(GameSavesDirectory))
        {
            string name = Path.GetFileName(entry);
            if (!name.StartsWith(Prefix + RunId + "-", StringComparison.OrdinalIgnoreCase)) continue;
            string full = Path.GetFullPath(entry);
            if (!string.Equals(full, PrimarySavePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("L00-C legacy recovery refuses an unexpected owned-name residue.");
        }
    }
    private string LegacyManifest(string primaryHash, string provenanceHash, int pid, string attestation) =>
        "{\"schema\":\"l00c-legacy-prejournal-recovery-manifest-v1\",\"runId\":\"" + RunId + "\",\"legacyShape\":\"primary-raw-only\",\"laboratoryRoot\":\"" + Esc(LaboratoryRoot) + "\",\"gamePathsSaves\":\"" + Esc(GameSavesDirectory) + "\",\"campaignRoot\":\"" + Esc(CampaignRoot) + "\",\"provenancePath\":\"" + Esc(ProvenancePath) + "\",\"provenanceSha256\":\"" + provenanceHash + "\",\"runtimeProcessId\":" + pid + ",\"primarySave\":\"" + Esc(PrimarySavePath) + "\",\"primarySha256\":\"" + primaryHash + "\",\"primaryMarkerState\":\"absent\",\"secondarySave\":\"" + Esc(SecondarySavePath) + "\",\"secondaryState\":\"absent\",\"secondaryMarkerState\":\"absent\",\"abortJournalState\":\"absent\",\"integratorAttestation\":\"" + attestation + "\"}";
    private string LegacySeal(string manifestHash, string primaryHash, int pid) =>
        "{\"schema\":\"l00c-legacy-prejournal-recovery-seal-v1\",\"runId\":\"" + RunId + "\",\"manifestPath\":\"" + Esc(LegacyRecoveryManifestPath) + "\",\"manifestSha256\":\"" + manifestHash + "\",\"runtimeProcessId\":" + pid + ",\"primarySave\":\"" + Esc(PrimarySavePath) + "\",\"primarySha256\":\"" + primaryHash + "\"}";
    private string LegacyIntent(string manifestHash, string sealHash, int pid)
    {
        var manifest = L00CStrictEvidenceJson.Parse(File.ReadAllText(LegacyRecoveryManifestPath));
        return "{\"schema\":\"l00c-legacy-prejournal-recovery-intent-v1\",\"runId\":\"" + RunId + "\",\"runtimeProcessId\":" + pid + ",\"manifestSha256\":\"" + manifestHash + "\",\"sealSha256\":\"" + sealHash + "\",\"primarySave\":\"" + Esc(PrimarySavePath) + "\",\"primarySha256\":\"" + manifest.StringValue("primarySha256") + "\"}";
    }
    private string LegacyCleaned(string manifestHash, string sealHash, string intentHash, int pid) =>
        "{\"schema\":\"l00c-legacy-prejournal-recovery-cleaned-v1\",\"runId\":\"" + RunId + "\",\"runtimeProcessId\":" + pid + ",\"manifestSha256\":\"" + manifestHash + "\",\"sealSha256\":\"" + sealHash + "\",\"intentSha256\":\"" + intentHash + "\",\"primarySave\":\"" + Esc(PrimarySavePath) + "\"}";
    private static void RequireSupportedLegacyAuthority(string runId, int runtimeProcessId, string attestation)
    {
        CheckId(runId);
        if (runId != SupportedLegacyRecoveryRunId || runtimeProcessId != SupportedLegacyRecoveryProcessId || attestation != LegacyRecoveryAttestation) throw new InvalidOperationException("L00-C legacy recovery authority is pinned to one attested run and PID.");
    }
    private static void RequireLegacyAbsent(string path, string label)
    {
        if (File.Exists(path) || Directory.Exists(path) || IsReparse(path)) throw new InvalidOperationException("L00-C legacy recovery requires absent " + label + ".");
    }
    private static void ValidateHashArgument(string hash, string label)
    {
        if (hash is null || !ValidHash(hash)) throw new InvalidOperationException("L00-C legacy " + label + " hash is invalid.");
    }
    private static void RequireRuntimeStopped(Func<bool>? runtimeStopped, string operation)
    {
        if (runtimeStopped is null || !runtimeStopped()) throw new InvalidOperationException("L00-C " + operation + " refuses while runtime is alive or PID was reused.");
    }
    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private void ValidateCleanupReceipt(string text, int runtimeProcessId, int provenancePid)
    {
        var j=L00CStrictEvidenceJson.Parse(text); j.Exactly("schema","runId","runtimeStoppedRequired","runtimeProcessId","primarySave","primarySha256","secondarySave","secondarySha256");
        if(j.StringValue("schema")!="l00c-appdata-cleanup-v1"||j.StringValue("runId")!=RunId||!j.True("runtimeStoppedRequired")||j.Int("runtimeProcessId")!=runtimeProcessId||runtimeProcessId!=provenancePid||!string.Equals(j.StringValue("primarySave"),PrimarySavePath,StringComparison.OrdinalIgnoreCase)||!string.Equals(j.StringValue("secondarySave"),SecondarySavePath,StringComparison.OrdinalIgnoreCase)||!ValidHash(j.StringValue("primarySha256"))||!ValidHash(j.StringValue("secondarySha256")))throw new InvalidOperationException("L00-C cleanup cross-identity mismatch.");
    }
    private static bool ValidHash(string value) { if(value.Length!=64)return false; foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='A'&&c<='F')))return false; return true; }
    private static void WriteNewDurable(string path, string value) { using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough); byte[] b = Encoding.UTF8.GetBytes(value); f.Write(b, 0, b.Length); f.Flush(true); }
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
    private sealed class RestoreTransitionHook : IDisposable { private Action<string>? prior; internal RestoreTransitionHook(Action<string>? value){prior=value;} public void Dispose(){var value=prior;prior=null;transitionHook=value;} }
}
