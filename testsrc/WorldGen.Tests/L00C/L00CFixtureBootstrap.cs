// Debug-only handoff from the ten-save campaign owner to the immutable S2
// scenario and its production S3 lifecycle adapter.
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ISRWorldGen.L00C.Laboratory;

internal sealed class L00CFixtureBootstrap
{
    internal const string ObservationAdapterRequiredCode = "L00C_S2_S3_OBSERVATION_ADAPTER_REQUIRED";
    private const int WaitTimeoutSeconds = 120;

    private readonly L00CCampaignStorage campaign;
    private readonly string evidence;
    private readonly L00CScenarioDefinition definition;
    private readonly L00CScenarioHostAdapter integratedAdapter;
    private bool handedOff;

    // The adapter must own all ten paths in the
    // durable campaign manifest and provide Ready/SaveCommitted observations.
    internal L00CFixtureBootstrap(L00CCampaignStorage campaign, L00CScenarioHostAdapter adapter)
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
        integratedAdapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    // Called only by the process-global ScreenManager pump. It publishes the
    // fully composed scheduler before any native creation.
    internal bool TryAdvance(object screenManager, out L00CMenuActionLaboratoryHost? completedHost)
    {
        RequireDebugLaboratory();
        if (screenManager is null) throw new ArgumentNullException(nameof(screenManager));
        completedHost = null;
        if (handedOff) return true;
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
