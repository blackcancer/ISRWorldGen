using System.Collections;
using System.Text.Json;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;

namespace ISRWorldGen.Tests.L02C;

[TestClass]
public sealed class PatternDiagnosticsTests
{
    [TestMethod]
    public void DeliberatelyVisibleVoronoiWitness_IsSignalledByAlignmentAndPeriodicityDiagnostics()
    {
        PatternInspectionMaps witness = PatternTestSupport.VisibleVoronoiWitness();
        PatternDiagnosticPolicy policy = PatternTestSupport.CalibratedPolicy();

        PatternDiagnosticReport report = ProfileTestSupport.Success(
            PatternDiagnostics.Analyze(witness, policy));

        Assert.IsTrue(report.SignalDetected);
        Assert.IsTrue(report.EdgeAlignmentExceeded);
        Assert.IsTrue(report.PeriodicityExceeded);
        Assert.IsGreaterThanOrEqualTo(policy.EdgeAlignmentThresholdPpm, report.EdgeAlignmentScorePpm);
        Assert.IsGreaterThanOrEqualTo(policy.PeriodicityThresholdPpm, report.AxisPeriodicityScorePpm);
        Assert.IsGreaterThanOrEqualTo(policy.MinimumPeriodLag, report.StrongestHorizontalLag);
        Assert.IsLessThanOrEqualTo(policy.MaximumPeriodLag, report.StrongestHorizontalLag);
        Assert.IsGreaterThanOrEqualTo(policy.MinimumPeriodLag, report.StrongestVerticalLag);
        Assert.IsLessThanOrEqualTo(policy.MaximumPeriodLag, report.StrongestVerticalLag);
        Assert.IsTrue(report.RequiresQualitativeReview);
    }

    [TestMethod]
    public void SmoothNonPeriodicField_IsNotPromotedToUniversalVisualPass()
    {
        PatternDiagnosticReport report = ProfileTestSupport.Success(PatternDiagnostics.Analyze(
            PatternTestSupport.SmoothField(),
            PatternTestSupport.CalibratedPolicy()));

        Assert.IsFalse(report.SignalDetected);
        Assert.IsFalse(report.EdgeAlignmentExceeded);
        Assert.IsFalse(report.PeriodicityExceeded);
        Assert.IsTrue(report.RequiresQualitativeReview);
        Assert.AreEqual(PatternReviewDisposition.QualitativeReviewRequired, report.ReviewDisposition);
    }

    [TestMethod]
    public void SensitivityReport_RecordsFalsePositivesFalseNegativesAndLimitsInStableOrder()
    {
        PatternDiagnosticPolicy policy = PatternTestSupport.CalibratedPolicy();
        PatternCorpusCase[] corpus =
        [
            new("04-low-contrast-voronoi", expectedVisibleVoronoi: true, PatternTestSupport.LowContrastWitness()),
            new("02-smooth", expectedVisibleVoronoi: false, PatternTestSupport.SmoothField()),
            new("03-periodic-non-voronoi", expectedVisibleVoronoi: false, PatternTestSupport.PeriodicNonVoronoi()),
            new("01-visible-voronoi", expectedVisibleVoronoi: true, PatternTestSupport.VisibleVoronoiWitness()),
        ];

        PatternSensitivityReport report = ProfileTestSupport.Success(
            PatternSensitivityEvaluator.Evaluate(corpus, policy));
        PatternSensitivityReport reversed = ProfileTestSupport.Success(
            PatternSensitivityEvaluator.Evaluate(corpus.Reverse().ToArray(), policy));

        Assert.AreEqual(1, report.TruePositiveCount);
        Assert.AreEqual(1, report.TrueNegativeCount);
        Assert.AreEqual(1, report.FalsePositiveCount);
        Assert.AreEqual(1, report.FalseNegativeCount);
        Assert.AreEqual(500_000, report.SensitivityPpm);
        Assert.AreEqual(500_000, report.SpecificityPpm);
        CollectionAssert.AreEqual(
            corpus.Select(item => item.CaseId).Order(StringComparer.Ordinal).ToArray(),
            report.Cases.Select(item => item.CaseId).ToArray());
        Assert.AreEqual(report.ContentChecksum, reversed.ContentChecksum);
        Assert.IsGreaterThanOrEqualTo(3, report.Limitations.Count);
        Assert.IsTrue(report.Limitations.Any(limit => limit.Contains("not universal", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(report.RequiresQualitativeReview);
    }

    [TestMethod]
    public void InspectionMaps_AreSeparateDefensiveCopiesWithCanonicalChecksum()
    {
        ushort[] final = PatternTestSupport.CreateVisibleFinalValues();
        ushort[] edges = PatternTestSupport.CreateGridEdges();
        PatternInspectionMaps maps = PatternTestSupport.Maps(final, edges);
        Hash256 checksum = maps.ContentChecksum;
        final[0] = ushort.MaxValue;
        edges[0] = ushort.MaxValue;

        Assert.AreEqual(checksum, maps.ContentChecksum);
        Assert.AreNotSame(maps.FinalOutput, maps.AtlasEdges);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<ushort>)maps.FinalOutput.Values)[0] = 1);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<ushort>)maps.AtlasEdges.Values)[0] = 1);
        CollectionAssert.AreNotEqual(maps.FinalOutput.Values.ToArray(), maps.AtlasEdges.Values.ToArray());
    }

    [TestMethod]
    public void WorkerCountClockAndPathsCannotEnterMapOrDiagnosticChecksums()
    {
        string[] forbiddenTokens = ["Worker", "Clock", "Time", "Path", "Account"];
        Type[] geographicTypes =
        [
            typeof(InspectionRaster),
            typeof(PatternInspectionMaps),
            typeof(PatternDiagnosticReport),
            typeof(PatternSensitivityReport),
        ];

        foreach (Type type in geographicTypes)
        {
            Assert.IsFalse(type.GetProperties().Any(property =>
                forbiddenTokens.Any(token => property.Name.Contains(token, StringComparison.OrdinalIgnoreCase))),
                $"{type.Name} exposes non-geographic runtime metadata.");
        }
    }

    [TestMethod]
    public void MapsWithDifferentDimensions_AreTypedFailure()
    {
        InspectionRaster final = PatternTestSupport.Raster(4, 4, new ushort[16]);
        InspectionRaster edges = PatternTestSupport.Raster(8, 2, new ushort[16]);

        GenerationFailure<PatternInspectionMaps> failure = ProfileTestSupport.Failure(
            PatternInspectionMaps.Create(final, edges, PatternTestSupport.InspectionBudget()));

        Assert.AreEqual(GenerationFailureCode.InvalidInput, failure.Error.Code);
        Assert.AreEqual("atlas.pattern.map-dimensions", failure.Error.Stage);
    }

    [TestMethod]
    public void ImpossibleRasterCapacity_IsRefusedBeforeEnumerationOrAllocation()
    {
        var values = new ImpossibleRasterList(int.MaxValue);

        GenerationFailure<InspectionRaster> failure = ProfileTestSupport.Failure(
            InspectionRaster.Create(int.MaxValue, int.MaxValue, values, PatternTestSupport.InspectionBudget()));

        Assert.AreEqual(GenerationFailureCode.InvalidInput, failure.Error.Code);
        Assert.AreEqual("atlas.pattern.raster-capacity", failure.Error.Stage);
        Assert.AreEqual(0, values.EnumerationCount);
        Assert.AreEqual(0, values.IndexAccessCount);
    }

    [TestMethod]
    [DoNotParallelize]
    public void LargeRepresentableRaster_IsRefusedByExplicitBudgetBeforeCallerAccess()
    {
        const int width = 20_000;
        const int height = 20_000;
        var values = new ImpossibleRasterList(width * height);

        long before = GC.GetAllocatedBytesForCurrentThread();
        GenerationFailure<InspectionRaster> failure = ProfileTestSupport.Failure(
            InspectionRaster.Create(width, height, values, PatternTestSupport.InspectionBudget()));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, failure.Error.Code);
        Assert.AreEqual("atlas.pattern.raster-budget", failure.Error.Stage);
        Assert.AreEqual(0, values.EnumerationCount);
        Assert.AreEqual(0, values.IndexAccessCount);
        Assert.IsLessThan(64 * 1024L, allocated);
    }

    [TestMethod]
    public void MapPairAndAnalysisWork_ArePreflightedAgainstExplicitBudgets()
    {
        var permissive = new PatternInspectionBudget(100, 100, 200);
        InspectionRaster final = ProfileTestSupport.Success(InspectionRaster.Create(4, 4, new ushort[16], permissive));
        InspectionRaster edges = ProfileTestSupport.Success(InspectionRaster.Create(4, 4, new ushort[16], permissive));
        var pairLimited = new PatternInspectionBudget(100, 100, 100);
        GenerationFailure<PatternInspectionMaps> pairFailure = ProfileTestSupport.Failure(
            PatternInspectionMaps.Create(final, edges, pairLimited));
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, pairFailure.Error.Code);
        Assert.AreEqual("atlas.pattern.map-budget", pairFailure.Error.Stage);

        PatternDiagnosticPolicy normal = PatternTestSupport.CalibratedPolicy();
        var workLimited = new PatternDiagnosticPolicy(
            normal.PolicyId,
            normal.PolicyVersion,
            normal.EdgeGradientThreshold,
            normal.EdgeAlignmentThresholdPpm,
            normal.PeriodicityThresholdPpm,
            normal.MinimumPeriodLag,
            normal.MaximumPeriodLag,
            maximumAnalysisWorkUnits: 1_000,
            maximumCorpusAnalysisWorkUnits: 1_000);
        GenerationFailure<PatternAnalysisEstimate> estimateFailure = ProfileTestSupport.Failure(
            PatternDiagnostics.Estimate(PatternTestSupport.VisibleVoronoiWitness(), workLimited));
        GenerationFailure<PatternDiagnosticReport> analysisFailure = ProfileTestSupport.Failure(
            PatternDiagnostics.Analyze(PatternTestSupport.VisibleVoronoiWitness(), workLimited));
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, estimateFailure.Error.Code);
        Assert.AreEqual("atlas.pattern.analysis-budget", estimateFailure.Error.Stage);
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, analysisFailure.Error.Code);
        Assert.AreEqual("atlas.pattern.analysis-budget", analysisFailure.Error.Stage);
    }

    [TestMethod]
    public void DiagnosticInputsAndOutputsRejectUnknownOrInvalidPolicies()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PatternDiagnosticPolicy(
            "invalid",
            policyVersion: 1,
            edgeGradientThreshold: 1,
            edgeAlignmentThresholdPpm: 1_000_001,
            periodicityThresholdPpm: 1,
            minimumPeriodLag: 2,
            maximumPeriodLag: 8,
            maximumAnalysisWorkUnits: 100_000,
            maximumCorpusAnalysisWorkUnits: 1_000_000));

        PatternInspectionMaps maps = PatternTestSupport.VisibleVoronoiWitness();
        PatternDiagnosticPolicy tooLarge = new(
            "unsupported-raster",
            policyVersion: 1,
            edgeGradientThreshold: 1,
            edgeAlignmentThresholdPpm: 1,
            periodicityThresholdPpm: 1,
            minimumPeriodLag: 2,
            maximumPeriodLag: 64,
            maximumAnalysisWorkUnits: 100_000,
            maximumCorpusAnalysisWorkUnits: 1_000_000);
        GenerationFailure<PatternDiagnosticReport> failure = ProfileTestSupport.Failure(
            PatternDiagnostics.Analyze(maps, tooLarge));
        Assert.AreEqual(GenerationFailureCode.InvalidInput, failure.Error.Code);
        Assert.AreEqual("atlas.pattern.policy-range", failure.Error.Stage);
    }

    [TestMethod]
    public void PolicyIdentity_CoversEveryEffectiveThresholdLagAndBudget()
    {
        PatternDiagnosticPolicy first = PatternTestSupport.CalibratedPolicy();
        var second = new PatternDiagnosticPolicy(
            first.PolicyId,
            first.PolicyVersion,
            edgeGradientThreshold: first.EdgeGradientThreshold + 1,
            first.EdgeAlignmentThresholdPpm,
            first.PeriodicityThresholdPpm,
            first.MinimumPeriodLag,
            first.MaximumPeriodLag,
            first.MaximumAnalysisWorkUnits,
            first.MaximumCorpusAnalysisWorkUnits);
        PatternInspectionMaps maps = PatternTestSupport.VisibleVoronoiWitness();
        PatternDiagnosticReport firstReport = ProfileTestSupport.Success(PatternDiagnostics.Analyze(maps, first));
        PatternDiagnosticReport secondReport = ProfileTestSupport.Success(PatternDiagnostics.Analyze(maps, second));
        PatternSensitivityReport firstSensitivity = ProfileTestSupport.Success(PatternSensitivityEvaluator.Evaluate(
            new[] { new PatternCorpusCase("same-result", true, maps) }, first));
        PatternSensitivityReport secondSensitivity = ProfileTestSupport.Success(PatternSensitivityEvaluator.Evaluate(
            new[] { new PatternCorpusCase("same-result", true, maps) }, second));

        Assert.AreNotEqual(first.ContentChecksum, second.ContentChecksum);
        Assert.AreNotEqual(firstReport.PolicyChecksum, secondReport.PolicyChecksum);
        Assert.AreNotEqual(firstReport.ContentChecksum, secondReport.ContentChecksum);
        Assert.AreNotEqual(firstSensitivity.PolicyChecksum, secondSensitivity.PolicyChecksum);
        Assert.AreNotEqual(firstSensitivity.ContentChecksum, secondSensitivity.ContentChecksum);
        Assert.AreEqual(firstReport.EdgeAlignmentScorePpm, secondReport.EdgeAlignmentScorePpm);
        Assert.AreEqual(firstReport.AxisPeriodicityScorePpm, secondReport.AxisPeriodicityScorePpm);
    }

    [TestMethod]
    public void TenThousandHomogeneousCases_DoNotOverflowSensitivityArithmetic()
    {
        PatternInspectionMaps witness = PatternTestSupport.VisibleVoronoiWitness();
        PatternCorpusCase[] corpus = Enumerable.Range(0, 10_000)
            .Select(index => new PatternCorpusCase($"visible-{index:D5}", true, witness))
            .ToArray();

        PatternSensitivityReport report = ProfileTestSupport.Success(
            PatternSensitivityEvaluator.Evaluate(corpus, PatternTestSupport.CalibratedPolicy()));

        Assert.AreEqual(10_000, report.TruePositiveCount);
        Assert.AreEqual(0, report.FalseNegativeCount);
        Assert.AreEqual(PatternDiagnostics.PartsPerMillion, report.SensitivityPpm);
        Assert.AreEqual(0, report.SpecificityPpm);
        Assert.HasCount(10_000, report.Cases);
    }

    [TestMethod]
    public void QualitativeEvidence_WritesSeparateFinalEdgeMapsAndSensitivityReport()
    {
        string repositoryRoot = PatternTestSupport.FindRepositoryRoot();
        string outputDirectory = Path.Combine(repositoryRoot, ".local", "L02C", "qualitative-evidence");
        Directory.CreateDirectory(outputDirectory);
        PatternInspectionMaps witness = PatternTestSupport.VisibleVoronoiWitness();
        PatternInspectionMaps control = PatternTestSupport.SmoothField();
        PatternDiagnosticPolicy policy = PatternTestSupport.CalibratedPolicy();
        PatternDiagnosticReport diagnostic = ProfileTestSupport.Success(PatternDiagnostics.Analyze(witness, policy));
        PatternDiagnosticReport controlDiagnostic = ProfileTestSupport.Success(PatternDiagnostics.Analyze(control, policy));
        PatternSensitivityReport sensitivity = ProfileTestSupport.Success(PatternSensitivityEvaluator.Evaluate(
        [
            new PatternCorpusCase("visible-voronoi", true, witness),
            new PatternCorpusCase("smooth", false, PatternTestSupport.SmoothField()),
            new PatternCorpusCase("periodic-non-voronoi", false, PatternTestSupport.PeriodicNonVoronoi()),
            new PatternCorpusCase("low-contrast-voronoi", true, PatternTestSupport.LowContrastWitness()),
        ], policy));

        string finalPath = Path.Combine(outputDirectory, "visible-voronoi-final.bmp");
        string edgesPath = Path.Combine(outputDirectory, "visible-voronoi-edges.bmp");
        string controlFinalPath = Path.Combine(outputDirectory, "smooth-anisotropic-final.bmp");
        string controlEdgesPath = Path.Combine(outputDirectory, "smooth-anisotropic-edges.bmp");
        string reportPath = Path.Combine(outputDirectory, "sensitivity-report.json");
        File.WriteAllBytes(finalPath, InspectionMapRenderer.RenderBitmap24(witness.FinalOutput));
        File.WriteAllBytes(edgesPath, InspectionMapRenderer.RenderBitmap24(witness.AtlasEdges));
        File.WriteAllBytes(controlFinalPath, InspectionMapRenderer.RenderBitmap24(control.FinalOutput));
        File.WriteAllBytes(controlEdgesPath, InspectionMapRenderer.RenderBitmap24(control.AtlasEdges));
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            status = "REVIEW_REQUIRED",
            requirement = "R02-06",
            test = "T02-06-analytical-review",
            detectorPolicy = policy.PolicyId,
            detectorPolicyVersion = policy.PolicyVersion,
            detectorPolicyChecksum = policy.ContentChecksum.ToString(),
            policy.EdgeGradientThreshold,
            policy.EdgeAlignmentThresholdPpm,
            policy.PeriodicityThresholdPpm,
            policy.MinimumPeriodLag,
            policy.MaximumPeriodLag,
            policy.MaximumAnalysisWorkUnits,
            policy.MaximumCorpusAnalysisWorkUnits,
            positiveWitness = new
            {
                expectedSignal = true,
                actualSignal = diagnostic.SignalDetected,
                mapsChecksum = witness.ContentChecksum.ToString(),
                diagnosticChecksum = diagnostic.ContentChecksum.ToString(),
                diagnostic.EdgeAlignmentScorePpm,
                diagnostic.AxisPeriodicityScorePpm,
            },
            smoothAnisotropicControl = new
            {
                expectedSignal = false,
                actualSignal = controlDiagnostic.SignalDetected,
                mapsChecksum = control.ContentChecksum.ToString(),
                diagnosticChecksum = controlDiagnostic.ContentChecksum.ToString(),
                controlDiagnostic.EdgeAlignmentScorePpm,
                controlDiagnostic.AxisPeriodicityScorePpm,
            },
            sensitivityChecksum = sensitivity.ContentChecksum.ToString(),
            truePositiveCount = sensitivity.TruePositiveCount,
            trueNegativeCount = sensitivity.TrueNegativeCount,
            falsePositiveCount = sensitivity.FalsePositiveCount,
            falseNegativeCount = sensitivity.FalseNegativeCount,
            limitations = sensitivity.Limitations,
            evidenceMaps = new[]
            {
                Path.GetFileName(controlFinalPath),
                Path.GetFileName(controlEdgesPath),
                Path.GetFileName(finalPath),
                Path.GetFileName(edgesPath),
            },
        }, new JsonSerializerOptions { WriteIndented = true }));

        Assert.IsTrue(File.Exists(finalPath));
        Assert.IsTrue(File.Exists(edgesPath));
        Assert.IsTrue(File.Exists(controlFinalPath));
        Assert.IsTrue(File.Exists(controlEdgesPath));
        Assert.IsTrue(File.Exists(reportPath));
        CollectionAssert.AreEqual(new byte[] { (byte)'B', (byte)'M' }, File.ReadAllBytes(finalPath)[..2]);
        CollectionAssert.AreEqual(new byte[] { (byte)'B', (byte)'M' }, File.ReadAllBytes(edgesPath)[..2]);
        CollectionAssert.AreNotEqual(File.ReadAllBytes(finalPath), File.ReadAllBytes(edgesPath));
        CollectionAssert.AreNotEqual(File.ReadAllBytes(controlFinalPath), File.ReadAllBytes(controlEdgesPath));
        Assert.IsTrue(diagnostic.SignalDetected);
        Assert.IsFalse(controlDiagnostic.SignalDetected);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(reportPath));
        Assert.AreEqual("REVIEW_REQUIRED", document.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(1, document.RootElement.GetProperty("falsePositiveCount").GetInt32());
        Assert.AreEqual(1, document.RootElement.GetProperty("falseNegativeCount").GetInt32());
    }

    private sealed class ImpossibleRasterList : IReadOnlyList<ushort>
    {
        internal ImpossibleRasterList(int count) => Count = count;

        public int Count { get; }

        public ushort this[int index]
        {
            get
            {
                IndexAccessCount++;
                throw new NotSupportedException();
            }
        }

        public int EnumerationCount { get; private set; }

        public int IndexAccessCount { get; private set; }

        public IEnumerator<ushort> GetEnumerator()
        {
            EnumerationCount++;
            throw new InvalidOperationException("Impossible raster must be rejected before enumeration.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
