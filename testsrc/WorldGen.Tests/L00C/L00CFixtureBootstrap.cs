// Debug-only handoff from the current two-save campaign owner to the immutable
// S2 scenario. The base manifest cannot own ten saves, so the legacy entrypoint
// fails closed before any native action. An integrator-owned storage/S3 adapter
// may use the explicit overload without changing the scenario model.
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ISRWorldGen.L00C.Laboratory;

internal sealed class L00CFixtureBootstrap
{
    internal const string ScenarioManifestRequiredCode = "L00C_S2_SCENARIO_MANIFEST_REQUIRED";
    internal const string ObservationAdapterRequiredCode = "L00C_S2_S3_OBSERVATION_ADAPTER_REQUIRED";
    private const int WaitTimeoutSeconds = 120;

    private readonly L00CCampaignStorage campaign;
    private readonly string evidence;
    private readonly L00CScenarioDefinition definition;
    private readonly L00CScenarioHostAdapter? integratedAdapter;
    private bool handedOff;
    private bool refusalWritten;

    internal L00CFixtureBootstrap(L00CCampaignStorage campaign)
        : this(campaign, null)
    {
    }

    // Explicit S3/integrator seam. The adapter must own all ten paths in the
    // durable campaign manifest and provide Ready/SaveCommitted observations.
    internal L00CFixtureBootstrap(L00CCampaignStorage campaign, L00CScenarioHostAdapter? adapter)
    {
        RequireDebugLaboratory();
        this.campaign = campaign ?? throw new ArgumentNullException(nameof(campaign));
        string root = CanonicalLaboratoryRoot(campaign.LaboratoryRoot);
        if (!Directory.Exists(root))
            throw new L00CScenarioException("L00C_S2_LABORATORY_ROOT_ABSENT", null,
                "repository .local\\L00C root must already exist");
        evidence = Path.GetFullPath(campaign.EvidenceDirectory);
        if (!Directory.Exists(evidence) || !IsUnder(evidence, campaign.CampaignRoot))
            throw new L00CScenarioException("L00C_S2_EVIDENCE_AUTHORITY_INVALID", null,
                "campaign evidence directory is absent or outside campaign root");
        long timeout = checked(Stopwatch.Frequency * WaitTimeoutSeconds);
        definition = L00CScenarioDefinition.CreateOwned(campaign.RunId,
            Path.GetFullPath(campaign.GameSavesDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), timeout);
        integratedAdapter = adapter;
    }

    // Called only by the process-global ScreenManager pump. The default base
    // path refuses before native creation because L00CCampaignStorage v1 owns
    // only activated-primary/secondary and cannot recover ten dedicated saves.
    internal bool TryAdvance(object screenManager, out L00CMenuActionLaboratoryHost? completedHost)
    {
        RequireDebugLaboratory();
        if (screenManager is null) throw new ArgumentNullException(nameof(screenManager));
        completedHost = null;
        if (handedOff) return true;
        if (integratedAdapter is null)
        {
            WriteRefusalOnce();
            throw new L00CScenarioException(ScenarioManifestRequiredCode, null,
                "storage v1 owns two saves; S2 requires ten exact A_i/B_i paths, sidecars, markers, interruption journal, and cleanup order");
        }

        string hostEvidence = Path.Combine(evidence, "menu-actions-s2");
        completedHost = L00CMenuActionLaboratoryHost.OpenIntegrated(definition, hostEvidence, integratedAdapter);
        handedOff = true;
        return true;
    }

    internal void SignalLevelFinalize(int sessionOrdinal)
    {
        throw new L00CScenarioException(ObservationAdapterRequiredCode, null,
            "bootstrap does not infer session identity from LevelFinalize; S3 must forward the exact immutable Ready observation after host handoff;received=" + sessionOrdinal);
    }

    internal L00CScenarioDefinition DefinitionForTests => definition;

    private void WriteRefusalOnce()
    {
        if (refusalWritten) return;
        refusalWritten = true;
        string path = Path.Combine(evidence, "s2-scenario-manifest-required.json");
        if (File.Exists(path)) return;
        string json = "{\"schema\":\"l00c-s2-integration-refusal-v1\",\"code\":\"" +
            ScenarioManifestRequiredCode + "\",\"runId\":\"" + Escape(campaign.RunId) +
            "\",\"requiredIterations\":5,\"requiredSessions\":15,\"requiredDedicatedSaves\":10," +
            "\"currentOwnedSaves\":2,\"nativeActionStarted\":false}";
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    private static string CanonicalLaboratoryRoot(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DirectoryInfo? parent = Directory.GetParent(full);
        if (parent is null || !string.Equals(Path.GetFileName(full), "L00C", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parent.Name, ".local", StringComparison.OrdinalIgnoreCase))
            throw new L00CScenarioException("L00C_S2_LABORATORY_ROOT_NOT_CANONICAL", null,
                "laboratory root must be the repository .local\\L00C directory");
        return full;
    }

    private static bool IsUnder(string path, string parent)
        => path.StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void RequireDebugLaboratory()
    {
#if L00C_STANDALONE_ORACLE
        return;
#elif !DEBUG
        throw new L00CScenarioException("L00C_S2_DEBUG_BUILD_REQUIRED", null,
            "fixture bootstrap is disabled outside Debug");
#else
        if (!Debugger.IsAttached || !string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal))
            throw new L00CScenarioException("L00C_S2_DEBUG_LAB_AUTHORITY_REQUIRED", null,
                "Debugger.IsAttached and ISR_L00C_LAB=1 are both required");
#endif
    }
}
