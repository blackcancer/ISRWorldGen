using System.Runtime.InteropServices;
using System.Text.Json;
using ISRWorldGen.Core.Atlas.Profiles;

namespace ISRWorldGen.Tests.L02C;

[TestClass]
public sealed class ProfileProcessProbeTests
{
    [TestMethod]
    public void DeterminismProcessProbe()
    {
        FrozenScaleProfile profile = ProfileTestSupport.FreezeBalanced();
        PatternInspectionMaps maps = PatternTestSupport.VisibleVoronoiWitness();
        PatternDiagnosticPolicy policy = PatternTestSupport.CalibratedPolicy();
        PatternDiagnosticReport diagnostic = ProfileTestSupport.Success(
            PatternDiagnostics.Analyze(maps, policy));
        PatternSensitivityReport sensitivity = ProfileTestSupport.Success(PatternSensitivityEvaluator.Evaluate(
        [
            new PatternCorpusCase("visible", true, maps),
            new PatternCorpusCase("smooth", false, PatternTestSupport.SmoothField()),
            new PatternCorpusCase("periodic-other", false, PatternTestSupport.PeriodicNonVoronoi()),
            new PatternCorpusCase("low-contrast", true, PatternTestSupport.LowContrastWitness()),
        ], policy));

        Assert.IsTrue(diagnostic.SignalDetected);
        Assert.AreEqual(1, sensitivity.FalsePositiveCount);
        Assert.AreEqual(1, sensitivity.FalseNegativeCount);

        string? outputPath = Environment.GetEnvironmentVariable("ISRW_L02C_PROBE_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        string commit = Environment.GetEnvironmentVariable("ISRW_L02C_COMMIT")
            ?? throw new InvalidOperationException("ISRW_L02C_COMMIT is required for persisted evidence.");
        string fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            status = "PASS",
            requirement = new[] { "R02-05", "R02-06" },
            test = new[] { "T02-05-analytical", "T02-06-analytical" },
            commit,
            profileHash = profile.GeographyConfigHash.ToString(),
            profileBytes = FrozenScaleProfileCodec.Serialize(profile).Length,
            mapsHash = maps.ContentChecksum.ToString(),
            policyHash = policy.ContentChecksum.ToString(),
            policy.EdgeGradientThreshold,
            policy.EdgeAlignmentThresholdPpm,
            policy.PeriodicityThresholdPpm,
            policy.MinimumPeriodLag,
            policy.MaximumPeriodLag,
            policy.MaximumAnalysisWorkUnits,
            policy.MaximumCorpusAnalysisWorkUnits,
            diagnosticHash = diagnostic.ContentChecksum.ToString(),
            sensitivityHash = sensitivity.ContentChecksum.ToString(),
            diagnostic.EdgeAlignmentScorePpm,
            diagnostic.AxisPeriodicityScorePpm,
            diagnostic.StrongestHorizontalLag,
            diagnostic.StrongestVerticalLag,
            sensitivity.TruePositiveCount,
            sensitivity.TrueNegativeCount,
            sensitivity.FalsePositiveCount,
            sensitivity.FalseNegativeCount,
            processId = Environment.ProcessId,
            framework = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
